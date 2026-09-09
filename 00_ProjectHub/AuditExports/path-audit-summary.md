# D1 路径审计摘要与路径政策（2026-09-09）

> **D1-b 已执行（2026-09-09）**：61 处纯输出（含内联写入与回退默认值）完成值级迁移至 `ZCode\90_Builds\*`（16+ 输出目录树已预建）；`Project` 常量改指工作副本（B 类）；**OUTPUT→local-only = 0 达成**（复跑 `audit_paths.py` 核验：OUTPUT 60 处全部指向 90_Builds）。
> **D1-c 残留（混合根 23 处）**：`Folder/Root` 类常量同根既读旧证据又写新验证（codec 审计 ×2 + RenderingReview ui-live/UI 审计族 ×21），值级迁移会断读——需代码级拆分（读根留 A 类、写根走 `RemiellePathPolicy.BuildOutputFor`），逐个在 Unity 真机验证后关闭。清单见 `path-audit.csv` 的 MIXED 类行。
> 新增工具：`Assets/Editor/RemiellePathPolicy.cs`（三类根 + `GuardWrite` 写护栏 + `BuildOutputFor`）。

数据源：`path-audit.csv`（`70_Automation/AssetScripts/audit_paths.py` 扫描主工程副本全部 .cs；另含 StreamingAssets 3 个 JSON 实例）。

## 审计结论

- 主工程内 **100 处**硬编码绝对路径，99 处指向 local-only（冻结原件侧）；**64 处疑似输出**（写入验证/构建产物），6 处疑似输入，30 处待人工归类（多为常量声明）。
- 输出目标分布：`RemielleControllerImplementation\20260906`（29，控制器门禁产物）、`RemielleRenderingReview\20260905`（33，渲染审计）、`RemielleControllerDependencies\implementation`（14，控制器阶段报告/Player 构建）、`RemielleModelReadiness`（8）、`RemielleRuntimeRepair`（5）、`RemielleDataAcquisition`（5）。
- **悬空路径 1 处**：`Editor/AnimCollectionSceneAudit.cs:19` 输出到已删除的 `local-only\RemielleZcode\takeover-smoke\`——该脚本（zcode 时期遗留署名）当前运行必抛 DirectoryNotFoundException，属待裁决残留代码。
- StreamingAssets 3 个 JSON（`binding-profiles.json`、`controller-avatar-bindings.json`、`index.json`）内含指向 local-only 原件的绝对路径。

## 三类路径政策（硬性，写入 AGENTS.md）

| 类别 | 规则 | 典型 |
|---|---|---|
| A 原始资产/历史证据输入 | 允许**读取**冻结原件，**禁止写入** | 取证链 JSON、Vault 数据 |
| B 当前工程输入 | 一律指向工作副本（`E:\ZZZ\ZCode\...`） | 场景、prefab、动作库 |
| C 输出（构建/截图/日志/验证报告） | 必须落 `ZCode` 侧目录（`90_Builds`、`70_Automation/Agents/Reports` 或对应功能域目录） | 一切 `Write/Copy/Create/Report` |

**写前护栏**：任何工具在写文件前检查目标绝对路径——命中 `E:\ZZZ\local-only\` 或冻结工程根即拒绝执行并报错。注意"只读验证"不豁免：验证脚本也会写报告，其输出路径同样受 C 类约束。

## 整改方案（D1-b 已执行 + D1-c 残留）

1. [x] 统一路径配置类 `RemiellePathPolicy`（A/B/C 三类根 + GuardWrite + BuildOutputFor）。
2. [x] 61 处纯输出值级迁移（只改字符串字面量值，零语法变更；输出目录树已预建）。
3. [x] StreamingAssets 三个 JSON 的原件引用**归类为 A 类输入**（运行时读取冻结原件，合规；依赖已登记，副本不离开本机运行不受影响）。
4. [x] `AnimCollectionSceneAudit` 悬空路径（GAP-013）：输出改指 `90_Builds\ControllerIntegration\`，脚本恢复可用。
5. [ ] **D1-c**：23 处混合根拆分（读根 A 类保留 + 写根迁 BuildOutputFor），逐个 Unity 真机验证；完成标准 = MIXED→local-only 归零。
6. [x] 复跑 `audit_paths.py`：OUTPUT→local-only = 0（完成标准达成）。
7. [ ] Unity 首次导入 + 编译验证（后台执行中，结果见 `90_Builds/unity-import-compile-check.log`）。
