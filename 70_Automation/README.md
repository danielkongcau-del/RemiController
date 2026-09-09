# 70_Automation — 自动化层

## Agents/（代理工作档）

任意 coding agent（ZCode、DeepSeek Harness 等，不特指某一产品）的工作规范、提示词、任务与汇报归档。进入规范见 `Agents/AGENTS.md`。

## 工具指针分区

以下目录登记既有工具的位置与用途（既有工具本体保持原地，见 `../WORKSPACE_MAP.md`）：

- `AssetScripts/`：资产处理/恢复脚本（Vault 恢复链、控制器向量生成器等，多在 local-only 各阶段目录）。
- `UnityEditorTools/`：Unity 批处理/审计/构建工具（主工程 Assets/Editor 系列；注意副本侧绝对路径审计 TODO-D1）。
- `Importers/`：导入器与格式转换（UnityGLTF 配置、粒子导入链等）。
- `AnalysisScripts/`：帧捕获/对照分析（`E:\ZZZ\tools\UnifiedCapture`、`E:\ZZZ\FrameTools`、各 Evidence 目录工具）。

新写的通用工具直接落对应分区；一次性脚本随所属阶段目录走。
