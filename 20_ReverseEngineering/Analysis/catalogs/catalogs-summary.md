# B1 v1 资产目录摘要（2026-09-09）

机器生成：`70_Automation/AssetScripts/build_catalogs.py`；原始摘要 `catalogs-summary.json`。云端 AI 与上级可直接检索下列 CSV——这是"资产本体不上云、目录与口径上云"的第一层。

## 目录清单与关键数字

| CSV | 内容 | 规模 | 与文档声称的对账 |
|---|---|---:|---|
| `animation-catalog.csv` / `animation-names.csv` | 全量动画文件与名称视图（tier/source/CAB/文件/大小） | 3,821 文件 | **829 身份已机器验证**：highest-quality 664 .npz + standalone 9 .npz + 156 .anim = 829，全去重 ✓ |
| `mesh-catalog.csv` | recovered/meshes/by-source 全枚举 | 180 文件 | 对照"178 个原始 Mesh GLB"：178+2（附属文件），吻合 |
| `material-texture-catalog.csv` | 材质/贴图 by-source 文件枚举 | 152 文件 | "95 材质包 / 410 纹理引用"是包与引用口径，与文件数口径不同；包内引用见 `recovered/materials/material_recovery_report.json` |
| `controller-catalog.csv` | 46 个原生 .controller.json + work 解析索引 + v2 修正集 + 攻击帧证据 | 627 文件 | — |
| `effects-timeline-inventory.csv` | 特效 13 域 + 时间线 10 域目录级清单 | 23 条目 | 现行权威为 `*-final` 目录 |
| 主工程动作库 | `Assets/StreamingAssets/RemielleControllerMotions/index.json` | **335 motions / 456 slots** | — |

## 口径说明（诚实边界）

- **名称级去重为 827**（简单词干），与文档声称的 810 存在 17 个差异，预计来自逻辑片名归一（大小写/变体后缀）。v2 按 `recovered/ledger/json_asset_ledger.jsonl` 的权威分类归一后复核。
- `scalar-full-source` 的 829 个 .raw 是解码依赖（通用文件名），不计入身份口径。

## 发现的隐患（已记 TODO-D1）

主工程动作库 `index.json` 的 `profiles.path` 指向 **local-only 原件的绝对路径**（binding-profiles.json）。在副本中运行动作系统前须确认该引用的行为（回退/缺失时的表现），属 D1 绝对路径审计范围。
