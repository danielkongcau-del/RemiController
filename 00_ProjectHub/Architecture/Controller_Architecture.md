# 控制器架构（Controller Architecture）

状态：初始化骨架（2026-09-09，依据冻结原件阶段文档整理）；完整组件图待从 `10_Unity/Remielle_Main` 实测补全。

## 现行构成

- **原生复刻层 `Assets/ControllerRuntime/`**（34 个 `Native*.cs`）：从游戏原生控制器逐环取证复刻的运行时——状态图/状态绑定、过渡搜索与门控、混合树、姿态数学与混合、时钟、条件、可见性栈、动作编解码。配套源数据在 `Data/source-controller-pack.json` 等三个 JSON。
- **接入层 `Assets/ControllerIntegration/`**（约 40 个脚本）：输入（缓冲、特殊技能量规则）、相机（布局、震动、高度锚）、命中（查询、停帧、反馈、粒子）、武器（拖尾、发光、表面）、特效（技能粒子、闪光、爆发）、训练靶等。
- **状态规模**：主控制器 97 个状态、469 条来源事件已接到实际动作实例；27 个模型 renderer 按来源身份绑定显隐。

## 依赖（均已内嵌主工程）

| 依赖 | 版本/commit | 位置 |
|---|---|---|
| OpenKCC（位移/碰撞） | a1a30ed7 | `Packages/com.nickmaltbie.*`（5 个内嵌包） |
| UnityHFSM | f8fdc2e | `Assets/ThirdParty/UnityHFSM` |
| Cinemachine | 3.1.7 | `Packages/com.unity.cinemachine` |
| 带状武器拖尾组件 | f34f445 | `Assets/ThirdParty/StickWeaponTrailEffect` |

## 行为规格取证链（只读）

原生状态条件语义、过渡规则、混合时序等规格以 `local-only` 控制器取证链（Preparation/Implementation/Integration 三目录的 oracle/vectors/evidence/unity-verification）为准。行为出错时回归取证链解读原版后再修，禁止无取证补丁。

## 已知边界

- 移动缺 walk→fly 表达（见 `../Design/Remielle_Movement.md`）。
- 部分扩展能力在现框架下暂不可实现，候选新仓库评估见 `40_Integration/Controller/`。
