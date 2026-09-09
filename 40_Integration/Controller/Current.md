# 现行控制器档案（Current Controller）

状态：唯一被认可的 controller 实现（2026-09-09 口径）。

## 位置

`10_Unity/Remielle_Main/Assets/`：
- `ControllerRuntime/`：34 个 Native* 脚本（原生状态图/过渡/混合/姿态/时钟/显隐/编解码复刻层）+ `Data/` 源包 JSON。
- `ControllerIntegration/`：约 40 个接入脚本（输入缓冲、特殊技能量、命中链、相机、武器、特效、训练靶）。

行为规格取证链（只读）：`E:\ZZZ\local-only` 的 RemielleControllerPreparation / -Implementation / -Integration 三目录（oracle/vectors/evidence/unity-verification）与 `RemielleControllerDependencies\implementation\`（分阶段实现文档与验证 JSON）。

## 能力（已验证）

移动（走路/闪避/冲刺链）、四段普攻、特殊技/强化特殊技（能量规则）、输入缓冲、命中检测/停帧/震动/粒子、冲刺镜头、武器拖尾发光、8/31 粒子特效。

## 已知做不到 / 未表达（候选仓库评估的标准输入，TODO-C2 待补全）

- walk→fly 二段移动（按住一段时间走路自动转慢速飞行）——现行状态机内未表达，待取证后先按 `60_Experiments/Movement/WalkToFly` 原型验证。
- （待逐项补全：空战、支援显隐自动化、其他）

## 修改纪律

- 行为错误回归取证链解读原版后再修，禁止无取证补丁。
- 动画身份 = source block + CAB + pathID；不按名字混用版本。
