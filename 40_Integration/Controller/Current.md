# 现行控制器档案（Current Controller）

状态：唯一被认可的 controller 实现（2026-09-09 口径）。

## 位置

`10_Unity/Remielle_Main/Assets/`：
- `ControllerRuntime/`：34 个 Native* 脚本（原生状态图/过渡/混合/姿态/时钟/显隐/编解码复刻层）+ `Data/` 源包 JSON。
- `ControllerIntegration/`：约 40 个接入脚本（输入缓冲、特殊技能量、命中链、相机、武器、特效、训练靶）。

行为规格取证链（只读）：`E:\ZZZ\local-only` 的 RemielleControllerPreparation / -Implementation / -Integration 三目录（oracle/vectors/evidence/unity-verification）与 `RemielleControllerDependencies\implementation\`（分阶段实现文档与验证 JSON）。

## 能力（已验证）

移动（走路/闪避/冲刺链）、四段普攻、特殊技/强化特殊技（能量规则）、输入缓冲、命中检测/停帧/震动/粒子、冲刺镜头、武器拖尾发光、8/31 粒子特效。

## 已知做不到 / 未表达（2026-09-09 依代码证据具体化，原"待补全清单"已回答）

- **接入层 `SourceActionSession` 明确拒绝的配置**（抛 `NotSupportedException`）：状态含子节点（`childIndices.Count != 0`）、`node.duration != 1`、`cycleOffset != 0`、`mirror == true`、`state.speed < 0`。即：**现行播放接入层支持状态间过渡混合，但不是通用的"状态内多子节点 Blend Tree + 多层动画"实现**；主要工作在第 0 层、第 0 个状态机。
- **walk→fly 参数链线索**：正常输入路径 `Bool_WalkToRun` 恒为 false（见 WalkToFly 实验卡）——可能是参数更新缺失而非架构缺失。
- 评估新仓库/适配层的判断标准（云端审查建议）：walk→fly 先按"单叶状态切换+参数缺失"排查；方向移动混合需 Blend Tree 求值适配；上下半身分层需多层输出/遮罩/生命周期；镜像/周期偏移/反向需补齐当前明确拒绝的适配。**候选扩展方向≠必须实现**；仅当目标功能确需且适配成本不合理时才引入外部实现。

## 修改纪律

- 行为错误回归取证链解读原版后再修，禁止无取证补丁。
- 动画身份 = source block + CAB + pathID；不按名字混用版本。
