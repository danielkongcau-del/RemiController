# 附件源状态补全

此前 native-visibility-targets.json 的 enabled / isActive 为 null，原因是旧 AnimeStudio 解析器没有保留相应字段：Renderer 将 m_Enabled 读入临时变量；GameObject 读取名称后结束，没有输出尾部的 tag / activeSelf。本次独立解码原始记录，未修改原始 raw、JSON、模型或 Unity prefab。

## 已恢复

- 完整解析 486 个 GameObject：组件 PPtr、层、名称、tag 和 activeSelf。逐项与已有对象目录交叉核对，严格消费完整记录，验证原始 SHA-256。所有源节点 activeSelf 都为 true。
- 从当前 27 个原始 Renderer 头部恢复 enabled，全部为 true；PPtr 指向的 GameObject 与独立 renderer JSON 一致。
- 关联原始 Transform 层级、默认 TRS 和 rootBone 身份，父链无环、source block + CAB + pathID 一致。
- 将当前展示／商店实际参与绘制的 7 个源网格与源 renderer 身份相连。剩余 20 个部件仍保留在正式源 prefab 中。

**这些是源文件默认状态，不是游戏运行时显隐时间表。** 默认值都开启，并不能说明商店页应把所有武器和技能附件画出来。游戏运行时的网格填入、事件、骨骼缩放、对象管理器等仍可能改变结果，不能把 null 补成 true 后强行显示全部部件。

## 当前来源表

| 部件 | 原始根骨 | 源 enabled | 当前展示／商店链 |
|---|---|---|---|
| Remielle_Weapon_02_L | Ctr_L_Wpn | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Sticker_02_R | Ctr_R_Wpn | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Cannon_R_04 | Skn_R_WingC_ZD_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Face | Bip001 Head | true | 参与原生绘制 |
| Remielle_Sticker_02_L | Ctr_L_Wpn | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_05_L | Skn_L_WingC_ZA_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_03_L | Ctr_L_Wpn_Z_03 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_HairShadow | Bip001 Spine2 | true | 参与原生绘制 |
| Remielle_Cannon_L_04 | Skn_L_WingC_ZD_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Wings | Bip001 Spine | true | 参与原生绘制 |
| Remielle_Cannon_R_01 | Skn_R_WingC_ZA_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Cannon_R_03 | Skn_R_WingC_ZC_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_02_R | Ctr_R_Wpn | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_01 | Skn_WingC_Root | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Cannon_R_02 | Skn_R_WingC_ZB_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Hair | Bip001 Spine2 | true | 参与原生绘制 |
| Remielle_Cannon_L_03 | Skn_L_WingC_ZC_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_05_R | Skn_R_WingC_ZA_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Weapon_03_R | Ctr_R_Wpn_Z_03 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Origin_Body_1 | Bip001 Pelvis | true | 参与原生绘制 |
| Remielle_Sticker_01 | Skn_WingC_Root | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Cannon_L_02 | Skn_L_WingC_ZB_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Origin_Body_2 | Bip001 Pelvis | true | 参与原生绘制 |
| Remielle_Floater_02 | Skn_L_Flo_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Eyebrow | Bip001 Head | true | 参与原生绘制 |
| Remielle_Weapon_04 | Ctr_R_Wpn_Z_01 | true | 源 prefab 保留，所选两页序列未绘制 |
| Remielle_Cannon_L_01 | Skn_L_WingC_ZA_01 | true | 源 prefab 保留，所选两页序列未绘制 |

## 使用与后续

脚本：E:\ZZZ\local-only\RemielleRenderingReview\20260905\recover_ui_source_visibility.py。

机器记录：[source-visibility.json](source-visibility.json)。每行含原始 renderer、GameObject、Transform、rootBone、相关源网格、原始字节偏移、哈希与已有捕获证据等级；不能单凭同名 Mesh 配对。

这份结果补全了“原文件是否真的缺启用状态”的问题。全组件视图中的离体小环具体归属、动作事件和实际显隐仍需继续核实，没有删网格或恢复旧的 26 网格配置。展示链目前按真实两页绘制来源选择；战斗附件与控制器接入在后续。

布局核对参考：[AssetsTools.NET 原作者文档](https://github.com/nesrak1/AssetsTools.NET/wiki/Getting-Started%3A-Assets-file-writing)、[读取 GameObject 尾部字段的实现](https://github.com/Modder4869/__Studio/blob/master/AssetStudio/Classes/GameObject.cs)。具体游戏数据以本地原始记录和一致性验证为准。
