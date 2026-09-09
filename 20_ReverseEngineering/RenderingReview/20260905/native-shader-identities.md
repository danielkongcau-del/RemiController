# 原生轮廓、眼部和头发 Shader 身份审计

- 结果：**PASS**
- 已将 5 个运行时哈希精确关联到 Unity 序列化 Shader、Pass、变体记录以及 block+CAB+pathID。
- 另有 2 个 draw 413 运行时哈希只有 D3D11 反汇编，当前 17 个已提取序列化 Shader 中无同哈希记录。

| 哈希 | 捕获作用 | 序列化 Shader / Pass | 变体 | 来源 |
|---|---|---|---|---|
| `6c0519a487c74676` | standard outline | `miHoYo/Character/NapAvatarStandard` / `CharacterOutlineDeferred` | record 48 [] | `170712882.blk` + `CAB-8f1ade485e6dbb268cac1df590e456c6` + `3888839570379262989` |
| `8e41e455f2e919f7` | face outline | `miHoYo/Character/NapAvatarStandardFace` / `FaceOutlineDeferred` | record 44 []; record 59 ['_TOON_LIGHTS'] | `2685514787.blk` + `CAB-6f0adea15b5b68788c4dcf3500233f2c` + `8174284373312262851` |
| `02549f5219c55aa4` | hair/depth-only prepass | `miHoYo/Character/NapAvatarStandard` / `CharDepthOnly` | record 4024 []; record 4025 ['_TOON_LIGHTS'] | `170712882.blk` + `CAB-8f1ade485e6dbb268cac1df590e456c6` + `3888839570379262989` |
| `d528b74afe9888e4` | opaque eye correction | `miHoYo/Character/NapAvatarStandardEye` / `CharacterOpaqueEye` | record 240 ['_NAP_SHADER_QUALITY_HIGH']; record 303 ['_NAP_SHADER_QUALITY_LOW']; record 349 ['_NAP_SHADER_QUALITY_MIDDLE'] | `2685514787.blk` + `CAB-cecdf75022a9f6081f5a0815bdbe8154` + `7755011506878469506` |
| `0c8ddaae78cc096f` | high-quality hair coverage | `miHoYo/Character/NapAvatarStandard` / `CharacterToonDeferred` | record 2703 ['_NAP_SHADER_QUALITY_HIGH', '_RENDERTYPE_HAIR'] | `170712882.blk` + `CAB-8f1ade485e6dbb268cac1df590e456c6` + `3888839570379262989` |

## 尚缺序列化身份

- `bcddceab76ee10ce` (vs)：generated hair-outline geometry。已保存反汇编；在 17 个当前提取的 Shader 资产中无匹配，不能据此虚构 block/CAB/pathID。
- `c07b54c6554b2591` (ps)：generated hair-outline grayscale mask。已保存反汇编；在 17 个当前提取的 Shader 资产中无匹配，不能据此虚构 block/CAB/pathID。

这项边界只影响 draw 413 的来源归档。它的运行时行为已经由输入签名、常量、反汇编和逐目标差分确认，不会推翻主材质、轮廓、眼部回填或头发覆盖的现有结论。
