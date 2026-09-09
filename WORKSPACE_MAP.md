# 工作区地图（WORKSPACE_MAP）

既有资产的安置采取**"物理迁入"与"指针安置"并存**的策略：

- **物理迁入**：需要日常修改的工作产物复制/移动进本工作区。
- **指针安置**：已通过验收、其取证链（verification / evidence / baseline / 源选择 JSON）以绝对路径互相引用的资产库，**保持原地只读**，本表即其索引。搬动它们会打断已验收链的路径引用，得不偿失。

## 一、物理迁入（在本工作区内）

| 位置 | 内容 | 来源与说明 |
|---|---|---|
| `10_Unity/Remielle_Main` | Unity 6000.3.17f1 主工程工作副本（Assets 5.6G + Packages + ProjectSettings） | 2026-09-09 自 `E:\ZZZ\local-only\RemielleHoyoToon` 复制；剔除 Library/Temp/obj/Logs/UserSettings/*.csproj/*.sln（可再生产物）。原件已冻结，只开副本。 |
| `20_ReverseEngineering/AssetVault` | 逆向资产库清理后工作副本（214,611 文件 / 约 17.4G） | 2026-09-09 自 `RemielleAssetVault` 全量复制（262,280 文件 / 18.109 GB / 零失败）后清理：删 22 个过时 legacy 迭代目录（47,655 文件，manifest 判证）+ 机制垃圾 + .bak。原件保持只读权威。见 `20_ReverseEngineering/AssetVault-Cleanup-Report.md` 与 `VAULT_GUIDE.md`。git 仅入库其 .md 文档。 |
| `20_ReverseEngineering/{RuntimeRepair,DataAcquisition,RenderingReview,Evidence}` | 资产扩充四组（合计约 78,700 文件 / 约 31G） | 2026-09-09 按"散落资产盘点结论"物理囊括：RuntimeRepair=来源选择与修复产物（含 runtime-source-selection.json 身份链）、DataAcquisition=官方采集库、RenderingReview=渲染对照与原生回放基建、Evidence=13 个取证目录组（ModelReadiness/SceneLit*/Frame*/CharShader/MaterialRawDump/visual-acquisition）。原件留 local-only 只读；git 仅入库其 .md。见 `20_ReverseEngineering/AssetInclusion-Report-20260909.md`。 |

## 二、指针安置（留在原地，一律只读）

| 指针目标 | 内容 | 与本工作区的关系 |
|---|---|---|
| `E:\ZZZ\local-only\RemielleAssetVault`（19G） | 权威逆向库原件：96 来源完整注册集（含全部 legacy 回归对照）、`recovered/meshes/by-source`、`recovered/animations`（664 完整分层 ACL 等）、蒙皮数学推导、验证工具 | **只读权威原件**。2026-09-09 起本工作区持有其清理后副本（见上表），全库验证与 legacy 回归对照以原件为准 |
| `E:\ZZZ\local-only\RemielleHoyoToon` | 冻结原始工程（已验收基线） | 副本的对照原件；不再打开、不再修改 |
| `E:\ZZZ\local-only\RemielleHandoff` | 统一交接文档、`Run-Checks.ps1` 检查入口、`baseline.json` 基线 | 验收与门禁的权威定义。注意：其基线针对冻结原件；副本侧的门禁基线待按 AGENTS.md 流程独立建立后另行登记 |
| `E:\ZZZ\local-only\RemielleControllerDependencies` | `implementation/`（现行控制器的分阶段实现文档与验证 JSON：输入/命中/停帧/震动/镜头/拖尾/粒子各阶段）+ `downloads/`（OpenKCC、UnityHFSM、Cinemachine、Animancer Pro、StickWeaponTrailEffect、BBB-Nexus 原始包） | 现行控制器能力边界的第一手资料；外部包的原始出处 |
| `E:\ZZZ\local-only\RemielleControllerPreparation / -Implementation / -Integration` | 控制器原生取证链（oracle/vectors/evidence/unity-verification、状态合同、开源选型） | 控制器行为规格的取证依据 |
| `E:\ZZZ\local-only\RemielleStaticShowcase` | 静态展示工程（URP） | 独立用途，勿与主工程混淆 |
| `E:\ZZZ\local-only\RemielleModelReadiness` 等 13 个取证目录 | 原件（副本已在 `20_ReverseEngineering/Evidence/`） | 渲染、光照、管线、材质、模型验收取证的原件，只读 |
| `E:\ZZZ\local-only\RemielleFullAudit`、`reverse-notes`、`vendor`、`workspace-repair-20260904` | 全量审计、逆向笔记、第三方 vendored、修复工作目录 | 性质待定的未囊括项，只读指针；是否物理入库另行裁决 |
| `E:\ZZZ\local-only\RemielleZcodezcode-loops.md` | 不合格 controller 工作的环索引残留文件 | 待用户裁决删除；不在任何囊括范围 |
| `E:\ZZZ\Remielle` | 立绘、FBX 格式文档、源引用 | 参考资料 |
| `E:\ZZZ\extracted`、`E:\ZZZ\Maps` | 游戏解包与地图数据 | 只读参考 |
| `E:\ZZZ\tools\UnifiedCapture`、`E:\ZZZ\FrameTools` | 捕获与帧工具 | 工具指针（登记于 `70_Automation`） |

## 三、历史裁决记录

- 2026-09-09：按用户裁决，不合格的旧 controller 工作（原 `E:\ZZZ\ZCODE` 独立实验工程与 `local-only\RemielleZcode` 文档目录）已彻底删除，未保留归档。现行唯一认可的 controller 是主工程内 `Assets/ControllerRuntime` + `Assets/ControllerIntegration` 及其 2026-09-08~09 阶段成果。
