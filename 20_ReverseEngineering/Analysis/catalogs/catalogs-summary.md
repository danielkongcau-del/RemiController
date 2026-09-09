# B1 v1 资产目录摘要（2026-09-09）

机器生成：`70_Automation/AssetScripts/build_catalogs.py`；原始摘要 `catalogs-summary.json`。云端 AI 与上级可直接检索下列 CSV——这是"资产本体不上云、目录与口径上云"的第一层。

## 目录清单与关键数字（2026-09-09 v2：文件级与身份级分表）

| CSV | 内容 | 规模 | 口径说明 |
|---|---|---:|---|
| `animation-identities.csv` | **恢复包身份表**（source_id, cab, name, recovery_products, files） | **829 身份** | 与 RECOVERY_STATUS 声称精确一致；tier 为恢复产品属性、不改身份 |
| `animation-catalog.csv` | 文件级全枚举（含 scalar 解码依赖与索引文件） | 3,821 文件 | 名称列可能是数字词干——文件视角，非资产语义 |
| `animation-names.csv` | 名称视图 | 1,478 词干名（全身份 2,968） | 简单词干去重 827（包身份口径）与文档 810 的差异**待用 ledger 逻辑片名归一解释，不做字符串强并** |
| `mesh-catalog.csv` | recovered/meshes/by-source 全枚举 | 180 文件 | 对照"178 GLB"：178+2 附属文件 |
| `material-texture-catalog.csv` | 材质/贴图 by-source 文件枚举 | 152 文件 | "95 包/410 引用"为包与引用口径，文件数口径不同 |
| `controller-catalog.csv` | 46 控制器 JSON + work 索引 + v2 修正集 | 627 文件 | — |
| `effects-timeline-inventory.csv` | 特效 13 域 + 时间线 10 域 | 23 条目 | 现行权威 `*-final` |
| 主工程动作库 | index.json | 335 motions / 456 slots | profiles.path 为 A 类原件引用（已登记） |

## 口径声明（诚实边界）

- **文件表 ≠ 资产身份表**：身份 = source block + CAB + pathID 语义（本目录以 source_id+cab+名称近似；pathID 级精确表待 B1-v2 从 ledger 生成）。
- v2 待办：pathID 级身份表、827→810 归一解释、locomotion 语义分类。

## 发现的隐患（已登记）

主工程动作库 `index.json` 的 `profiles.path` 指向 local-only 原件（A 类输入，合规，已登记 TODO-D1）。
