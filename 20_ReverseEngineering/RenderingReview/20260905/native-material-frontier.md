# Native material frontier

This report reduces the 31 nonzero, previously unconsumed material-CB fields to their actual captured pixel-shader branches.

- Parameters: 31
- Exact PS variants: 6
- Classification is specific to the captured battle frame.

| Classification | Count |
|---|---:|
| `branch-disabled` | 12 |
| `implemented-adaptation` | 5 |
| `implemented-neutral-equivalent` | 4 |
| `neutral-equivalent` | 7 |
| `not-read-in-captured-variant` | 3 |

| Parameter | Classification | Captured values | Evidence |
|---|---|---|---|
| `_AlbedoSmoothness2` | `implemented-neutral-equivalent` | `[[0.05000000074505806]]` | read; Native material-ID selection is now implemented; all captured Remielle smoothness slots are 0.05. |
| `_AlbedoSmoothness3` | `implemented-neutral-equivalent` | `[[0.05000000074505806]]` | read; Native material-ID selection is now implemented; all captured Remielle smoothness slots are 0.05. |
| `_AlbedoSmoothness4` | `implemented-neutral-equivalent` | `[[0.05000000074505806]]` | read; Native material-ID selection is now implemented; all captured Remielle smoothness slots are 0.05. |
| `_AlbedoSmoothness5` | `implemented-neutral-equivalent` | `[[0.05000000074505806]]` | read; Native material-ID selection is now implemented; all captured Remielle smoothness slots are 0.05. |
| `_MatCapBumpScaleFx` | `branch-disabled` | `[[1.0]]` | read; The captured _MatCapFX gate is 0 for every draw exposing this field. |
| `_MatCapUSpeedFx` | `branch-disabled` | `[[1.0]]` | read; The captured _MatCapFX gate is 0 for every draw exposing this field. |
| `_NoseLineHoriDisp` | `implemented-adaptation` | `[[0.8500000238418579],[0.9200000166893005]]` | read; StandardFace ps_013 directional threshold is now consumed; local color remains on the stable HoyoToon mask until the native G-buffer/BRDF chain is ported. |
| `_NoseLineLkDnDisp` | `implemented-adaptation` | `[[0.5],[0.6200000047683716]]` | read; StandardFace ps_013 directional threshold is now consumed; local color remains on the stable HoyoToon mask until the native G-buffer/BRDF chain is ported. |
| `_SpecialWeaponMergeParam03` | `neutral-equivalent` | `[[0.0,0.0,0.0,1.0]]` | read; Only the x lane is read on the captured signature branch and x is 0; the nonzero w lane is not read. |
| `_ColorOverrideAlbedo` | `branch-disabled` | `[[1.0,1.0,1.0,0.0]]` | read; The captured alpha/enabling lane is 0 for every applicable draw. |
| `_DecolorizationContrast` | `neutral-equivalent` | `[[1.0,1.0,0.0,0.0]]` | read; Captured value is the neutral tuple (1,1,0,0); the enabling lane is 0. |
| `_DetailColor` | `branch-disabled` | `[[0.3840000033378601,0.5649999976158142,1.4980000257492065,1.0]]` | read; The captured _Override gate is 0 for every draw. |
| `_FresnelColor` | `branch-disabled` | `[[0.0,0.20000000298023224,1.625,1.0]]` | read; The captured _Override gate is 0 for every draw. |
| `_FresnelWidth` | `branch-disabled` | `[[3.5]]` | read; The captured _Override gate is 0 for every draw. |
| `_MatCapColorTintFx` | `branch-disabled` | `[[0.0,0.0,0.0,1.0]]` | read; The captured _MatCapFX gate is 0 for every draw exposing this field. |
| `_MatCapTexID_MatCapColorBurst_MatCapAlphaBurst_MatCapUSpeed` | `not-read-in-captured-variant` | `[[7.0,0.5,0.5,0.0],[100.0,1.0,1.0,0.0],[0.0,0.6000000238418579,0.4000000059604645,0.0]]` | not read; No lane is read by any actual captured pixel-shader variant. |
| `_MatCapVSpeed_MatCapBlendMode_MatCapRefract_RefractDepth` | `not-read-in-captured-variant` | `[[0.0,1.0,0.0,0.5],[0.0,0.0,0.0,0.5],[0.0,2.0,0.0,0.5]]` | not read; No lane is read by any actual captured pixel-shader variant. |
| `_OverlayTexScale` | `branch-disabled` | `[[1000.0]]` | read; The captured _UseOverlayTex gate is 0 for every draw. |
| `_OverrideColor` | `branch-disabled` | `[[1.0,1.0,1.0,1.0]]` | read; The captured _Override gate is 0 for every draw. |
| `_OverrideRimGlowColor` | `branch-disabled` | `[[1.0,1.0,1.0,1.0]]` | read; The captured _OverrideRimGlow gate is 0 for every applicable draw. |
| `_OverrideRimGlowTexFX_ST` | `branch-disabled` | `[[1.0,1.0,0.0,0.0]]` | read; The captured _OverrideRimGlow gate is 0 for every applicable draw. |
| `_PerObjectShadowIntensity` | `neutral-equivalent` | `[[1.0]]` | read; Native material-ID shadow selection is active, but all captured intensities are 1. |
| `_PerObjectShadowIntensity2` | `neutral-equivalent` | `[[1.0]]` | read; Native material-ID shadow selection is active, but all captured intensities are 1. |
| `_PerObjectShadowIntensity3` | `neutral-equivalent` | `[[1.0]]` | read; Native material-ID shadow selection is active, but all captured intensities are 1. |
| `_PerObjectShadowIntensity4` | `neutral-equivalent` | `[[1.0]]` | read; Native material-ID shadow selection is active, but all captured intensities are 1. |
| `_PerObjectShadowIntensity5` | `neutral-equivalent` | `[[1.0]]` | read; Native material-ID shadow selection is active, but all captured intensities are 1. |
| `_ReceiveShadows` | `implemented-adaptation` | `[[1.0]]` | read; Value is enabled. The review path now renders dynamic four-cascade and per-object R16 maps and consumes them with the recovered 11/9-tap comparison kernels. |
| `_RefractParamArray` | `not-read-in-captured-variant` | `[[5.0,5.0,0.0,0.0]]` | not read; No lane is read by any actual captured pixel-shader variant. |
| `_ShadowColorFadeByZ` | `implemented-adaptation` | `[[1.0]]` | read; The derived review shader now uses the recovered camera-distance normalization formula, captured value 1 and captured _PackedParams0.w. |
| `_ShadowNormalBias` | `implemented-adaptation` | `[[0.009999999776482582]]` | read; The live native-layout shadow receiver offsets world position by the captured normal*0.01 value before both custom projections. |
| `_VertexOffset` | `branch-disabled` | `[[1.0]]` | read; This packed value is present only with the captured _MatCapFX=0 family and has no proven active pixel contribution. |

## Boundary

A neutral or disabled result only applies to this captured frame and these exact variants. It does not prove the feature can never activate in another animation, scene, costume, or effect state.
