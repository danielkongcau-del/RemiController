# Remielle 原生 G-buffer 字段审计

- 结果：**PASS**
- 六个材质 PS、19 个映射 draw；四目标与深度/模板联合确认 104,502 个 Remielle 像素，全部进入 draw 523。
- o1 单目标保守子集：38,542；o0/o2/o3/oD 各 104,502。
- 编码法线最大单位长度误差：0.0016851。

## 字段合同

| 目标 | 格式 | 恢复结果 |
|---|---|---|
| o0 | RGBA16F | 主材质/光照颜色；五个变体 alpha=1，头发覆盖变体 alpha 随视角与纹理变化。 |
| o1 | RGBA8 sRGB | RGB 为 `0.2*sqrt(x)` 编码的 RimGlow，draw 523 以 `(5*x)^2` 解码；A 为发光/附加亮度累计的三分之一。 |
| o2.xy | R10G10 | 中心约 0.498 的带符号平方根运动编码。 |
| o2.z | R10 | 除以 255 的标志字节；本帧所有 Remielle 像素为 0。 |
| o2.w | A2 | 材质 ID/皮肤类匹配写 0.34，落盘量化为 1/3；其余为 0。 |
| o3 | RGB10A2 | 世界法线 `n*0.5+0.5`，A=1。 |
| oD | D32S8 | 直接写入 128；眼/眉分类写 144。后续阶段再叠加位得到 132/148。 |

## 逐材质写入

| Draw | 材质 | PS | 像素 | 模板 | o2.w | o1.a 范围 | o0.a 范围 |
|---:|---|---|---:|---|---|---|---|
| 365 | MAT_Remielle_Weapon_02 | `9dd6a2a0a6d11117` | 432 | {'128': 432} | {'0': 432} | [0, 0] | [1.0, 1.0] |
| 366 | MAT_Remielle_Weapon_02 | `9dd6a2a0a6d11117` | 495 | {'128': 495} | {'0': 495} | [0, 0] | [1.0, 1.0] |
| 367 | MAT_Remielle_Origin_Body_1 | `9dd6a2a0a6d11117` | 21,249 | {'128': 21249} | {'0': 16317, '1': 4932} | [0, 0] | [1.0, 1.0] |
| 371 | MAT_Remielle_Hair | `80c34aae4f69f1ff` | 3,963 | {'128': 3963} | {'0': 3963} | [0, 0] | [1.0, 1.0] |
| 373 | MAT_Remielle_Wings | `8fc7e589644cb3c4` | 17,737 | {'128': 17737} | {'0': 17737} | [0, 58] | [1.0, 1.0] |
| 374 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 50,648 | {'128': 50648} | {'0': 50648} | [0, 208] | [1.0, 1.0] |
| 375 | MAT_Remielle_Origin_Body_2 | `77bdd348772c62c8` | 20,189 | {'128': 20189} | {'0': 18206, '1': 1983} | [0, 0] | [1.0, 1.0] |
| 376 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 47 | {'128': 47} | {'0': 47} | [0, 57] | [1.0, 1.0] |
| 377 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 101 | {'128': 101} | {'0': 101} | [0, 56] | [1.0, 1.0] |
| 378 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 251 | {'128': 251} | {'0': 251} | [0, 63] | [1.0, 1.0] |
| 379 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 336 | {'128': 336} | {'0': 336} | [0, 77] | [1.0, 1.0] |
| 380 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 33 | {'128': 33} | {'0': 33} | [0, 57] | [1.0, 1.0] |
| 381 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 78 | {'128': 78} | {'0': 78} | [0, 57] | [1.0, 1.0] |
| 382 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 50 | {'128': 50} | {'0': 50} | [0, 82] | [1.0, 1.0] |
| 383 | MAT_Remielle_Weapon_01 | `77bdd348772c62c8` | 336 | {'128': 336} | {'0': 336} | [0, 94] | [1.0, 1.0] |
| 384 | MAT_Remielle_Face | `dff613cbed805284` | 1,812 | {'128': 1812} | {'1': 1812} | [0, 0] | [1.0, 1.0] |
| 385 | MAT_Remielle_Eye | `dff613cbed805284` | 143 | {'144': 143} | {'0': 143} | [0, 0] | [1.0, 1.0] |
| 386 | MAT_Remielle_Eyebrow | `dff613cbed805284` | 32 | {'144': 32} | {'0': 32} | [0, 0] | [1.0, 1.0] |
| 387 | MAT_Remielle_Hair | `0c8ddaae78cc096f` | 2,828 | {'128': 2828} | {'0': 2828} | [0, 0] | [0.496582, 1.0] |

## 已证实的边界

本帧运动开关为 [1.0]，o2.xy 在两个 10 位通道上的实际范围为 [[482, 531], [480, 534]]，因此运动编码已经有动态数值证据。Remielle 的 o2.z 标志字节全部为 0；跨抓帧审计已在训练敌人上实证 Ignis bit 1 与全局 bit 2，Ghost、VFX 和 bit 4 尚无激活帧。实时 Unity 生产器已按字段语义写入 RimGlow RGB 与发光累加 alpha；G-buffer Pass 已从 o0 抑制 Forward RimGlow，防止 draw 523 重复相加。o0 的其余主体仍是 HoyoToon Forward 适配，不能称作完整原生材质光照。

## 权威产物

- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-gbuffer-payloads.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-material-cbuffer-audit.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-gbuffer-deferred.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\captured-deferred-character.json`
