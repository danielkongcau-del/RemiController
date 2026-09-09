# ZCode 工作区代理须知

> 本文件对进入 `E:\ZZZ\ZCode` 的**任意 coding agent**（ZCode、DeepSeek Harness 或其他）生效。
> 术语约定：一律使用"代理 / coding agent"等中立指称；历史文档中的 "Codex/codex" 均理解为"任意 coding agent"，不得特指某一产品。

## 分区规则

- `00_ProjectHub` 知识层：先读后写。开工前读 `CURRENT_STATUS.md`/`TODO.md`/`Inventory`；收工时回写状态与索引。
- `10_Unity/Remielle_Main` 唯一权威主工程：保持可运行。第三方完整仓库**不得**直接拖入，一律先进 `30_ExternalRepos` 与 `40_Integration`。
- `20_ReverseEngineering`：`AssetVault/` 为逆向资产库**清理后副本**（数据只读，身份 = source block + CAB + pathID；衍生物走 50_AssetPipeline；全库验证与 legacy 回归对照查 local-only 原件）；`Raw/` 只增不改；分析与归纳产物落 `Analysis/`；其余既有取证目录（指针见 `WORKSPACE_MAP.md`）只读。
- `30_ExternalRepos` 上游第三方仓库：除非用户明确要求，不修改、不提交。
- `40_Integration` 架构移植实验室：候选控制器/系统在此做适配原型与架构验证，验证通过才合并主工程。
- `50_AssetPipeline` 资产中转：按 Incoming → Converted → Cleaned → Retargeted → UnityReady 流转，被拒项进 Rejected 并记录原因。
- `60_Experiments` 实验区：想法先在这里原型化，不污染主工程；失败进 `99_Archive`。
- `70_Automation/Agents` 代理工作档：任务、提示词、汇报统一归档，规范见该目录 `AGENTS.md`。
- `99_Archive` 废弃保留：只进不改。

## 硬性红线（继承自 E:\ZZZ\AGENTS.md，全部继续有效）

- 原始资产、提取数据与本地工具保持本地，不上传；原始文件不改写。资产身份 = source block + CAB + pathID，external 表按 source block + CAB 查找。
- `E:\ZZZ\local-only\` 下全部既有目录是**已验收基线，一律只读**。新工作只落 `ZCode/`。
- `E:\ZZZ\local-only\RemielleHoyoToon` 是**冻结原始工程**：不再用编辑器打开、不再改动、不再往其阶段目录写产物。日常开发一律在 `10_Unity/Remielle_Main`（2026-09-09 复制生成的工作副本）。
- 批处理渲染必须 D3D11，禁止 `-nographics`；同一工程不得并发打开。
- 表现层 Euler(90,180,0)、Body 背面 UV3、Face SDF 在 `_LightTex`、LUT 不互换不重复施加等管线约定，见 `E:\ZZZ\AGENTS.md`，在本工作副本中继续有效。
- 数学与蒙皮推导以 `RemielleAssetVault/skinning-verification/skinning-math-derivation.md` 为准。

## 本工作副本特有的危险点

- 主工程内的编辑器工具（构建/审计脚本）存在**指向 local-only 旧阶段的硬编码绝对路径**。在副本中运行任何工具前，先审计其输出路径：新产物一律改落 `ZCode/` 侧目录（`90_Builds`、`70_Automation/Agents/Reports` 或对应阶段目录），不得写回 local-only 基线。该审计是 TODO 中的在办事项。
- 副本与冻结原件同名同 GUID：**严禁两者同时在 Unity 中打开**。只开副本。

## 工作循环

```
进入工作区
  → 读 README / CURRENT_STATUS / TODO
  → 查 00_ProjectHub/Inventory 与 WORKSPACE_MAP（先指针后翻盘）
  → 检索逆向层 / 外部仓库层取证
  → 60_Experiments 原型 → 40_Integration 验证 → 合并 10_Unity/Remielle_Main
  → 回写 CURRENT_STATUS / TODO / Inventory / 汇报档
```
