# Live draw 523 constant audit

The exact recovered draw 523 shader reads 36 float4 registers from its 185-register buffer. The live Unity probe refreshes 12 of them from the current camera, light and LUT layout; every other read is an explicit captured combat profile or feature-state value.

| Register | Reflected name | Status | Reason |
|---:|---|---|---|
| 6 | `_MainLightPosition` | `live` | overwritten from the current Unity state |
| 7 | `_MainLightColor` | `captured-invariant` | native program reads only _MainLightColor.w; captured value is 1 |
| 23 | `_FXCC_LutToneParams[0]` | `captured-profile` | captured FXCC LUT tone profile |
| 24 | `_FXCC_LutToneParams[1]` | `captured-profile` | captured FXCC LUT tone profile |
| 25 | `_FXCC_LutToneParams[2]` | `captured-profile` | captured FXCC LUT tone profile |
| 26 | `_FXCC_LutToneParams[3]` | `captured-profile` | captured FXCC LUT tone profile |
| 30 | `_WorldSpaceCameraPos` | `live` | overwritten from the current Unity state |
| 62 | `_ZBufferParams` | `live` | overwritten from the current Unity state |
| 78 | `unity_WorldToCamera[0]` | `live` | overwritten from the current Unity state |
| 79 | `unity_WorldToCamera[1]` | `live` | overwritten from the current Unity state |
| 80 | `unity_WorldToCamera[2]` | `live` | overwritten from the current Unity state |
| 134 | `_InvViewProjMatrix[0]` | `live` | overwritten from the current Unity state |
| 135 | `_InvViewProjMatrix[1]` | `live` | overwritten from the current Unity state |
| 136 | `_InvViewProjMatrix[2]` | `live` | overwritten from the current Unity state |
| 137 | `_InvViewProjMatrix[3]` | `live` | overwritten from the current Unity state |
| 138 | `_ScreenSize` | `live` | overwritten from the current Unity state |
| 149 | `_SceneFogParamsPart1[0]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 150 | `_SceneFogParamsPart1[1]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 151 | `_SceneFogParamsPart1[2]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 152 | `_SceneFogParamsPart1[3]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 153 | `_SceneFogParamsPart2[0]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 154 | `_SceneFogParamsPart2[1]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 155 | `_SceneFogParamsPart2[2]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 156 | `_SceneFogParamsPart2[3]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 157 | `_SceneFogParamsPart3[0]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 158 | `_SceneFogParamsPart3[1]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 159 | `_SceneFogParamsPart3[2]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 160 | `_SceneFogParamsPart3[3]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 161 | `_SceneFogParamsPart4[0]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 162 | `_SceneFogParamsPart4[1]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 163 | `_SceneFogParamsPart4[2]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 164 | `_SceneFogParamsPart4[3]` | `captured-profile` | captured ENVIRO_SIMPLE_FOG profile; camera position remains live |
| 180 | `_RimGlowWidthForCharacter` | `captured-feature` | captured character rim width is 1 |
| 181 | `_MotionBlurMask` | `captured-feature` | captured motion blur mask is 1 |
| 183 | `_Lut_Params_Char` | `live` | overwritten from the current Unity state |
| 184 | `_is_apply_lut_character_on` | `captured-feature` | captured character LUT enable is 1 |

## Boundary

Camera position, depth linearization, world-to-camera rows, inverse view-projection, screen size, main-light direction and LUT dimensions are recomputed every frame. Fog and FXCC tone remain the selected captured battle profile. Rim width, motion-mask and character-LUT enable remain captured feature switches. This is intentional and machine-visible; it is not described as a fully reconstructed runtime scene manager.
