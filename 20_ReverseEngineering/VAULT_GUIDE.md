# Vault 分类总览（VAULT_GUIDE）

> `20_ReverseEngineering/AssetVault/` 是原 `RemielleAssetVault` 的**清理后工作副本**（2026-09-09 复制并清理，见 [AssetVault-Cleanup-Report.md](AssetVault-Cleanup-Report.md)）。原件在 `E:\ZZZ\local-only\RemielleAssetVault`，只读权威。
> 使用总则：**身份 = source block + CAB + pathID**，同名不代表同一资产（动画 829 源身份 / 810 名称）。原始数据只读；衍生物走 `50_AssetPipeline`。

## 按域查找表

| 域 | 当前权威位置（副本内） | 内容与规模 |
|---|---|---|
| **网格/模型** | `recovered/meshes/by-source/` | 178 个原始 Mesh 的蒙皮 GLB（含全部 UV 通道/切线/顶点色/蒙皮/子网格）；头发另有 UV1 派生版（有采集来源记录） |
| 骨架 | `recovered/skeletons/` | 472 原生骨骼 + Origin/PasSeul/Ramiel 三套 Animator 层级 |
| 模型原始源 | `assets/model/`、`assets/raw-primary/` | 原始导出与主源数据（raw-primary 7.3G，全库最大） |
| **动画（最高精度）** | `recovered/animations/highest-quality/` | 664 个完整分层 ACL 包 |
| 动画（独立） | `recovered/animations/standalone-native/` | 9 个独立 ACL + 156 个原生未压缩 |
| 动画解码依赖 | `recovered/animations/scalar-recovered/full-source/` | 完整解码必需的 JSON/raw/resS/ACL 与流式证据——**不是废弃产物，不得删** |
| 动画原始源 | `assets/animation/` | 4.5G 原始动画导出 |
| **材质** | `recovered/materials/` | 95 个材质包、410 个纹理引用全闭合 |
| 贴图 | `recovered/textures/` | 原生/导入纹理证据 |
| **渲染/着色器** | `assets/rendering/shared-shader-corpus/` | 21,877 文件：shader 字节码/反汇编/cbuffer/逐材质映射（`permaterial_map.json`、`migoto_hash_index.json`） |
| **特效** | `assets/effects/` | 现行权威为 `*-final` 目录：`effect-napsim-typed-final`、`effect-napsim-pptr-final`、`effect-dependency-typed-final`、`effect-dependency-pptr-final`、`effect-components-parsed/-typed`、`effect-materials`、`effect-cubemaps`、`effect-lensflare-textures` 等 |
| **时间线/运镜** | `assets/timeline/` | `timeline-clip-typed-final`、`timeline-clip-pptr-final`、`timeline-clip-raw`、`mainmenu-playable-directors{,-raw}`、`mainmenu-timeline-animationclips` |
| **控制器** | `assets/gameplay/controllers/` | 46 个原生 `.controller.json`（Origin/PasSeul/Ramiel × MainCity/NPC/UI/Timeline/Transform 等变体）+ `work/` 解析索引（`cab2blk.json`、`clip_pid2name.json`、`animators.json`）+ `summary.md` |
| 控制器修正 | `assets/gameplay/controllers-v2-corrected/` | 修正版 ControllerConstant 运动绑定输出（**preferred**） |
| 控制器证据 | `assets/gameplay/controllers 同级 controller-legacy-evidence/` | 攻击帧与遗留控制器证据（保留，非过时） |
| 环境 | `assets/environment/` | 环境资产 |
| 游戏侧能力 | `assets/gameplay/`（ability 等） | 技能/能力资产 |
| **账本/取证** | `recovered/ledger/`、`assets/evidence/` | 全语料 JSON 账本（`json_asset_ledger.jsonl`）、stage0 审计、`visual-authority-ledger-v5`（视觉权威，supersedes v4）、`reverse-checkpoint` |
| 溯源 | `assets/provenance/` | 来源证明（105M） |
| 元数据 | `metadata/` | `manifest.json`（96 来源注册）、`coverage.json`、`RECOVERY_STATUS.md`、`GAPS.md`、`verification.json` |
| 数学推导 | `skinning-verification/` | 蒙皮数学推导（含 renders） |
| 工具 | `tools/` | 恢复/同步/验证工具（注意：验证权威在原件，副本内工具路径指向原件） |
| 来源注册 | `config/` | `sources.json`（主源）、`additional_sources.json`、`excluded_sources.json`（47 项 SUPERSEDED/DIAGNOSTIC 排除记录） |

## 已知边界（继承 GAPS.md）

- 34 个优化 Animator：序列化 JSON 保留，通用反优化需各对象精确 Avatar。
- 2 个历史 WeatherConfig 字段：原始字节保留。
- 10 个 Timeline 运行时绑定：源序列化值为 null。
- 6 个网格无完整 GPU 几何证据（`Remielle_Sticker_02_R`、`Remielle_HairShadow`、`Remielle_Weapon_02_R/03_R/04`、`Remielle_Floater_02`），有源/骨骼/材质依据。

## 日常验证（针对原件）

```powershell
$pythonExe = 'D:\Anaconda\python.exe'
$vaultDir = 'E:\ZZZ\local-only\RemielleAssetVault'
& $pythonExe -X utf8 "$vaultDir\tools\verify_vault.py"
& $pythonExe -X utf8 "$vaultDir\tools\verify_recovered_assets.py"
```

副本不做全库验证（清理已造成预期偏差，见清理报告）；副本侧改动按 `50_AssetPipeline` manifest 规则自查。
