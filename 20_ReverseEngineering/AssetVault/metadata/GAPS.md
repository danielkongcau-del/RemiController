# 当前恢复范围与证据边界

更新于 2026-09-04 清理后的全库复验。机器来源：[recovery_summary.json](recovery_summary.json)、[recovery_verification.json](recovery_verification.json)。
模型使用与后续计划见 [交接文档](../../RemielleHandoff/README.md)。旧版“24 个纹理缺口、占位骨未解决、26 网格预览”描述已失效。

| 范围 | 当前状态 |
|---|---|
| 178 个原始 Mesh 的骨骼路径 | 已全部解析；原先两根领子骨及历史羽毛特效路径均有来源证据 |
| 原先 24 个纹理 PPtr 缺口 | 已解析并提供原生/导入纹理依据；95 个材质包中 410 个纹理引用闭合 |
| 原生动画 | 829 个源身份完整恢复：664 分层 ACL、9 独立 ACL、156 未压缩；旧不完整派生物已退役 |
| 角色/后处理 LUT、Wings 透射输入 | 已取得原始数据并接入当前工程；不代表全部游戏渲染阶段一致 |
| 当前 27 个网格的捕获 GPU 几何 | 21 个完整匹配；另 6 个有源、骨骼与材质依据但没有完整 GPU 几何证据 |

六个网格：`Remielle_Sticker_02_R`、`Remielle_HairShadow`、`Remielle_Weapon_02_R`、`Remielle_Weapon_03_R`、`Remielle_Floater_02`、`Remielle_Weapon_04`。
原始 renderer 的序列化 Mesh 可以为 null；几何/材质匹配不能声称已经读到了运行时 Unity 对象实例指针。

全库还有以下专门范围，原始数据均保留，不凭空补值：

- **34 个优化 Animator**：序列化 JSON 已保留；通用解优化仍需各对象精确的 Avatar/external 层级。
- **2 个历史 WeatherConfig 字段**：原始字节保留；需对应版本 TypeTree/程序集或精确运行时对象证据。
- **10 个 Timeline 运行时绑定**：已证明源序列化值为 null；若需要管理器实际注入身份，须额外运行时采集。

模型阶段仍未覆盖完整游戏延迟渲染/环境后处理、附件显隐、输入控制器、全部技能事件和所有动作的游戏内对照。
这些边界不能解释成当前纹理/骨架仍损坏，也不能因本次模型验收通过而删除。
