# 代理工作规范（Agents）

> 对任意 coding agent 生效；"代理"不特指任何产品。

## 目录

- `Prompts/`：可复用的任务提示词模板。
- `Tasks/`：任务档（一个任务一个文件：目标、边界、验收口径）。
- `Reports/`：汇报归档，命名 `YYYYMMDD-<主题>-<代理标识>.md`。

## 工作纪律（与工作区根 AGENTS.md 配套）

1. 开工：读根目录 `README` → `CURRENT_STATUS` → `TODO` → `00_ProjectHub`；认领任务后在 `Tasks/` 建档。
2. 过程：行为/资产结论必须有证据（取证链、数值、真实 Unity 运行日志），不接受纯静态臆断；所有"知道但没修完"的发现当场登记 `Inventory/Missing_Content.md`。
3. 收工：回写 `CURRENT_STATUS` / `TODO` / 相关索引，汇报落 `Reports/`；涉及架构取舍的补 ADR。
4. 边界：local-only 只读；主工程保持可运行；上游仓库不修改；产物输出路径一律在 ZCode 侧。
