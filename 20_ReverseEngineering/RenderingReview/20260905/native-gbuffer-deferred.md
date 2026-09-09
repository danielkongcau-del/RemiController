# 原生 G-buffer 与延迟光照审计

- 抓帧闭环：**PASS**
- Remielle 主材质绘制：19 个，原生 PS 变体：6 个。
- G-buffer：o0=RGBA16F 主材质/光照颜色，o1=SRGB 编码的附加光照负载，o2=运动与特征位，o3=编码世界法线，oD=深度+模板分类。
- 角色/敌人 draw 523 GPU 回放：128,977 像素；HDR 386,931 个值逐位一致，辅助目标最大 0.007774 个原生量化单位，越界 0。
- 19 个 Remielle draw 确认改写 104,502 个 G-buffer 像素，全部位于 draw 523 处理范围内。
- 环境 draw 531 输入：12/12 个使用槽，格式逐槽匹配：True。
- 环境 draw 531 GPU 回放：3,471,327 像素，10,413,981 个 RGB 值，最大 1.0 个原生量化单位，越界 0。
- 模板分区：匹配。531=32，532=其余非零类，533=0。

## 522-533 顺序

| Draw | PS | 模板参考值 | 输出 | 作用 |
|---:|---|---:|---|---|
| 522 | `9d67aca640a93e5f` | 0 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 初始化 HDR 与辅助 R10 目标；读取深度、半分辨率深度和材质特征。 |
| 523 | `fbb07bad65276f2d` | 144 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 处理含 bit 7 的角色/敌人分类；本帧实际改写 stencil 128/132/144/148，包含 Remielle。 |
| 524 | `fbb941945a24dbbd` | 16 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 模板参考值 16 的分支；本帧没有实际颜色改写。 |
| 525 | `71181985065efac4` | 32 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 模板参考值 32 的着色分支，启用 TINT_SHADOW_ON 变体。 |
| 526 | `41ac7f87a76c8cb0` | 2 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 模板参考值 2 的 USE_KODAMA_GI 分支。 |
| 527 | `ddf41e8085d6aee9` | 32 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 模板参考值 32 的角色点光/镜面反射分支。 |
| 528 | `8c8669298e73aab1` | 0 | o0:R11G11B10_FLOAT, o1:R10G10B10A2_UNORM, oD:D32_FLOAT_S8X24_UINT | 在第一个 HDR 目标上完成前段合成；该运行时哈希没有静态序列化命中。 |
| 529 | `4628e1a3a9df9736` | 0 | o0:R11G11B10_FLOAT | 将第一个 HDR 目标通过网格 blit 写入后续 resolve 的独立输入资源。 |
| 530 | `d3edb6147a61a9bf` | 0 | o0:R11G11B10_FLOAT | 生成 1272x720 的 R11G11B10 环境/反射输入 d72d6162。 |
| 531 | `766bc167afe556fc` | 32 | o0:R11G11B10_FLOAT, oD:D32_FLOAT_S8X24_UINT | 模板值 32 的环境主 resolve：BRDF、环境光、AO、镜面、雾与场景 LUT。 |
| 532 | `f776a44ac6faf849` | 32 | o0:R11G11B10_FLOAT, oD:D32_FLOAT_S8X24_UINT | 把前段 HDR 中的角色/其他非零且非 32 分类恢复到最终目标。 |
| 533 | `8118b2aa16fabc93` | 0 | o0:R11G11B10_FLOAT, oD:D32_FLOAT_S8X24_UINT | 处理模板值 0 的背景分区，并应用其雾/LUT 路径。 |

## 已闭合与实时边界

抓帧层已经闭合：Remielle 材质 MRT 编码、draw 523 角色/敌人分支、draw 531 环境分支、模板分区及两条原生着色路径均有可重复证据。角色分支 HDR 逐位一致，环境分支在原生 R11G11B10 精度内逐值通过。

实时层尚未闭合。当前 Unity 展示仍由 Built-in Forward + HoyoToon 运行；要把这一原生 resolve 用到动画角色，必须实时生成四个 G-buffer、深度/模板、环境/反射、胶囊 AO 和场景 LUT。静态 DDS 只能用于审计，不能作为移动相机下的运行时输入。

## 权威产物

- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\native-gbuffer-deferred.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\captured-deferred-shading.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\deferred-shading-gpu-verification.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\captured-deferred-character.json`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\deferred-character-gpu-verification.json`
- `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Shaders\CapturedDeferredShading531.shader`
- `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Shaders\CapturedDeferredCharacter523.shader`
- `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Editor\RemielleDeferredShadingAudit.cs`
- `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Editor\RemielleDeferredCharacterAudit.cs`
