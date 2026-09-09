# D1 路径审计摘要与路径政策（2026-09-09）

> **D1-c 已完成（2026-09-09）**：21 个混合根文件拆分（读根 A 类保留 + `WriteRoot` 写根迁 `90_Builds`）——43 处写调用点 + 7 处派生输出根 + 4 处中间变量/跨类派生 + 1 处跨类误伤修复；**用法级检查归零**（34 个 local-only 常量全部为读根，写上下文真实残留 0，排除跨类限定名读引用）。
> **真机实证（审查要求的完成标准）**：`NativeMotionCodecAudit.Run()` 经 Unity batch 真实执行——退出码 0、审计 pass=true（含 transformFloat32 逐位验证）；**受保护目录（local-only motion-sampling，25 文件）运行前后 SHA-256 完全一致**；输出精确落位 `90_Builds\ControllerImplementation\20260906\motion-sampling\`。日志：`90_Builds/codec-audit-run.log`。
> `RemiellePathPolicy` v2：登记全部只读根（冻结原件 + 五组资产副本 + Manifests），`GuardWrite`/`GuardProjectEdit` 分离 C 类输出与 B 类工程编辑。

数据源：`path-audit.csv`（`audit_paths.py` 行级 + **用法级**双检查；另含 StreamingAssets 3 个 JSON 实例，已归类 A 类输入）。

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
7. [x] Unity 首次导入+编译验证**通过**（2026-09-09：`-batchmode -quit` 退出码 0，`Assembly-CSharp`/`Assembly-CSharp-Editor` 全新构建，日志零 `error CS`；61 处迁移与 `RemiellePathPolicy` 均编译成功。日志：`90_Builds/unity-import-compile-check.log`）。
