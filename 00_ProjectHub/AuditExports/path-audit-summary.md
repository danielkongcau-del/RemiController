# D1 路径审计摘要与路径政策（2026-09-09）

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

## 整改方案（D1-b，未执行——需在 Unity 中逐一验证，不批量盲改）

1. 新建统一路径配置类（如 `RemiellePathPolicy`）：集中声明 A/B/C 三类根路径 + `GuardWrite(path)` 护栏。
2. 64 处输出常量改为引用配置的 C 类根（落 `ZCode\90_Builds\<功能域>\` 或 `70_Automation\Agents\Reports\`），每改一个脚本必须跑一次对应审计确认输出落位正确（D3D11、禁 -nographics）。
3. StreamingAssets 三个 JSON 的原件引用：运行时如必需读原件（A 类），挂到配置类并注明；否则改指副本。
4. `AnimCollectionSceneAudit.cs`：悬空输出路径，随 D1-b 一并处置（删除或改指，需用户裁决——它是 zcode 署名残留）。
5. 完成后重跑 `audit_paths.py`，目标：**指向 local-only 的 OUTPUT 类 = 0**；以此作为 D1 完成标准。
