# Remielle native material cbuffer audit

This report aligns the battle capture's native `ps-cb4` bytes with the exact native shader layout, extracted source Material JSON, and the current review shader.

## Result

- Mapped Remielle draws: **19**; unresolved draws: **0**.
- Adjacent foreign training-enemy draws excluded from Remielle completeness: **4** (368, 369, 370, 372).
- Per-draw reflected observations: **1845**; unique shader/name/offset signatures: **519**.
- Current shader coverage: **385 consumed**, **6 declared-only**, **128 missing**.
- Nonzero unconsumed candidates: **24**. These are investigation targets, not automatic visual defects.

## Draw mapping

| Draw | Material | Diffuse | PS | CB bytes | Mapping evidence |
|---:|---|---|---|---:|---|
| 365 | `MAT_Remielle_Weapon_02` | `85b91845` | `9dd6a2a0a6d11117` | 2752 | diffuse-hash unique; first mesh |
| 366 | `MAT_Remielle_Weapon_02` | `85b91845` | `9dd6a2a0a6d11117` | 2752 | diffuse-hash unique; second mesh |
| 367 | `MAT_Remielle_Origin_Body_1` | `e51be5d1` | `9dd6a2a0a6d11117` | 2752 | diffuse-hash narrows to Body_1/Body_1_T; opaque order |
| 371 | `MAT_Remielle_Hair` | `578239d7` | `80c34aae4f69f1ff` | 2752 | diffuse-hash Hair and opaque shader variant |
| 373 | `MAT_Remielle_Wings` | `80ad86c3` | `8fc7e589644cb3c4` | 2752 | diffuse-hash unique and transmission shader variant |
| 374 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 375 | `MAT_Remielle_Origin_Body_2` | `6538d30d` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 376 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 377 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 378 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 379 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 380 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 381 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 382 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 383 | `MAT_Remielle_Weapon_01` | `420e2418` | `77bdd348772c62c8` | 2752 | diffuse-hash unique |
| 384 | `MAT_Remielle_Face` | `baf9e1be` | `dff613cbed805284` | 2144 | shared face diffuse; stable Face/Eye/Eyebrow order |
| 385 | `MAT_Remielle_Eye` | `baf9e1be` | `dff613cbed805284` | 2144 | shared face diffuse; stable Face/Eye/Eyebrow order |
| 386 | `MAT_Remielle_Eyebrow` | `baf9e1be` | `dff613cbed805284` | 2144 | shared face diffuse; stable Face/Eye/Eyebrow order |
| 387 | `MAT_Remielle_Hair` | `578239d7` | `0c8ddaae78cc096f` | 2752 | Hair_T draw; authored values derived from Hair JSON |

## Nonzero unconsumed candidates

| Property | Current status | Materials | Draws | Source relation |
|---|---|---|---|---|
| `_MatCapBumpScaleFx` | declared-only | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_MatCapUSpeedFx` | declared-only | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | runtime-or-packed-difference |
| `_SpecialWeaponMergeParam03` | declared-only | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_ColorOverrideAlbedo` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_DecolorizationContrast` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_DetailColor` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_FresnelColor` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_FresnelWidth` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_MatCapColorTintFx` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | runtime-or-packed-difference |
| `_MatCapTexID_MatCapColorBurst_MatCapAlphaBurst_MatCapUSpeed` | missing | MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02 | 365, 366, 367, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | absent |
| `_MatCapVSpeed_MatCapBlendMode_MatCapRefract_RefractDepth` | missing | MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02 | 365, 366, 367, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | absent |
| `_OverlayTexScale` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_OverrideColor` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_OverrideRimGlowColor` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_OverrideRimGlowTexFX_ST` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | match |
| `_PerObjectShadowIntensity` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_PerObjectShadowIntensity2` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | absent, match |
| `_PerObjectShadowIntensity3` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | absent, match |
| `_PerObjectShadowIntensity4` | missing | MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 387 | match |
| `_PerObjectShadowIntensity5` | missing | MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 387 | match |
| `_ReceiveShadows` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_RefractParamArray` | missing | MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02 | 365, 366, 367, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | absent |
| `_ShadowNormalBias` | missing | MAT_Remielle_Eye, MAT_Remielle_Eyebrow, MAT_Remielle_Face, MAT_Remielle_Hair, MAT_Remielle_Origin_Body_1, MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Weapon_02, MAT_Remielle_Wings | 365, 366, 367, 371, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387 | match |
| `_VertexOffset` | missing | MAT_Remielle_Origin_Body_2, MAT_Remielle_Weapon_01, MAT_Remielle_Wings | 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383 | runtime-or-packed-difference |

## Excluded adjacent foreign draws

- Draw 368: diffuse `481c5d67`, CB range `4785ce09:0` — projected pixels and world transform are on the co-rendered training enemy; no exact ordered Remielle source topology candidate.
- Draw 369: diffuse `481c5d67`, CB range `4785ce09:32` — projected pixels and world transform are on the co-rendered training enemy; no exact ordered Remielle source topology candidate.
- Draw 370: diffuse `481c5d67`, CB range `4785ce09:64` — projected pixels and world transform are on the co-rendered training enemy; no exact ordered Remielle source topology candidate.
- Draw 372: diffuse `0fe52874`, CB range `358d62cb:32` — projected pixels and world transform are on the co-rendered training enemy; no exact ordered Remielle source topology candidate.

## Evidence boundary

A remaining source mismatch can be a runtime MaterialPropertyBlock override or another packed representation. Integer fields are decoded from native reflection and serialized colors are tested both directly and after Unity-standard sRGB-to-linear conversion. A reflected nonzero value can still be inactive for the actual pixels because shader reflection proves variant membership, not branch execution. The JSON report preserves raw bytes, both numeric interpretations, every byte-derived value, and hashes for follow-up.
