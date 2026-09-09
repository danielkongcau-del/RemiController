# 20_ReverseEngineering — 逆向层

## 分区

- `Raw/`：**新采集的原始数据**（Models/Animations/Textures/Materials/VFX/Audio/Config/Scripts/Dumps）。只增不改；原始文件不改写。
- `Remielle/`：Remielle 专属资产的识别与分类（按动作类型分域：Locomotion/BasicAttack/Special/EXSpecial/Dodge/...）。
- `Analysis/`：分析归纳产物——Animation_Mapping（动画映射）、VFX_Mapping（特效归属）、Skill_Timeline（技能时间线）、Camera_Mapping（运镜）、Parameter_Research（参数研究，如 walk→fly 触发条件）。
- `Tools/`：本层使用的新工具（既有工具指针见 `../70_Automation`）。

## 重要：Vault 副本已物理入库（2026-09-09 边界变更）

`AssetVault/` 是原 `RemielleAssetVault` 的**清理后工作副本**：262,280 文件全量复制（18.109 GB，零失败）后，清除 22 个 manifest 判定过时的 legacy 迭代目录（47,655 文件）、机制垃圾与备份残片，余 214,611 文件。详见 [AssetVault-Cleanup-Report.md](AssetVault-Cleanup-Report.md)；按域查找见 [VAULT_GUIDE.md](VAULT_GUIDE.md)。

- **原件**（`E:\ZZZ\local-only\RemielleAssetVault`）：只读权威、完整注册集与回归对照；需要 legacy 对照或全库验证时查原件。
- **副本**（本目录 `AssetVault/`）：日常检索与后续加工的工作集；git 仅入库其 `.md` 文档。
- 副本内数据同样**只读**（身份完整性）；衍生物走 `../50_AssetPipeline`。

本层其余职责不变：`Raw/` 承接新采集，`Analysis/` 承接解读归纳，结论沉淀进 `../00_ProjectHub/Inventory`。当前最大任务仍是对资产做**完整查询归纳**（TODO-B1，现在有了物理基座）。
