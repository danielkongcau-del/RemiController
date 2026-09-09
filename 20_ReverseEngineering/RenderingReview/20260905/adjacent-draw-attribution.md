# Battle adjacent draw attribution

Result: **PASS**. Draws 368-370 and 372 are the co-rendered training enemy, not missing Remielle meshes or materials. No unattributed draw remains in the inspected 368-373 range.

The decisive evidence is the native `VSSetConstantBuffers1` subrange. Draw 371 reads constants 0-31 from buffer `358d62cb`; draw 372 reads constants 32-63 from that same physical resource. Applying draw 371's zero offset to draw 372 produced the earlier false head-top projection. The correct offset projects draw 372 to the exact 52 pixels changed in the next G-buffer snapshot.

## Draw evidence

| Draw | Attribution | CB hash:first | Translation | Projected bbox | o1 changed bbox / pixels | Source topology |
|---:|---|---|---|---|---|---|
| 368 | foreign-training-enemy | `4785ce09:0` | -2.4902, 1.0383, -2.1059 | 1155.6, 670.9, 1231.8, 747.7 | [1156, 673, 1231, 747] / 970 | none |
| 369 | foreign-training-enemy | `4785ce09:32` | -2.6765, 1.6939, -2.1266 | 1143.2, 682.0, 1243.5, 756.8 | [1143, 697, 1242, 756] / 1932 | none |
| 370 | foreign-training-enemy | `4785ce09:64` | -2.6740, 1.6728, -2.1265 | 1094.6, 677.0, 1297.2, 893.8 | [1095, 679, 1296, 893] / 5879 | none |
| 371 | remielle-source-joined-anchor | `358d62cb:0` | -13.4423, 1.7645, -2.4172 | 1231.7, 689.8, 1312.6, 801.4 | [1232, 712, 1311, 800] / 1092 | Remielle_Hair |
| 372 | foreign-training-enemy | `358d62cb:32` | -2.7488, 2.2239, -2.1233 | 1186.2, 804.1, 1204.5, 811.0 | [1186, 804, 1204, 810] / 52 | none |
| 373 | remielle-source-joined-anchor | `0c8934aa:0` | -13.4328, 1.6794, -2.4224 | 1142.6, 327.5, 1393.2, 635.7 | [1144, 328, 1392, 634] / 9875 | Remielle_Wings |

## Corrected draw 372 conclusion

- Correct native range: first constant **32**, byte offset **512**.
- Correct projection: `[1186.162, 804.089, 1204.515, 810.994]`.
- Observed changed pixels: `[1186, 804, 1204, 810]`, **52 pixels**.
- Rejected zero-offset projection: `[1249.248, 635.642, 1323.636, 663.704]`; it does not contain the changed pixels.
- Its 148-vertex disc-like geometry and green emissive material belong to the distant training target visible behind Remielle. Their absence from the Remielle recovery corpus is expected.

## Consequence

These four calls must be excluded from Remielle material/mesh completeness counts. The shared character shader variant on draw 372 only proves that both actors use the same program family. Draw adjacency likewise does not prove ownership. The source-joined Remielle sequence resumes at Hair draw 371 and Wings draw 373.

Overlay: `E:\ZZZ\local-only\RemielleRenderingReview\20260905\adjacent-draw-attribution\battle-adjacent-draw-attribution.png`.
