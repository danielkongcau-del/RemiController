# Native G-buffer instances and late passes

Result: **PASS**.

## 1024x576 reflection instance

The low-resolution four-target sequence is the native mirror-reflection producer. Its final `4bb2fe34` image is processed by UberPost, passed byte-for-byte into Gaussian Blur 8/9, then bound byte-for-byte at draw 530 t6. The serialized draw-530 variant enables `USE_MIRROR_REFLECTION`; draw 530 writes `d72d6162`, which draw 531 consumes byte-for-byte.

- Confirmed Remielle base draws: 12; union 11,470 pixels.
- Confirmed Remielle outline draws: 12; union 855 pixels.
- Late hair 163-165: union 0 pixels (fully culled in this reflection).
- Transparent/sticker 229-231: union 124 pixels.
- The reflection omits or culls Weapon_05, face/eye/eyebrow and two cannon draws. This is a reflection roster choice, not a source-asset gap.

## Main-camera producer after the base materials

The 19 confirmed Remielle material draws retain their 104,502-pixel union. The following 19 confirmed Remielle outline draws add a 10,017-pixel union before deferred resolve.

| Call | Role | Changed | Per target | Stencil ref |
|---:|---|---:|---|---:|
| 413 | generated hair-outline geometry writes stencil 132; color output is byte-identical at the affected pixels | 2,902 | o0:0, o1:0, o2:0, o3:0, oD:2902 | 132 |
| 414 | face base redraw gated by stencil 132; zero pixels change in this frame | 0 | o0:0, o1:0, o2:0, o3:0, oD:0 | 132 |
| 415 | eye redraw gated by stencil 148; restores 32 primary-color pixels | 32 | o0:32, o1:0, o2:0, o3:0, oD:0 | 148 |
| 416 | eyebrow redraw gated by stencil 148; restores 21 primary-color pixels | 21 | o0:21, o1:0, o2:0, o3:0, oD:0 | 148 |
| 488 | NapAvatarStandardEye overlay; changes 45 primary-color pixels | 45 | o0:45, o1:0, o2:0, o3:0, oD:0 | 0 |
| 489 | hair alpha/depth prepass; changes stencil/depth on 53 pixels | 53 | o0:0, o1:0, o2:0, o3:0, oD:53 | 16 |
| 490 | high-quality hair coverage with a distinct blend/depth state; changes 53 fringe pixels | 53 | o0:53, o1:1, o2:0, o3:53, oD:53 | 144 |
| 491 | hair outline follow-up; changes 75 fringe pixels | 75 | o0:75, o1:0, o2:66, o3:25, oD:75 | 144 |

Draw 413 uses generated outline geometry and writes stencil 132 over the hair region. Draws 414-416 then repeat the face-family shader under stencil 132/148; only eye and eyebrow primary color changes survive. Draws 489-491 form a depth-mask, high-quality hair-coverage and outline sequence for the remaining fringe pixels. The hair material draw at 490 deliberately uses a different blend and depth/stencil state from base draw 387.

## Feature byte

Every confirmed Remielle base-material write in this frame has feature byte 0. The same capture nevertheless exercises byte 4 (bit 2): draws 64-66/68 and 368-370/372 belong to the separately proven training enemy and bind `ps-cb3[40].x = 1`. This validates the nonzero global-feature path without assigning those pixels to Remielle.

## Boundary

This report establishes instance ownership and ordering. It does not make the 1024x576 reflection a substitute for the main camera, and it does not treat outline or fringe passes as extra source meshes/material slots. The live Unity renderer must reproduce these stages explicitly if strict native parity is required.
