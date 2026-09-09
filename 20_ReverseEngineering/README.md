# 20_ReverseEngineering — 逆向层

## 分区

- `Raw/`：**新采集的原始数据**（Models/Animations/Textures/Materials/VFX/Audio/Config/Scripts/Dumps）。只增不改；原始文件不改写。
- `Remielle/`：Remielle 专属资产的识别与分类（按动作类型分域：Locomotion/BasicAttack/Special/EXSpecial/Dodge/...）。
- `Analysis/`：分析归纳产物——Animation_Mapping（动画映射）、VFX_Mapping（特效归属）、Skill_Timeline（技能时间线）、Camera_Mapping（运镜）、Parameter_Research（参数研究，如 walk→fly 触发条件）。
- `Tools/`：本层使用的新工具（既有工具指针见 `../70_Automation`）。

## 重要：权威库不在这里

已验收的逆向资产库在 `E:\ZZZ\local-only\RemielleAssetVault`（19G，含 664 完整分层 ACL 等），连同各证据目录一并**原地只读**（指针表见 `../WORKSPACE_MAP.md`）。理由：其取证链以绝对路径互相引用，搬动会打断已验收链。

本层职责：**新的**采集、**新的**分析归纳，以及把权威库的结论沉淀为索引（`../00_ProjectHub/Inventory`）。

## 当前最大任务

对既有逆向资产做一次**完整查询归纳**（TODO-B1）：动画 829 源身份、粒子 31 项、运镜曲线、材质，产出各索引文档。
