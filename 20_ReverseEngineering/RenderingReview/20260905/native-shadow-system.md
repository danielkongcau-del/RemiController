# Native shadow system evidence

The captured battle shaders use two independent comparison-sampled shadow sources.

- Mapped Remielle draws: 19
- Exact pixel-shader variants: 6
- Global main-light shadow: `t0`, 2048x2048, four-layer R16 resource, 11 comparison samples.
- Per-object character shadow: variant-specific slot, 2048x2048 R16 resource, 9 comparison samples.
- All mapped draws share cb0 `5fb77447`, cb2 `11fdc8a4`, cb3 `161a5c12` in this frame.

| Shader variant | Per-object slot | Global taps | Per-object taps |
|---|---:|---:|---:|
| `miHoYo_Character_NapAvatarStandardFace_00__ps_013` | t5 | 11 | 9 |
| `miHoYo_Character_NapAvatarStandard_00__ps_021` | t7 | 11 | 9 |
| `miHoYo_Character_NapAvatarStandard_00__ps_024` | t7 | 11 | 9 |
| `miHoYo_Character_NapAvatarStandard_00__ps_026` | t8 | 11 | 9 |
| `miHoYo_Character_NapAvatarStandard_00__ps_331` | t13 | 11 | 9 |
| `miHoYo_Character_NapAvatarStandard_00__ps_336` | t12 | 11 | 9 |

## Captured material state

All mapped draws have `_ReceiveShadows=1`, `_ShadowColorFadeByZ=1`, `_ShadowNormalBias=0.01`, and every available per-material shadow intensity slot is 1.

## Live implementation boundary

The DDS payloads and matrices can reproduce the captured frame only. They are tied to that frame's object pose, camera and light. Binding them as ordinary live material textures would freeze the projected shadows when animation or the camera moves.

The review scene now implements the live structural path: an animated per-object depth pass, four camera-fitted main-light cascade passes, current matrices/split spheres, and the recovered 9/11-tap comparison kernels. The live filter preserves the native 0.111100003 / 0.0908999965 accumulation constants, direct comparison semantics and the cascade depth-range guard. Player readback verifies non-empty R16 maps and pose-dependent receiver attenuation. The captured camera-distance shadow-color normalization is also implemented. The remaining boundary is exact native cascade fitting, captured level casters and the complete material composition; captured DDS files remain evidence rather than live textures.
