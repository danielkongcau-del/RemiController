# ZCode 工作区代理须知

> 本文件对进入 `E:\ZZZ\ZCode` 的**任意 coding agent**（ZCode、DeepSeek Harness 或其他）生效。
> 术语约定：一律使用"代理 / coding agent"等中立指称；历史文档中的 "Codex/codex" 均理解为"任意 coding agent"，不得特指某一产品。

## 分区规则

- `00_ProjectHub` 知识层：先读后写。开工前读 `CURRENT_STATUS.md`/`TODO.md`/`Inventory`；收工时回写状态与索引。
- `10_Unity/Remielle_Main` 唯一权威主工程：保持可运行。第三方完整仓库**不得**直接拖入，一律先进 `30_ExternalRepos` 与 `40_Integration`。
- `20_ReverseEngineering`：五组资产副本（`AssetVault`/`RuntimeRepair`/`DataAcquisition`/`RenderingReview`/`Evidence`）数据一律只读（身份 = source block + CAB + pathID；衍生物走 50_AssetPipeline；全库验证与 legacy 回归对照查 local-only 原件；git 仅入库其 .md）；`Raw/` 只增不改；分析与归纳产物落 `Analysis/`。
- `30_ExternalRepos` 上游第三方仓库：除非用户明确要求，不修改、不提交。
- `40_Integration` 架构移植实验室：候选控制器/系统在此做适配原型与架构验证，验证通过才合并主工程。
- `50_AssetPipeline` 资产中转：按 Incoming → Converted → Cleaned → Retargeted → UnityReady 流转，被拒项进 Rejected 并记录原因。
- `60_Experiments` 实验区：想法先在这里原型化，不污染主工程；失败进 `99_Archive`。
- `70_Automation/Agents` 代理工作档：任务、提示词、汇报统一归档，规范见该目录 `AGENTS.md`。
- `99_Archive` 废弃保留：只进不改。

## 硬性红线（继承自 E:\ZZZ\AGENTS.md，全部继续有效）

- **资产检索唯一标准 = `20_ReverseEngineering/ASSET_LOOKUP.md`**：身份检索（source block + CAB + pathID）为权威口径，名字仅是线索、同名不证明同资产；四级查找链（工作区逆向层 → extracted → 补充解包/深度静态逆向 → 运行时注入）顺序不可跳级；宣布"资产不存在"必须满足该文件的裁决口径并留检索轨迹；每一跳的产出必须按回流纪律归位并重生成清单。
- **路径三类法与写护栏（D1，2026-09-09 起）**：A 类（原始资产/历史证据输入）只读冻结原件、禁止写入；B 类（当前工程输入）一律指向工作副本；C 类（一切构建/截图/日志/验证报告输出）必须落 ZCode 侧目录。任何工具写文件前必须检查目标不落在 `E:\ZZZ\local-only\` 内——"只读验证"不豁免（验证脚本也写报告）。审计登记与整改方案见 `00_ProjectHub/AuditExports/path-audit-summary.md`（当前 64 处输出写回待 D1-b 整改归零）。
- **代码入库（用户 2026-09-09 决策）**：不设私有代码仓库；自研代码（主工程 .cs/.asmdef 及 .meta，工作区脚本，实验/集成原型）直接进本仓库。第三方内嵌库（Assets/ThirdParty、Packages/com.*）仍不入库，登记于 `30_ExternalRepos/PROVENANCE.md`。
- **验收双口径**：功能完成判据用成组验收（如"普通特殊技完整链路"：动画→显隐→特效→命中→镜头→停帧→退出一起过）；进度表达用库存计数（接了几项/各是什么/还剩什么，命名清单制）。两者必须同时报告。

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
