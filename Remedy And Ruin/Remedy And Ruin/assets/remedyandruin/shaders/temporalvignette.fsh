#version 330 core

in vec2 uv;

out vec4 outColor;

uniform sampler2D primaryFb;
uniform vec2 invFrameSize;
uniform float strength;
uniform float glitchProgress;
uniform float timeCounter;
uniform int mode; // 0 = Temporal Fog (sepia/desaturation), 1 = Skull-Strain concussion (horizontal shear + contrast/brightness)

// Temporal Fog / Skull-Strain headache screen effect (design doc: Part 3, Confusion/Brain
// Fog). Reworked per playtester feedback: the original vignette read as "cold" (icy, not
// disorienting) and wasn't even noticeable, the vein tendrils were overkill, and the old
// high-frequency camera shudder (see ApplyWobble in TemporalVignetteRenderer.cs) was
// annoying rather than disorienting. Replaced with: Temporal Fog gets a flat muted
// sepia/desaturation color grade, nothing else. Concussion keeps its periodic horizontal
// shear (unchanged, feedback didn't ask to remove it) and gains a contrast/brightness
// boost; its camera motion moved from a jittery shudder to a slow drunken sway, handled
// entirely in C# (ApplyWobble), not here.

vec3 sampleBlurred(vec2 centerUv, vec2 texelStep, float amount)
{
	vec3 sum = texture(primaryFb, centerUv).rgb * 4.0;
	sum += texture(primaryFb, centerUv + vec2(texelStep.x, 0.0) * amount).rgb;
	sum += texture(primaryFb, centerUv - vec2(texelStep.x, 0.0) * amount).rgb;
	sum += texture(primaryFb, centerUv + vec2(0.0, texelStep.y) * amount).rgb;
	sum += texture(primaryFb, centerUv - vec2(0.0, texelStep.y) * amount).rgb;
	return sum / 8.0;
}

float hash2(vec2 p)
{
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void main()
{
	vec2 screenUv = gl_FragCoord.xy * invFrameSize;

	if (strength <= 0.0001)
	{
		outColor = texture(primaryFb, screenUv);
		return;
	}

	// Skull-Strain concussion: a subtle horizontal shear of the scene itself, applied
	// before sampling so it genuinely displaces what's on screen rather than just tinting
	// over it. Wide bands (10 across the screen) smoothly blended into each other, not a
	// hard per-pixel tear, and a small max offset - a woozy wobble, not a glitch/tear.
	// Unchanged from before - playtester feedback didn't ask to remove this one.
	if (mode == 1 && glitchProgress > 0.0001)
	{
		float envelope = sin(clamp(glitchProgress, 0.0, 1.0) * 3.14159265);
		float bandPos = screenUv.y * 10.0;
		float band = floor(bandPos);
		float bandFrac = smoothstep(0.0, 1.0, fract(bandPos));
		float seed = floor(timeCounter * 0.8);
		float shearA = hash2(vec2(band, seed)) - 0.5;
		float shearB = hash2(vec2(band + 1.0, seed)) - 0.5;
		float shear = mix(shearA, shearB, bandFrac);
		screenUv.x += shear * 0.012 * envelope;
	}

	// Chromatic aberration: sample each channel offset along the vector from screen center,
	// growing with both distance from center and overall strength. Kept for both modes -
	// feedback only asked to remove the vignette/tendrils/shudder, not this.
	vec2 fromCenter = screenUv - vec2(0.5);
	float aberrationAmount = strength * 0.01;
	vec2 aberrationDir = fromCenter * aberrationAmount;
	float blurAmount = strength * 1.5;

	float r = sampleBlurred(screenUv + aberrationDir, invFrameSize, blurAmount).r;
	float g = sampleBlurred(screenUv, invFrameSize, blurAmount).g;
	float b = sampleBlurred(screenUv - aberrationDir, invFrameSize, blurAmount).b;
	vec3 col = vec3(r, g, b);

	if (mode == 0)
	{
		// Temporal Fog: flat muted sepia/desaturation, no vignette, no tendrils, no throb -
		// just a consistently readable color grade so it's actually noticeable while active.
		float gray = dot(col, vec3(0.299, 0.587, 0.114));
		vec3 sepia = vec3(gray * 1.07, gray * 0.74, gray * 0.43);
		col = mix(col, sepia, clamp(strength, 0.0, 1.0) * 0.8);
	}
	else
	{
		// Skull-Strain concussion: contrast + brightness boost, up to +50% at full strength -
		// "dazed", not "cold". Pivoted at mid-gray so it reads as contrast, not just a wash.
		float boost = mix(1.0, 1.50, clamp(strength, 0.0, 1.0));
		col = (col - 0.5) * boost + 0.5;
		col *= boost;
		col = clamp(col, 0.0, 1.0);
	}

	outColor = vec4(col, 1.0);
}
