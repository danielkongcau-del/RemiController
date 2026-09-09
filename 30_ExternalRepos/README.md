# 30_ExternalRepos — 外部代码层

## 分区

`Controllers/`（角色控制器候选）、`Animation/`、`Camera/`、`VFX/`、`Rendering/`、`Utilities/`。

## 规则

- **上游仓库只读**：除非用户明确要求，不修改、不向上游提交。
- 引入流程：clone/解压入对应分区 → 在本目录 `PROVENANCE.md` 登记（来源 URL、commit/tag、协议、SHA-256、日期）→ 需要评估的进 `../40_Integration` 对应候选目录 → 评估通过并适配后才允许进入主工程。
- 完整仓库**永远不直接拖进** `10_Unity/Remielle_Main`。

## 已内嵌主工程的第三方（登记备查，原始包在 local-only downloads）

| 依赖 | 版本/commit | 协议 |
|---|---|---|
| OpenKCC | a1a30ed7 | MIT |
| UnityHFSM | f8fdc2e | MIT |
| Cinemachine | 3.1.7 | Unity Companion License |
| StickWeaponTrailEffect | f34f445 | MIT |
| HoyoToon 适配 shader | 5.2.7 嵌入 | 见工程内声明 |

原始下载包（zip/tgz/unitypackage，含未导入的 Animancer Pro、BBB-Nexus）在 `E:\ZZZ\local-only\RemielleControllerDependencies\downloads`（指针，见 `../WORKSPACE_MAP.md`）。
