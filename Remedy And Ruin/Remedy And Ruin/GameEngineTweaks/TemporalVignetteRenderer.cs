using System;
using Remedy_And_Ruin.GameEngineTweaks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin
{
    /// <summary>
    /// Full-screen post-process effect for Temporal Fog and Skull-Strain's bone-injury
    /// headache (design doc: Part 3, Confusion/Brain Fog). Each mode has its own look:
    /// Temporal Fog is a flat muted sepia/desaturation color grade, with no camera motion.
    /// Skull-Strain's concussion applies a periodic horizontal shear plus a
    /// contrast/brightness boost, and drives a slow drunken camera sway (see ApplyWobble).
    /// Chromatic aberration/blur is shared by both; see the shader file.
    ///
    /// TempFogStrength, MindPoisonFogStrength, and ConcussionStrength are fully independent
    /// state and layer if active at once - a player could have a physical head injury while
    /// also standing in a temporal rift's fog while also poisoned. TempFogStrength and
    /// MindPoisonFogStrength both drive the same sepia/desaturation visual (mode 0) since
    /// they're the same on-screen symptom from two different causes, but are kept as
    /// separate fields - combined via Max at render time - specifically so neither cause can
    /// silently overwrite the other's value; see MindPoisonFogStrength's own doc comment.
    /// The mode-0 pass (if either is active) renders first into an offscreen "ping" buffer
    /// sized to the screen; Concussion (if active) then renders on top of that result rather
    /// than the raw scene, so both effects visibly stack. The offscreen buffer is only used
    /// when both are active at once - if only one is active, it renders straight to the
    /// visible framebuffer, no ping buffer involved.
    /// </summary>
    public class TemporalVignetteRenderer : IRenderer
    {
        private class EffectState
        {
            public float Strength;
            public float SecondsToNextPulseRoll;
            public float PulseSecondsLeft;
            public float CurrentWobbleStrength;
            public float LastYawOffset;
            public float LastPitchOffset;
        }

        private const float GlitchRollChance = 0.4f;
        private const float GlitchPulseDuration = 2.2f;

        /// <summary>
        /// Ceiling for the wobble curve's intensity term (see ApplyWobble), on the same
        /// 0-100 scale vanilla's DrunkPerceptionEffect uses for alcohol intoxication - 50
        /// is half of vanilla's own max. This is an independent sway, not a hook into
        /// vanilla's intoxication system; only the curve shape is borrowed.
        /// </summary>
        private const float MaxWobbleIntensity = 50f;

        private readonly ICoreClientAPI capi;
        private readonly Random rand = new Random();
        private MeshRef quadRef;
        private IShaderProgram prog;
        private float timeCounter;

        private FrameBufferRef pingFb;
        private int pingWidth;
        private int pingHeight;

        private readonly EffectState tempFog = new EffectState();
        private readonly EffectState mindPoisonFog = new EffectState();
        private readonly EffectState concussion = new EffectState();
        private readonly EffectState drunkWobble = new EffectState();

        /// <summary>
        /// Neurotoxic Poison's own dizziness sway, read directly off the local player's
        /// WatchedAttributes every frame (EntityBehaviorRemedyEffects.NeurotoxicDrunkWobbleAttributeKey,
        /// kept in sync server-side by StartDrunkWobbleContribution) rather than driven through
        /// DrunkWobbleStrength's public setter - mirrors vanilla's own DrunkPerceptionEffect,
        /// which reads its "intoxication" WatchedAttributes float the same way. Kept as its own
        /// EffectState (combined with drunkWobble only via both independently calling ApplyWobble,
        /// same as concussion/drunkWobble already coexist) so a debug .rrbrainrot call and a real
        /// Neurotoxic exposure can't stomp each other's value.
        /// </summary>
        private readonly EffectState neurotoxicWobble = new EffectState();

        /// <summary>The real Temporal Fog condition's own strength (low Temporal Stability, Mind Tonic overdose), 0-1. 0 disables it entirely. See MindPoisonFogStrength for why Mind Poison uses a separate field for the same visual.</summary>
        public float TempFogStrength
        {
            get => tempFog.Strength;
            set => tempFog.Strength = value;
        }

        /// <summary>
        /// Mind Poison's own contribution to Temporal Fog's sepia/desaturation visual, 0-1. 0
        /// disables it entirely. Kept separate from TempFogStrength (not writing the same
        /// field) so Mind Poison and the real Temporal Fog condition can be active
        /// independently, in any order, without one overwriting the other's value - the two
        /// are combined via Max at render time instead. Callers that specifically need "is
        /// Mind Poison active" (e.g. Hallucination's trigger) should read this, not
        /// TempFogStrength, which can also be nonzero for reasons unrelated to Mind Poison.
        /// </summary>
        public float MindPoisonFogStrength
        {
            get => mindPoisonFog.Strength;
            set => mindPoisonFog.Strength = value;
        }

        /// <summary>Skull-Strain's bone-injury headache, 0-1. 0 disables it entirely. Drives both the horizontal-shear/contrast-boost vignette and the drunken camera sway.</summary>
        public float ConcussionStrength
        {
            get => concussion.Strength;
            set => concussion.Strength = value;
        }

        /// <summary>Mind Poison's drunken camera sway only, 0-1 - the same ApplyWobble curve Concussion uses, with no shear/contrast-boost vignette attached. Subject to the same cWobble accessibility toggle as Concussion's sway.</summary>
        public float DrunkWobbleStrength
        {
            get => drunkWobble.Strength;
            set => drunkWobble.Strength = value;
        }

        public double RenderOrder => 0.05;
        public int RenderRange => 1;

        public TemporalVignetteRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "remedyandruin-temporalvignette");
            quadRef = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
            capi.Event.ReloadShader += LoadShader;
            LoadShader();
            tempFog.SecondsToNextPulseRoll = NextGlitchRollDelay();
            mindPoisonFog.SecondsToNextPulseRoll = NextGlitchRollDelay();
            concussion.SecondsToNextPulseRoll = NextGlitchRollDelay();
        }

        private float NextGlitchRollDelay()
        {
            return 8f + (float)rand.NextDouble() * 4f;
        }

        public bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();
            prog.AssetDomain = "remedyandruin";
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            capi.Shader.RegisterFileShaderProgram("temporalvignette", prog);
            bool ok = prog.Compile();
            if (!ok)
            {
                capi.Logger.Error("remedyandruin: temporalvignette shader failed to compile/link - vignette effect disabled until fixed.");
            }
            return ok;
        }

        // Rolls for a pulse every ~8-12s while active at all; the timer sits idle while
        // Strength is 0 so it doesn't fire the instant the condition starts.
        private void UpdatePulse(EffectState state, float deltaTime)
        {
            if (state.Strength > 0.0001f)
            {
                state.SecondsToNextPulseRoll -= deltaTime;
                if (state.SecondsToNextPulseRoll <= 0f)
                {
                    state.SecondsToNextPulseRoll = NextGlitchRollDelay();
                    if (rand.NextDouble() < GlitchRollChance)
                    {
                        state.PulseSecondsLeft = GlitchPulseDuration;
                    }
                }
            }
            if (state.PulseSecondsLeft > 0f)
            {
                state.PulseSecondsLeft = Math.Max(0f, state.PulseSecondsLeft - deltaTime);
            }
        }

        // 0 at pulse start, 1 at pulse end (and while no pulse is active) - the shader turns
        // this into a smooth grow-then-fade arc via sin(progress * PI).
        private static float GetProgress(EffectState state)
        {
            if (state.PulseSecondsLeft <= 0f) return 0f;
            float elapsed = GlitchPulseDuration - state.PulseSecondsLeft;
            return elapsed / GlitchPulseDuration;
        }

        // (Re)creates the offscreen buffer Temporal Fog renders into when Concussion also
        // needs to layer on top of it, sized to match the current screen resolution.
        private void EnsurePingBuffer(int width, int height)
        {
            if (pingFb != null && pingWidth == width && pingHeight == height) return;

            if (pingFb != null)
            {
                capi.Render.DestroyFrameBuffer(pingFb);
                pingFb = null;
            }

            pingFb = capi.Render.CreateFrameBuffer(new FramebufferAttrs("remedyandruin-vignette-ping", width, height)
            {
                Attachments = new FramebufferAttrsAttachment[]
                {
                    new FramebufferAttrsAttachment
                    {
                        AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                        Texture = new RawTexture { Width = width, Height = height, TextureId = 0 }
                    }
                }
            });
            pingWidth = width;
            pingHeight = height;
        }

        /// <summary>
        /// A slow drunken-stumble camera sway, using vanilla DrunkPerceptionEffect's
        /// multi-sine yaw/pitch curve shape (see MaxWobbleIntensity), applied independently
        /// of vanilla's intoxication system. Shared by Skull-Strain's concussion and Mind
        /// Poison's own sway (each keeps its own EffectState/Strength, so the two still
        /// scale and fade independently) - both are gated by the same cWobble accessibility
        /// setting, which controls camera-sway motion sickness in general rather than being
        /// scoped to concussion specifically.
        ///
        /// Only the delta between this frame's and last frame's target offset is ever
        /// applied to MouseYaw/MousePitch, rather than the target offset itself. This
        /// curve's terms are low-frequency (multi-second periods) and hold the same sign
        /// for seconds at a time, so applying the raw target every frame - as vanilla's
        /// own version does - accumulates into continuous, framerate-dependent spinning
        /// instead of a bounded oscillation. Tracking and subtracting the previous
        /// applied offset keeps the sway centered on the player's actual look direction.
        /// </summary>
        private void ApplyWobble(EffectState state, float deltaTime)
        {
            state.CurrentWobbleStrength += (state.Strength - state.CurrentWobbleStrength) * Math.Min(deltaTime, 1f);

            bool active = state.CurrentWobbleStrength > 0.01f
                && capi.World.Player.CameraMode == EnumCameraMode.FirstPerson
                && (Remedy_And_RuinModSystem.Config?.concussionWobbleEnabled ?? true);

            float targetYawOffset = 0f;
            float targetPitchOffset = 0f;
            if (active)
            {
                float intensity = MaxWobbleIntensity * GameMath.Clamp(state.CurrentWobbleStrength, 0f, 1f);
                float f = intensity / 250f;
                float accum = (float)((double)capi.InWorldEllapsedMilliseconds / 3000.0 % 100.0 * Math.PI);
                targetPitchOffset = (float)(Math.Cos(accum / 1.15) + Math.Cos(accum / 1.35f)) * f / 2f;
                targetYawOffset = (float)(Math.Sin(accum / 1.1) + Math.Sin(accum / 1.5f) + Math.Sin(accum / 5f) * 0.2) * f;
            }

            // Only the change since last frame's applied offset ever reaches MouseYaw/Pitch -
            // this is what keeps the sway bounded (an oscillation around real look direction)
            // instead of an unbounded accumulation. Runs this branch even when inactive, so a
            // mid-swing offset unwinds cleanly instead of leaving the camera stuck off-center.
            capi.Input.MouseYaw += targetYawOffset - state.LastYawOffset;
            capi.Input.MousePitch += targetPitchOffset - state.LastPitchOffset;
            state.LastYawOffset = targetYawOffset;
            state.LastPitchOffset = targetPitchOffset;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            timeCounter += deltaTime;
            UpdatePulse(tempFog, deltaTime);
            UpdatePulse(mindPoisonFog, deltaTime);
            UpdatePulse(concussion, deltaTime);

            // Wobble is a camera-input effect, not a draw call - keep it working even if
            // the vignette shader itself failed to compile/link. Temporal Fog gets no
            // camera motion at all (just the color grade), so only Concussion and Mind
            // Poison's drunkWobble sway - drunkWobble shares ApplyWobble's curve but has no
            // draw pass of its own, so it never picks up Concussion's shear/contrast-boost look.
            EntityPlayer localPlayer = capi.World?.Player?.Entity;
            if (localPlayer != null)
            {
                neurotoxicWobble.Strength = localPlayer.WatchedAttributes.GetFloat(EntityBehaviorRemedyEffects.NeurotoxicDrunkWobbleAttributeKey);
            }

            if (!capi.IsGamePaused)
            {
                ApplyWobble(concussion, deltaTime);
                ApplyWobble(drunkWobble, deltaTime);
                ApplyWobble(neurotoxicWobble, deltaTime);
            }

            // TempFogStrength and MindPoisonFogStrength share the same mode-0 visual but are
            // independent fields (see MindPoisonFogStrength's doc comment) - whichever is
            // currently stronger drives both the strength and the glitch-pulse progress passed
            // to that one draw call.
            EffectState dominantFog = tempFog.Strength >= mindPoisonFog.Strength ? tempFog : mindPoisonFog;
            float fogStrength = Math.Max(tempFog.Strength, mindPoisonFog.Strength);

            bool tempFogActive = fogStrength > 0.0001f;
            bool concussionActive = concussion.Strength > 0.0001f;
            if (!tempFogActive && !concussionActive) return;
            if (prog == null || prog.Disposed || prog.LoadError) return;

            int width = capi.Render.FrameWidth;
            int height = capi.Render.FrameHeight;

            // Bracket GL state explicitly - we run before Ortho/GUI in the frame (AfterBlit),
            // so any depth/blend state we leave dirty here can corrupt the HUD drawn right
            // after us. Matches vanilla's own vignette pass (ClientPlatformWindows.
            // RenderFinalComposition): depth test off, blending left ON (GUI assumes it's
            // already enabled going into Ortho and doesn't re-enable it itself) - our shader
            // always outputs alpha 1.0, so blending stays a no-op for our own draw either way.
            capi.Render.GlToggleBlend(true);
            capi.Render.GLDisableDepthTest();
            capi.Render.GLDepthMask(false);

            int inputTexture = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0];

            if (tempFogActive)
            {
                if (concussionActive)
                {
                    // Concussion still needs to render on top of this result, so render
                    // Temporal Fog into the offscreen ping buffer rather than straight to
                    // the screen.
                    EnsurePingBuffer(width, height);
                    capi.Render.CurrentFrameBuffer = pingFb;
                    DrawPass(inputTexture, fogStrength, GetProgress(dominantFog), mode: 0, width, height);
                    // Setting CurrentFrameBuffer = null binds the default framebuffer but,
                    // per the engine's own setter, does not reset the viewport - relies on
                    // ping always being sized to match width/height exactly, so the
                    // viewport the ping bind left behind is already correct here too.
                    capi.Render.CurrentFrameBuffer = null;
                    inputTexture = pingFb.ColorTextureIds[0];
                }
                else
                {
                    // Nothing else will draw this frame - render Temporal Fog straight to
                    // the visible framebuffer.
                    DrawPass(inputTexture, fogStrength, GetProgress(dominantFog), mode: 0, width, height);
                }
            }

            if (concussionActive)
            {
                // Sampling inputTexture here: either the raw scene (Temporal Fog wasn't
                // active) or Temporal Fog's own result (it was) - either way this pass
                // writes the final, fully-composited image to the visible framebuffer.
                DrawPass(inputTexture, concussion.Strength, GetProgress(concussion), mode: 1, width, height);
            }

            capi.Render.GLDepthMask(true);
            capi.Render.GLEnableDepthTest();
        }

        // mode 0 = Temporal Fog (sepia/desaturation), mode 1 = Skull-Strain concussion
        // (horizontal shear + contrast/brightness). See temporalvignette.fsh.
        private void DrawPass(int sampleTextureId, float strength, float glitchProgress, int mode, int width, int height)
        {
            prog.Use();
            prog.BindTexture2D("primaryFb", sampleTextureId, 0);
            prog.Uniform("invFrameSize", 1f / (float)width, 1f / (float)height);
            prog.Uniform("strength", strength);
            prog.Uniform("glitchProgress", glitchProgress);
            prog.Uniform("mode", mode);
            prog.Uniform("timeCounter", timeCounter);
            capi.Render.RenderMesh(quadRef);
            prog.Stop();
        }

        public void Dispose()
        {
            quadRef?.Dispose();
            prog?.Dispose();
            if (pingFb != null)
            {
                capi.Render.DestroyFrameBuffer(pingFb);
                pingFb = null;
            }
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            capi.Event.ReloadShader -= LoadShader;
        }
    }
}
