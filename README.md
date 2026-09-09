# ZCode 工作区 — Remielle 复刻研发

本工作区是 Remielle（绝区零）角色复刻的研发总目录：Unity 主工程、逆向资产索引、开源仓库、集成实验室、资产管线与知识层统一安置于此。目标是从"Unity 角色 Demo"演进为涵盖**游戏逆向、角色控制器、动画重建、VFX 重建、镜头重建与开源架构融合**的小型研发工程。

> 术语约定：本工作区全部文档中出现的"代理 / coding agent"指任意编码代理（ZCode、DeepSeek Harness 或其他），不特指任何产品。历史文档中的 "Codex/codex" 一律按"任意 coding agent"理解。

## 快速上手（代理进入工作区的读取顺序）

1. `README.md`（本文件）→ `CURRENT_STATUS.md` → `TODO.md`
2. `00_ProjectHub/`（架构、设计、决策、资产索引）
3. 需要查资产 → `00_ProjectHub/Inventory/` + `WORKSPACE_MAP.md`（先看指针表，大量权威库不在本目录内）
4. 需要动代码 → `10_Unity/Remielle_Main`（唯一权威主工程）

## 分区一览

| 目录 | 性质 | 一句话定位 |
|---|---|---|
| `00_ProjectHub/` | 知识层（可写） | 工作区大脑：架构、设计、决策（ADR）、资产索引、缺口清单 |
| `10_Unity/` | 工程层 | `Remielle_Main` 权威主工程（工作副本）+ 沙盒工程 |
| `20_ReverseEngineering/` | 逆向层 | 新采集 Raw（只增不改）、Remielle 分类、分析产物、工具 |
| `30_ExternalRepos/` | 外部层 | 上游第三方仓库（只读，不直接修改） |
| `40_Integration/` | 集成层 | 架构移植实验室：候选控制器/系统先在此验证再进主工程 |
| `50_AssetPipeline/` | 管线层 | 资产中转：Incoming → Converted → Cleaned → Retargeted → UnityReady |
| `60_Experiments/` | 实验层 | 原型验证区；成功升入 Integration，失败进 99_Archive |
| `70_Automation/` | 自动化层 | 代理工作档（Prompts/Tasks/Reports）与工具指针 |
| `80_References/` | 参考层 | 游戏录像、截图、帧捕获、技术笔记的指针与新增内容 |
| `90_Builds/` | 构建层 | Dev/Test/Release 构建与捕获输出 |
| `99_Archive/` | 归档层 | 废弃但保留的工作 |

## 三条铁律

1. **Raw 永不修改**：逆向原始文件、上游仓库、原始 dump 不改写；需要改就复制到 Integration 或 AssetPipeline。原始资产保持本地，不上传。
2. **Main 永远可运行**：`10_Unity/Remielle_Main` 任何一次保存都应处于可编译、可进 Play Mode 的状态；试验代码先进 `60_Experiments` 或沙盒工程。
3. **已知未解决必须文档化**：所有"知道但没修完"的事项进入 `00_ProjectHub/Inventory/Missing_Content.md` 与 `TODO.md`，不允许只留在对话上下文里。

详细规则见 `AGENTS.md`；既有资产的位置与归属见 `WORKSPACE_MAP.md`。
