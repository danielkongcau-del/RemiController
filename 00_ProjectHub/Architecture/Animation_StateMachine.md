# 动画系统（Animation StateMachine）

状态：初始化骨架；求值链细节以主工程实测与 E:\ZZZ\AGENTS.md 约定为准。

## 已知事实

- 动画源为 Vault 全量恢复库（829 源身份/810 名称；664 完整分层 ACL + 9 独立 ACL + 156 未压缩），以最高精度导入，不重采样、不降精度（`RemielleAssetVault/recovered/animations`）。
- `RemielleNativeAnimation` 系列负责求值：恢复源层级默认 TRS、q/-q 统一符号后重算切线、EnsureQuaternionContinuity、有序切换与独立采样一致。
- 14 条 root/motion 标量保留原始包，不重复施加在根 Transform 位移之上。
- 工程内 `Assets/V3/Animations` 的 15 个 .anim 是**渲染/光照验收示例集**，不代表可用动作全集；控制器动作必须回全量库取证（教训已固化）。
- 单动作位移提取已接通（真实模型的位移提取经 Unity 验证）。

## 待补

- [ ] 全量库 → 工程 .anim 的导入映射表（见 `../Inventory/Animation_Index.md`）。
- [ ] walk（走路）与 fly（慢速飞行）两套移动动画的命名与切换点取证。
