# TODO

> 规则：开工前认领、收工回写；完成项移入对应阶段文档，不在此堆积。已知未解决的缺口同时维护在 `00_ProjectHub/Inventory/Missing_Content.md`。

## A. 逻辑（控制器行为）

- [x] A1 **walk→fly**：取证→运行时闭环→正式实现全链完成（2026-09-10，IMPLEMENTATION_REPORT）；31/31 审计+回归绿。遗留：GAP-022/023（环境项）
- [ ] A2 普通特殊技**闪光颜色修正**：HDR 诊断已定位"相机未挂最终 LUT + 高亮饱和未校准"，按取证链核对源 Alpha 指数与显示路径。
- [ ] A3 运镜补全：特殊技/强化特殊技运镜、原生退出仰角映射、全翼构图、遮挡处理。

## B. 资产（逆向归纳与接入）

- [x] B1 **逆向资产全量查询归纳（v1）**：机器目录层已生成（`20_ReverseEngineering/Analysis/catalogs/`，829 身份已机器验证）+ SHA-256 全量清单（`20_ReverseEngineering/Manifests/`，云端可复验）。2026-09-09。
- [ ] B1-v2 **按功能域的语义归纳**（不设全量前置，避免整理阻塞开发）：优先 walk→fly 与普通特殊技两域——名称口径归一（827 vs 810）、Animation_Bindings/VFX_Event_Map/Camera_Bindings 三表从骨架补成全量、`Gap_Register`/`Feature_Coverage` 随工作滚动更新。
- [ ] B2 剩余 23 项粒子特效接入（31 项中已完成 8 项）。
- [ ] B3 高亮饱和与完整原生粒子材质对齐（HDR 路径）。

## C. 拓展性（架构演进）

- [ ] C1 评估引入新的开源角色控制器仓库：候选先进 `30_ExternalRepos/Controllers/Candidates`，按 `40_Integration` 流程做架构移植评估（重点考察：walk→fly 状态表达、与现行 Native 状态图/动画链的兼容、迁移成本）。
- [ ] C2 界定现行框架做不到的具体能力清单，作为 C1 的评估标准（输入 C1 的评估文档）。

## D. 工作区维护

- [x] D1-b **输出路径整改（2026-09-09 完成）**：61 处纯输出迁移至 `90_Builds`（OUTPUT→local-only=0）；`RemiellePathPolicy` 三类根+写护栏落地；GAP-013 悬空路径修复、GAP-014 归类 A 类合规。
- [x] D1-c **混合根拆分（2026-09-09 完成，附真机实证）**：21 文件拆分（读根 A 类 + WriteRoot 迁 90_Builds），用法级残留 0；`NativeMotionCodecAudit.Run()` 真机运行 pass=true、受保护目录前后哈希一致、输出落位 90_Builds。
- [ ] D1-e 其余审计工具的抽样真机复跑（每个域至少一个），确认迁移后行为等价；结果登记 `70_Automation/Agents/Reports/`。
- [ ] D2 副本侧门禁基线：在副本跑一轮真实门禁（D3D11、禁 -nographics）后建立 ZCode 侧基线文件（不得直接刷新哈希掩盖差异）。与 D1-e 并列为工作区纪律待办。
- [x] D1-d Unity 首次导入+编译验证**通过**（2026-09-09：退出码 0、零 CS 错误、双程序集全新构建）。
- [ ] D3 `Animation_Index` 首批归纳：工程内 `Assets/V3/Animations` 15 个 .anim 为渲染验收示例集，控制器动作取自全量库——先建立"库内真名 → 工程 .anim"映射表。（并入 B1-v2）
