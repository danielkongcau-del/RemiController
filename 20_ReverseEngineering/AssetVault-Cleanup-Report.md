# Vault 副本清理报告（2026-09-09）

## 背景

应用户指令变更边界：将原项目全量逆向资产复制进本工作区，并在**副本内**清除错误与过时数据、归类放置。**原项目与原件一律未动**（`E:\ZZZ\local-only\RemielleAssetVault` 保持完整、只读、权威）。

## 复制核验

- 源：`E:\ZZZ\local-only\RemielleAssetVault`（原件，保持不变）
- 目标：`ZCode/20_ReverseEngineering/AssetVault`（工作副本）
- robocopy 结果：26,261 个目录、262,280 个文件、18.109 GB，**失败 0、不匹配 0、跳过 0**
- 副本来源无 reparse point；原件中的 NTFS 硬链接在副本中成为独立文件，副本内任何操作不影响原件

## 清除清单（仅副本，逐项有据）

### 1. 过时的迭代数据：`assets/legacy/` 全部 22 个 `legacy-*` 目录

47,655 个文件 / 723 MB。判据 = 副本内 `metadata/manifest.json` 各来源注册注释（全部点名后继版本）与 `config/excluded_sources.json` 的机器分类：

| legacy 迭代 | 证据（manifest note） |
|---|---|
| effect-dependency-mb-pptr-closure p0h3 / p0h3-fullindex / p0h3-fullindex2 | Superseded by fullindex3 correction |
| effect-dependency-mb-typed-p0h | Superseded by p0h3 |
| effect-napsim-pptr-closure p0f / p0f2 / p0f3 | Superseded by p0g2 |
| effect-napsim-typed-p0e | Superseded by corrected p0g2 corpus |
| effect-napsim-typed-p0g1-regression | REGRESSION_OUTPUT（"retained at source; p0g2 is the vault authority"——原件保留回归对照，副本清除） |
| effect-typed-components / -p0d2 / -p0d3 | Superseded by p0d4 |
| mesh-identity-sources-v1 | Superseded by all_v1 |
| timeline-clip-mb-pptr-closure p0h5 / p0h5-fullindex | Superseded by p0h14-final |
| timeline-clip-mb-typed p0h4 / p0h5 / p0h7-exact / p0h11d | Superseded by p0h11e |
| timeline-weather-mb-typed p0h6 / p0h7-exact / p0h9 | Superseded by p0h15 |

现役权威（保留在副本）：`assets/effects/*-final`（napsim/dependency 的 typed/pptr final）、`assets/timeline/timeline-clip-*-final` 等。

### 2. 机制垃圾：12 个文件 + 5 个 `__pycache__` 目录

`.pyc/.tmp/.lock/Thumbs.db/desktop.ini` 等；位于 `assets/evidence/asset-audit-stage0`、`assets/gameplay/controllers/work{,/ctrlbin}`、`assets/rendering/shared-shader-corpus/_fix`、`tools/`。

### 3. 备份残片：2 个 `.bak`

`assets/gameplay/controllers/work/extract-src/Program.cs.bak`、`assets/rendering/shared-shader-corpus/_fix/join.json.bak`。

## 数学校验

262,280（复制）− 47,655（legacy）− 12（垃圾）− 2（bak）= **214,611 = 清理后实测文件数**，零偏差。

## 保留的存疑项（待裁决，未删）

- `assets/effects/effect-p0c1-legacy`（695 文件）：manifest note 为 "Earlier effect export retained for provenance and regression comparison"——**未点名后继版本**，不排除是某些 effect 域的唯一注册导出。保守保留，如确认有后继再清。
- `assets/gameplay/controllers/work/`（178 MB）：注册在案（`controllers` 源，588 文件，"controller audit data"），内含 cab2blk.json、clip_pid2name.json、animators.json 等解析索引，**对控制器取证有直接价值**，保留。

## 副本与原件的关系（重要）

- **原件 = 完整权威注册集**（96 来源 / 257,275 注册文件，manifest 与实物一致，含全部 legacy 回归对照）。
- **副本 = 清理后的工作集**（214,611 文件）。副本内 `metadata/manifest.json` 仍登记 96 来源（含已清除的 legacy），属预期偏差——vault 内部 manifest 是溯源数据、不手改；一切注册一致性以原件为准。需要 legacy 回归对照时回原件查询。
- git：副本整体不入库（仅其 `.md` 文档入库）；策展后的解读与索引沉淀在 `Analysis/` 与 `00_ProjectHub/Inventory/`。
