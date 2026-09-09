# 20_ReverseEngineering — 逆向层

## 分区

- `Raw/`：**新采集的原始数据**（Models/Animations/Textures/Materials/VFX/Audio/Config/Scripts/Dumps）。只增不改；原始文件不改写。
- `Remielle/`：Remielle 专属资产的识别与分类（按动作类型分域：Locomotion/BasicAttack/Special/EXSpecial/Dodge/...）。
- `Analysis/`：分析归纳产物——Animation_Mapping（动画映射）、VFX_Mapping（特效归属）、Skill_Timeline（技能时间线）、Camera_Mapping（运镜）、Parameter_Research（参数研究，如 walk→fly 触发条件）。
- `Tools/`：本层使用的新工具（既有工具指针见 `../70_Automation`）。

## 目录构成（2026-09-09 扩充：全量资产物理入库）

**资产副本五组**（数据只读、衍生物走 `../50_AssetPipeline`、git 仅入库其 `.md`）：

| 目录 | 内容 | 说明 |
|---|---|---|
| `AssetVault/` | 逆向资产库清理后副本 | 见 [VAULT_GUIDE.md](VAULT_GUIDE.md) 与 [AssetVault-Cleanup-Report.md](AssetVault-Cleanup-Report.md) |
| `RuntimeRepair/` | 运行时来源选择与修复产物 | 含 `runtime-source-selection.json`（当前来源选择身份链）、acl-native-source、动画目录、effect-bone 修复与验证 |
| `DataAcquisition/` | 官方采集库 | 帧分析、基础模型材质/渲染器/绑骨/贴图绑定导出、artifact-manifest |
| `RenderingReview/` | 渲染对照与原生回放基建 | NativePlayer、Replay 工程（cpp/shader）、对比页面与捕获 |
| `Evidence/` | 取证证据组（13 目录） | RemielleModelReadiness（模型验收+正式 Player）、SceneLit*、Frame*、CharacterShaderEvidence、MaterialRawDump、visual-acquisition |

**工作区**：`Raw/` 新采集（只增不改）、`Remielle/` Remielle 分类、`Analysis/` 解读归纳、`Tools/` 本层工具。

原件全部留在 `local-only` 只读（指针见 `../WORKSPACE_MAP.md`）；需要回归对照或全库验证时查原件。当前最大任务仍是对资产做**完整查询归纳**（TODO-B1）。
