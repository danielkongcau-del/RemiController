# 资产检索标准（ASSET_LOOKUP）—— 唯一权威查找路径

> 本文件是**强制标准**（已固化进根目录 `AGENTS.md` 硬性红线）：工作区内任何代理查找资产必须按本文件的链条与口径执行。

## 铁律：身份检索，不是名字检索

- 资产身份 = **source block（源块）+ CAB（容器）+ pathID**。名字只是线索，**同名不证明同资产**：动画 829 个源身份只对应 810 个名称，同名的不同版本必须按身份区分。
- 按名字检索失败 ≠ 数据不存在。名字查不到时，必须换身份口径复查（账本按 `name`/`type`/`schema` 字段、控制器侧按 pathID 索引），仍无结果才允许进入下一站。
- 副本内账本（`AssetVault/recovered/ledger/json_asset_ledger.jsonl`）的 `path` 字段指向 local-only 原件——用 `sourceId` + 相对路径尾段映射回工作区副本（两者目录结构同构）。

## 四级查找链（顺序不可跳级）

```
① 工作区逆向层（Vault 副本 + 四组扩充 + 主工程副本）
② E:\ZZZ\extracted（99G 全量静态解包）
③a 补充解包（安装目录中 extracted 未覆盖/已更新的 bundle）
③b 深度静态逆向（已提取未解码：TypeTree/程序集/解码器）
④  运行时注入/采集（准入门槛：静态侧被"证明"缺失，非"没找到"）
```

### 站① 工作区逆向层 —— 日常默认起点

| 检索目标 | 入口 |
|---|---|
| 按域导航 | `VAULT_GUIDE.md`（网格/动画/材质/特效/时间线/控制器/账本各自的权威位置） |
| 按路径/大小 | `../Manifests/*.sha256.csv`（292,906 文件全量枚举） |
| 按名称/类型/schema | `AssetVault/recovered/ledger/json_asset_ledger.jsonl`（54,549 条 JSON 语料：sourceId/path/sha256/kind/schema/name/type/typed/partial） |
| 动画 pathID↔名称 | `AssetVault/assets/gameplay/controllers/work/clip_pid2name.json`（pathID → 片名） |
| CAB↔源块 | 同目录 `cab2blk.json`、`blk_list.txt`、`animator_index_entries.json` |
| 动画/网格/材质/控制器目录级 | `../Analysis/catalogs/*.csv` |
| 渲染/采集/取证 | 四组扩充（RenderingReview/DataAcquisition/RuntimeRepair/Evidence），入口见各组 README/REPORT |
| 主工程已接入资产 | `10_Unity/Remielle_Main/Assets`（动作库 index：335 motions/456 slots） |

### 站② extracted 全量解包

- 根：`E:\ZZZ\extracted\gameassets`（99G；Vault 的 96 个注册来源全部由此按"Remielle 相关"筛选而来）。
- 身份穿越：`AssetVault/config/sources.json`（来源 id → extracted 路径映射）与 `metadata/manifest.json`（每来源的 origin 路径、文件数、linked/copied）。目录布局按身份层级（source/CAB/...）。
- **发现新解包目录**：`sources.json` 的 `discovery` 规则（`directoryPattern: remielle_*`）即为此设计——extracted 里新出现的 Remielle 目录按此登记入 Vault。

### 站③a 补充解包

- 触发条件：确认游戏安装版本比 extracted 新、或目标 bundle 未被 extracted 覆盖。
- 执行：用既有解包工具链对安装目录（`E:\ZZZ\miHoYo Launcher\games\ZenlessZoneZero Game`）补充提取 → 落 `Raw/` → 按 `sources.json` discovery 登记。

### 站③b 深度静态逆向

- 触发条件：字节已在 extracted/Vault 里，但无人能解码（"有数据、无解析器"）。
- 已知待解清单（`AssetVault/metadata/GAPS.md`）：34 个优化 Animator（需各对象 external Avatar 层级）、2 个 WeatherConfig 历史字段（需历史 TypeTree/程序集）、运行时 Timeline 绑定（序列化已证 null）。
- 手段：TypeTree/managed 程序集比对、解码器开发（先例：ACL 编解码器、`tools/recover_*.py` 链）。

### 站④ 运行时注入/采集 —— 最后手段

- **准入门槛**：静态侧被**证明**缺失（对照 GAPS.md 的 runtime-only 清单），而非"我没找到"。不在 GAPS 清单且静态查不到的，先回头用身份口径重查。
- 基建：UnifiedCapture / 帧工具链（`E:\ZZZ\tools`、FrameTools）；采集验证流程与回滚套件在 `DataAcquisition/20260904`（含原版 dll + 恢复脚本）。

## 回流纪律（每一跳的产出必须归位）

1. 新采集/新解包 → `Raw/`（只增不改）。
2. 加工/解码产物 → `50_AssetPipeline`（Incoming→…→UnityReady）。
3. 归位后**重生成清单**（`70_Automation/AssetScripts/generate_manifests.py`）并提交，保持云端可复验。
4. 语义结论沉淀 `Analysis/` 与 `00_ProjectHub/Inventory`。

## "绝对没有"的裁决口径

只有同时满足以下条件才允许宣布某资产不存在，并需留下检索轨迹（查了哪些站、用了什么口径）：
①按身份口径查过站①账本与清单；②查过站② extracted（按 source/CAB/pathID）；③排除站③a 版本缺口；④对照 GAPS.md 排除站④准入类。此后结论只能是"需要采集或继续解包"，并记录进当前任务的报告。
