using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to player entities (see Remedy And RuinModSystem.StartServerSide). Owns all
    /// poison/remedy effect state and application.
    ///
    /// OnItemConsumed: called by Patch_RawEating/Patch_MealEating with a consumed item and its
    /// remedyandruinEffect (or remedyandruinEffectByType) attribute, unparsed.
    /// </summary>
    public class EntityBehaviorRemedyEffects : EntityBehavior
    {
        public ITreeAttribute RREffects
        {
            get => entity.WatchedAttributes.GetTreeAttribute("remedyandruinEffects");
            set
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", value);
                MarkDirty();
            }
        }
        public void MarkDirty() => entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects");
        public void MarkDirty(string key)
        {
            entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects/" + key);
            parseEffectsAndApply();
        }

        private EffectThreadManager threadManager;

        //============== PPEFFECTS ==============//

        public TreeArrayAttribute RRPoisonEffects
        {
            get => RREffects["rrpoisons"] as TreeArrayAttribute;
            set
            {
                if(value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpoisons"] = arrvalue;
                    MarkDirty("rrpoisons");
                }
            }
        }
        public TreeArrayAttribute RRIllnessEffects
        {
            get => RREffects["rrillness"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrillness"] = arrvalue;
                    MarkDirty("rrillness");
                }
            }
        }
        public TreeArrayAttribute RRPotionEffects
        {
            get => RREffects["rrpotions"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpotions"] = arrvalue;
                    MarkDirty("rrpotions");
                }
            }
        }

        public bool lastKnownEffectsAdvanceOffline
        {
            get => RREffects.GetBool("lastKnownEffectsAdvanceOffline");
            set
            {
                RREffects.SetBool("lastKnownEffectsAdvanceOffline", value);
                MarkDirty();
            }
        }

        //============== TOLERANCES ==============//

        public int toxicTolerance
        {
            get => RREffects.GetInt("toxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("toxicTolerance", value);
                MarkDirty();
            }
        }

        public int noxiousTolerance
        {
            get => RREffects.GetInt("noxiousTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("noxiousTolerance", value);
                MarkDirty();
            }
        }

        public int cardiacTolerance
        {
            get => RREffects.GetInt("cardiacTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("cardiacTolerance", value);
                MarkDirty();
            }
        }

        public int neurotoxicTolerance
        {
            get => RREffects.GetInt("neurotoxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("neurotoxicTolerance", value);
                MarkDirty();
            }
        }

        public int brainrotTolerance
        {
            get => RREffects.GetInt("brainrotTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("brainrotTolerance", value);
                MarkDirty();
            }
        }

        private int GetTolerance(string cluster)
        {
            switch (cluster)
            {
                case "TOXICPOISON": return toxicTolerance;
                case "NOXIOUSPOISON": return noxiousTolerance;
                case "CARDIACPOISON": return cardiacTolerance;
                case "NEUROTOXICPOISON": return neurotoxicTolerance;
                case "MINDPOISON": return brainrotTolerance;
                default: return 0;
            }
        }

        private void SetTolerance(string cluster, int value)
        {
            switch (cluster)
            {
                case "TOXICPOISON": toxicTolerance = value; break;
                case "NOXIOUSPOISON": noxiousTolerance = value; break;
                case "CARDIACPOISON": cardiacTolerance = value; break;
                case "NEUROTOXICPOISON": neurotoxicTolerance = value; break;
                case "MINDPOISON": brainrotTolerance = value; break;
            }
        }

        private double GetLastExposureDay(string cluster) => RREffects.GetDouble(cluster + "LastExposureDay", 0.0);

        private void SetLastExposureDay(string cluster, double value)
        {
            RREffects.SetDouble(cluster + "LastExposureDay", value);
            MarkDirty();
        }

        private float GetToleranceDecayAccumulator(string cluster) => RREffects.GetFloat(cluster + "ToleranceDecayAccumulator", 0f);

        private void SetToleranceDecayAccumulator(string cluster, float value)
        {
            RREffects.SetFloat(cluster + "ToleranceDecayAccumulator", value);
            MarkDirty();
        }

        public bool GetPendingToleranceCredit(string cluster) => RREffects.GetBool(cluster + "PendingToleranceCredit", false);

        public void SetPendingToleranceCredit(string cluster, bool value)
        {
            RREffects.SetBool(cluster + "PendingToleranceCredit", value);
            MarkDirty();
        }

        public void RegisterSurvivedExposure(string cluster)
        {
            int current = GetTolerance(cluster);
            SetTolerance(cluster, Math.Min(27, current + 1));
            SetLastExposureDay(cluster, entity.World.Calendar.TotalDays);
            SetToleranceDecayAccumulator(cluster, 0f);
        }

        private static readonly string[] ToleranceClusters = { "TOXICPOISON", "NOXIOUSPOISON", "CARDIACPOISON", "NEUROTOXICPOISON", "MINDPOISON" };

        private long toleranceDecayListenerId;
        private int lastToleranceDecayCheckDay;

        public void ResetToleranceDecayCheckpoint()
        {
            lastToleranceDecayCheckDay = (int)Math.Floor(entity.World.Calendar.TotalDays);
        }

        private void DecayTolerances(float dt)
        {
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            int daysPassed = today - lastToleranceDecayCheckDay;
            if (daysPassed <= 0) return;
            lastToleranceDecayCheckDay = today;

            double daysPerMonth = entity.World.Calendar.DaysPerMonth;

            foreach (string cluster in ToleranceClusters)
            {
                int tolerance = GetTolerance(cluster);
                if (tolerance <= 0) continue;

                double lastExposureDay = GetLastExposureDay(cluster);
                double daysSinceExposure = entity.World.Calendar.TotalDays - lastExposureDay;
                if (daysSinceExposure < daysPerMonth) continue; // still in the grace period

                float accumulator = GetToleranceDecayAccumulator(cluster) + (27f * 0.05f * daysPassed);
                while (accumulator >= 1f && tolerance > 0)
                {
                    tolerance--;
                    accumulator -= 1f;
                }
                SetTolerance(cluster, tolerance);
                SetToleranceDecayAccumulator(cluster, accumulator);
            }
        }

        //============== TOXICITY ==============//

        public float ToxicityCounter
        {
            get => RREffects.GetFloat("toxicityCounter", 0f);
            set
            {
                value = GameMath.Clamp(value, 0f, float.MaxValue);
                RREffects.SetFloat("toxicityCounter", value);
                MarkDirty();
            }
        }

        public void IncreaseToxicity(string cluster, float amount)
        {
            float previous = ToxicityCounter;
            float updated = previous + amount;
            ToxicityCounter = updated;

            if (previous < Remedy_And_RuinModSystem.Config.toxicityOverdoseThreshold
                && updated >= Remedy_And_RuinModSystem.Config.toxicityOverdoseThreshold)
            {
                TriggerOverdose(cluster);
            }
        }

        private void TriggerOverdose(string cluster)
        {
            /*
             * PLACEHOLDER
             * §6's overdose-effect-per-potion-type table decides what actually happens here, once
             * Plan 12/13 builds real potion effects to construct an overdose instance from. cluster
             * identifies which potion caused this crossing (the only input this method needs later).
             */
        }

        private long toxicityDecayListenerId;

        private void DecayToxicity(float dt)
        {
            float gameSpeedMultiplier = entity.World.Calendar.SpeedOfTime * entity.World.Calendar.CalendarSpeedMul / 30f;
            float decayAmount = Remedy_And_RuinModSystem.Config.toxicityDecayPerRealSecond * gameSpeedMultiplier;
            if (decayAmount <= 0f) return;

            float current = ToxicityCounter;
            if (current <= 0f) return;

            ToxicityCounter = Math.Max(0f, current - decayAmount);
        }

        //============== PROPERTIES ===============//

        bool     drankAntidote = false;
        DateTime timeAntidoteConsumed = DateTime.MinValue;

        //============== CONSTRUCTORS ==============//

        public EntityBehaviorRemedyEffects(Entity entity) : base(entity)
        {
            //if (entity.World.Side != EnumAppSide.Server) return;
            if (!entity.WatchedAttributes.HasAttribute("remedyandruinEffects"))
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", new TreeAttribute());
            }
            if (RREffects["rrpoisons"] == null)
            {
                RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpoisons");
            }
            if (RREffects["rrillness"] == null)
            {
                RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrillness");
            }
            if (RREffects["rrpotions"] == null)
            {
                RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpotions");
            }
            threadManager = new EffectThreadManager(entity);
            toxicityDecayListenerId = entity.World.RegisterGameTickListener(DecayToxicity, 1000);
            lastToleranceDecayCheckDay = (int)Math.Floor(entity.World.Calendar.TotalDays);
            toleranceDecayListenerId = entity.World.RegisterGameTickListener(DecayTolerances, 60000);
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            if (toxicityDecayListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(toxicityDecayListenerId);
            }
            if (toleranceDecayListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(toleranceDecayListenerId);
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            threadManager.HandleForcefulEnd();
        }

        public void DestroyProgress()
        {
            //I warned you not to.
            brainrotTolerance = 0;
            cardiacTolerance = 0;
            neurotoxicTolerance = 0;
            noxiousTolerance = 0;
            toxicTolerance = 0;
            RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpoisons");
            RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrillness");
            RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpotions");
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public bool HandleDisconnect() => threadManager.HandleDisconnect();

        public bool HandleGameWorldSaving() => threadManager.HandleGameWorldSaving();

        public void ReconstructActiveEffectsOnLogin() => parseEffectsAndApply();

        public override string PropertyName()
        {
            return "remedyandruinEffects";
        }

        //============== Data Structures ==============//

        public enum EffectCluster
        {
            ANALGESIC,
            ANTIDOTE,
            ANTINAUSEA,
            ANTISEPTIC,
            ANTIVIRAL,
            MINDTONIC,
            SEDATIVE,
            TONIC,
            TOXICPOISON,
            NOXIOUSPOISON,
            CARDIACPOISON,
            NEUROTOXICPOISON,
            MINDPOISON,
            TOPICALOINTMENT
        }

        public enum EffectType
        {
            POISON,
            ILLNESS
        }

        public struct EffectStruct (EffectCluster inval)
        {
            public EffectCluster cluster = inval;
            public bool   isConcentrated        = false; //base/potion or concentrate?
            public bool   isPoison              = false; //poison or potion?
            public double timestarted          = 0.0;    //used by all effects with duration
            public double timeleft             = 0.0;    //used by all effects with duration
            public float  effectMultiplier      = 0.0f;  //used by all poisons
            public float  onsetMultiplier       = 0.0f;  //used by some poisons
            public float  toxicEffectMultiplier = 0.0f;  //used by dual-effect mushrooms
            public float  toxicOnsetMultiplier  = 0.0f;  //used by dual-effect mushrooms
        }

        //============== EVENT HANDLERS ==============//

        public void OnItemConsumed(ItemStack consumedStack, JsonObject effectData, float potencyScale = 1.0f)
        {
            // Convert effectData.cluster to uppercase for consistency and fill in defaults/parse data
            EffectStruct effect = new EffectStruct(effectData["cluster"].AsString().ToUpper().ToEnum<EffectCluster>());

            if (effectData.KeyExists("tier")) { effect.isConcentrated = effectData["isConcentrated"].AsBool(); }
            if (effectData.KeyExists("isPoison")) { effect.isPoison = effectData["isPoison"].AsBool(); }
            if (effectData.KeyExists("effectMultiplier"))
            {
                effect.effectMultiplier = effectData["effectMultiplier"].AsFloat() * potencyScale;
            }
            if (effectData.KeyExists("onsetMultiplier"))
            {
                effect.onsetMultiplier = effectData["onsetMultiplier"].AsFloat();
            }
            if (effectData.KeyExists("toxicEffectMultiplier"))
            {
                effect.toxicEffectMultiplier = effectData["toxicEffectMultiplier"].AsFloat() * potencyScale;
            }
            if (effectData.KeyExists("toxicOnsetMultiplier"))
            {
                effect.toxicOnsetMultiplier = effectData["toxicOnsetMultiplier"].AsFloat();
            }

            ApplyEffect(effect);

        }

        private Dictionary<string, bool> effectsApplied = new Dictionary<string, bool>(); //used to prevent double application of the same effect>

        void parseEffectsAndApply()
        {
            //read effects from treeArrayAttribute and apply them. Write to effectsApplied to prevent double application.
            /*
             * rrpoisons
             * rrillness
             * rrpotions
             */

            var liveKeys = new HashSet<string>();
            //use the effect name + UID as actual key.
            //pull rrpoisons
            TreeAttribute[] rrpoisons = RRPoisonEffects.value;
            foreach (var poison in rrpoisons)
            {
                string effectname = poison.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyPoisonEffect(guid);
                }
            }
            //repeat for potions and illnesses
            TreeAttribute[] rrillnesses = RRIllnessEffects.value;
            foreach (var illness in rrillnesses)
            {
                string effectname = illness.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyIllnessEffect(guid);
                }
            }

            TreeAttribute[] rrpotions = RRPotionEffects.value;
            foreach (var potion in rrpotions)
            {
                string effectname = potion.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyPotionEffect(guid);
                }
            }

            foreach (var staleKey in effectsApplied.Keys.Except(liveKeys).ToList())
            {
                effectsApplied.Remove(staleKey);
            }
        }

        //============== ACTUAL CODE ==============//

        public void ApplyEffect(EffectStruct effect) => ApplyEffect(effect, forceIneligibleForTolerance: false);

        // only called when an item is eaten or an arrow lands so it can never apply an illness.
        public void ApplyEffect(EffectStruct effect, bool forceIneligibleForTolerance)
        {
            Guid uid = Guid.NewGuid();
            string effectname = effect.cluster.ToString() + "|" + uid.ToString();
            bool poison = false;
            bool antidote = false;
            switch (effect.cluster)
            {
                case EffectCluster.ANALGESIC:
                    break;
                case EffectCluster.ANTIDOTE:
                    /*
                     * ANTIDOTE:
                     *     - used to indicate antidote
                     *     - drinking once induces vomiting and voids satiety
                     *     - drinking again within one IRL minute removes all poison effects, completely, and entirely.
                     *     - poison effects lost this way do not count towards building tolerance
                     */
                antidote = true;
                    break;
                case EffectCluster.ANTINAUSEA:
                    break;
                case EffectCluster.ANTISEPTIC:
                    break;
                case EffectCluster.ANTIVIRAL:
                    break;
                case EffectCluster.MINDTONIC:
                    break;
                case EffectCluster.SEDATIVE:
                    break;
                case EffectCluster.TONIC:
                    break;
                case EffectCluster.TOXICPOISON:
                    /*
                     * TOXIC POISON :
                     *     - used to indicate liverbane aka liver failure
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(toxicTolerance / 3) / 9.0f
                     *     - apply healingeffectiveness reduction for a certain amount of time
                     *     - once timer expires, add DoT effect if effect > 0.15; DoT never expires
                     *     - if effect < 0.15, do not apply DoT and remove healingeffectiveness reduction
                     *     - surviving this awards 1/27 of progression towards toxicTolerance.
                     */
                    poison = true;
                    break;
                case EffectCluster.NOXIOUSPOISON:
                    /*
                     * NOXIOUS POISON :
                     *     - used to indicate gutbane aka GI irritation
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(noxiousTolerance / 3) / 9.0f
                     *     - apply psychedelic trip effect for a certain amount of time (same as eating a psychedelic mushroom)
                     *     - trigger Vomiting loop for a certain amount of time, lessened by effect multiplier
                     *     - surviving this awards 1/27 of progression towards noxiousTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.CARDIACPOISON:
                    /*
                     * CARDIAC POISON :
                     *     - used to indicate heartbane aka heart failure
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(cardiacTolerance / 3) / 9.0f
                     *     - apply cardiac event (reduce current and max health by 5hp) for a certain amount of time determined by effect multiplier
                     *     - watch player activity and roll the dice on another cardiac event if player uses tools or sprints
                     *     - surviving this awards 1/27 of progression towards cardiacTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.NEUROTOXICPOISON:
                    /*
                     * NEUROTOXIC POISON :
                     *     - used to indicate nervebane aka nerve damage
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(neurotoxicTolerance / 3) / 9.0f
                     *     - apply nerve damage for 6h * effect multiplier
                     *     - additional nerve damage applications will apply the same effect again for max(0, effect multiplier - tolerance value) * (2 ^ n-1 duration) * 6h
                     *     - the 3rd nerve damage application will also apply a cardiac event for 24h * effect multiplier
                     *     - surviving this awards 1/27 of progression towards neurotoxicTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.MINDPOISON:
                    /*
                     * MIND POISON :
                     *     - used to indicate Brain Rot aka mind damage
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(brainrotTolerance / 3) / 9.0f
                     *     - apply temporal fog effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - apply psychedelic trip effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - apply drunken wobble effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - watch player activity for movement and roll the dice on vomiting if player doesn't hold crouch/block key while moving
                     *     - double hunger rate
                     *     - double thirst rate if HoD is installed.
                     *     - surviving this awards 1/27 of progression towards brainrotTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.TOPICALOINTMENT:
                    break;
            }
            //write to treeArrayAttribute
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", effectname);
            neweffect.SetString("cluster", effect.cluster.ToString());
            neweffect.SetBool("isConcentrated", effect.isConcentrated);
            neweffect.SetDouble("timestarted", effect.timestarted);
            neweffect.SetDouble("timeleft", effect.timeleft);
            neweffect.SetFloat("effectMultiplier", effect.effectMultiplier);
            neweffect.SetFloat("toxicEffectMultiplier", effect.toxicEffectMultiplier);
            neweffect.SetFloat("toxicOnsetMultiplier", effect.toxicOnsetMultiplier);
            if (poison)
            {
                if (!IsPostAntidoteWindowActive())
                {
                    List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                    bool toleranceEligible = !forceIneligibleForTolerance
                        && !rrpoisons.Any(existing => existing.GetString("cluster") == effect.cluster.ToString());
                    neweffect.SetBool("toleranceEligible", toleranceEligible);
                    neweffect.SetBool("isPoison", true);
                    neweffect.SetFloat("onsetMultiplier", effect.onsetMultiplier); //only used for poisons
                    rrpoisons.Add(neweffect);
                    RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
                }
                // else: poison immunity is active during the post-Antidote window - this exposure
                // never happened at all.
            }
            else if (!antidote)
            {
                if (IsPostAntidoteWindowActive() && new Random().NextDouble() < 0.5)
                {
                    // 50% chance: the potion is voided entirely and vomiting triggers, per the
                    // restricted-diet window's potion-risk rule - the potion is never added to
                    // RRPotionEffects at all, so its effect never applies.
                    TriggerAntidoteWindowVomit();
                }
                else
                {
                    List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
                    neweffect.SetBool("isPoison", false);
                    rrpotions.Add(neweffect);
                    RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
                }
            }
            if (antidote)
            {
                if (!drankAntidote || timeAntidoteConsumed.AddSeconds(60) <= DateTime.Now)
                {
                    // First dose of a fresh sequence - either genuinely the first dose, or a stale
                    // sequence whose 60-second window already lapsed without a valid second dose
                    // landing. Either way this dose starts over; it never continues a dead sequence.
                    timeAntidoteConsumed = DateTime.Now;
                    drankAntidote = true;
                    VoidStomachContents(new Random().NextDouble());
                }
                else
                {
                    // Second dose, landing within the window - the cure actually takes effect.
                    List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                    rrpoisons.Clear();
                    RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
                    threadManager.HandleForcefulEnd();
                    drankAntidote = false;
                    timeAntidoteConsumed = DateTime.MinValue;
                    ApplyAntidoteAftermathEffect();
                }
            }
        }

        public void OnAnyItemConsumed(ItemStack stack, IWorldAccessor world)
        {
            if (drankAntidote && !IsAntidoteItem(stack))
            {
                // Consuming literally anything else between the Antidote's two doses resets the
                // sequence - only the Antidote's own two doses ever advance it.
                drankAntidote = false;
                timeAntidoteConsumed = DateTime.MinValue;
            }

            if (IsPostAntidoteWindowActive() && !AntidoteWindowFoodRules.IsSafeRawItem(stack, world))
            {
                TriggerAntidoteWindowVomit();
            }
        }

        public void TriggerAntidoteWindowVomit()
        {
            VoidStomachContents(1.0);
        }

        private static bool IsAntidoteItem(ItemStack stack)
        {
            string cluster = stack?.Collectible?.Attributes?["remedyandruinEffect"]?["cluster"]?.AsString();
            return string.Equals(cluster, "ANTIDOTE", StringComparison.OrdinalIgnoreCase);
        }

        public void OnMealConsumed(IWorldAccessor world, ItemStack containerStack, ItemStack[] contentStacks, BlockMeal block)
        {
            if (drankAntidote)
            {
                drankAntidote = false;
                timeAntidoteConsumed = DateTime.MinValue;
            }

            if (IsPostAntidoteWindowActive() && !AntidoteWindowFoodRules.IsSafeMeal(world, containerStack, contentStacks, block))
            {
                TriggerAntidoteWindowVomit();
            }
        }

        private void ApplyAntidoteAftermathEffect()
        {
            Guid uid = Guid.NewGuid();
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", "ANTIDOTEAFTERMATH|" + uid.ToString());
            neweffect.SetString("cluster", "ANTIDOTEAFTERMATH");
            neweffect.SetBool("isConcentrated", false);
            neweffect.SetBool("isPoison", false);
            neweffect.SetDouble("timestarted", 0.0);
            neweffect.SetDouble("timeleft", 2.0); // 2 in-game hours - the single shared window duration
            neweffect.SetFloat("effectMultiplier", 1.0f);
            neweffect.SetFloat("toxicEffectMultiplier", 0f);
            neweffect.SetFloat("toxicOnsetMultiplier", 0f);

            List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
            rrpotions.Add(neweffect);
            RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
        }

        public bool IsPostAntidoteWindowActive()
        {
            return RRPotionEffects.value.Any(e => e.GetString("cluster") == "ANTIDOTEAFTERMATH");
        }

        private void VoidStomachContents(double chance)
        {
            float amountToDrain = 0.0f;
            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if(hunger != null)
            {
                amountToDrain = hunger.Saturation;
                var stomach = entity.GetBehavior("expandedstomach");
                if (stomach != null)
                {
                    float? currentStomachAmount = stomach.GetType().GetProperty("ExpandedStomachMeter")?.GetValue(stomach) as float?;
                    if (currentStomachAmount.HasValue)
                    {
                        stomach.GetType().GetProperty("ExpandedStomachMeter")?.SetValue(stomach, 0f); //drain stomach first so it doesn't intercept the next call
                    }
                }
                hunger.Saturation -= amountToDrain; //ExpandedStomach will intercept this if it's not empty...
            }
            //TODO: wire in chance for additional void events if not triggered by antidote
        }

        private const double MaxVomitRollIntervalSeconds = 600.0;

        /// <summary>
        /// Registers a repeating, randomized vomit roll: once each interval elapses it always
        /// vomits (VoidStomachContents(1.0), not a probability check - the interval itself is the
        /// randomization). Each cycle's interval is baseIntervalSeconds jittered by +/-1/3 (pass
        /// the midpoint of the desired spread, e.g. 45 for a 30-60s range at full strength),
        /// divided by effectiveMultiplier (floored at minMultiplierFloor so a heavily
        /// tolerance-discounted dose still eventually rolls) and capped at
        /// MaxVomitRollIntervalSeconds. Returns the game tick listener id so the caller can
        /// unregister it once its own effect ends - this method has no opinion on that lifetime.
        /// Not for Mind Poison's move-triggered vomiting, which is event-driven rather than
        /// interval-driven and needs its own mechanism.
        /// </summary>
        public long StartRepeatingVomitRoll(double baseIntervalSeconds, float effectiveMultiplier, float minMultiplierFloor = 0.05f)
        {
            double elapsedSeconds = 0.0;
            double targetSeconds = NextVomitRollIntervalSeconds(baseIntervalSeconds, effectiveMultiplier, minMultiplierFloor);

            return entity.World.RegisterGameTickListener(dt =>
            {
                elapsedSeconds += dt;
                if (elapsedSeconds < targetSeconds) return;

                elapsedSeconds = 0.0;
                targetSeconds = NextVomitRollIntervalSeconds(baseIntervalSeconds, effectiveMultiplier, minMultiplierFloor);
                VoidStomachContents(1.0);
            }, 1000);
        }

        private static double NextVomitRollIntervalSeconds(double baseIntervalSeconds, float effectiveMultiplier, float minMultiplierFloor)
        {
            double jitteredSeconds = baseIntervalSeconds * (2.0 / 3.0 + new Random().NextDouble() * (2.0 / 3.0));
            double scaledSeconds = jitteredSeconds / Math.Max(effectiveMultiplier, minMultiplierFloor);
            return Math.Min(scaledSeconds, MaxVomitRollIntervalSeconds);
        }
    }

    public static class Helpers
    {
        public static T ToEnum<T>(this string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }
    }
}
