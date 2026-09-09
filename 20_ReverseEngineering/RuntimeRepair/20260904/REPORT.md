# 当前模型来源与准备工具

本目录名称保留，但以下内容仍是当前正式重建依赖。统一使用方法与修复推导见 [交接文档](../../RemielleHandoff/README.md)。

| 入口 | 用途 |
|---|---|
| `runtime-source-selection.json` | 当前 27 个原皮网格的精确来源选择与证据 |
| `qualified-mesh-audit.json` | 网格版本、骨架与捕获几何对应 |
| `prepare_runtime_sources.py` | 准备正确 GLB、头发 UV 派生及来源记录 |
| `prepare_exact_material_inputs.py` | 当前材质的源身份、贴图方向与参数输入 |
| `inspect_native_animation_rig.py` | 原始骨架和组装骨架对应 |
| `prepare_animation_inputs.py` | 最高精度原生动画到 Unity bytes 的打包 |
| `prepare_lut_verification.py` | LUT/透射验证输入的准备 |
| `acquire_all_animation_sources.py` | 按已登记源身份提取完整原生动画输入 |
| `acl-native-source`、`animation-catalogs` | 原生包装器来源与动画对象目录 |
| `native-visibility-raw`、`native-active-flags.json` | 后续附件显隐所需的原始对象与状态证据 |
| `effect-bone-path-evidence.json`、骨骼修复记录 | 原先骨骼名称缺口的解决依据 |
| `vault-path-check` | junction/长路径包含检查的安全回归夹具 |

骨骼与纹理缺口、角色/后处理 LUT、原生动画层级均已回填。旧低精度获取脚本、一次性骨骼改名脚本、before 和临时探针已删除，正确结果与来源记录保留。
此目录中的既有 Unity/动画/聚合 JSON 是当时成功验证快照；最终模型状态以 [ModelReadiness/verification.json](../../RemielleModelReadiness/20260904/verification.json) 为准。

仍须区分 21 个完整 GPU 几何匹配网格与另 6 个仅有源/骨骼/材质依据的网格；不能把静态 renderer 的 null Mesh 自动解释为运行时实例已确认。
