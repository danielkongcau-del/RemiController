# 实验：Walk → Fly 移动切换

立项：2026-09-09　状态：**未开始（取证阶段）**

## 目标

复刻 Remielle 的二段移动：按住移动键一段时间后，从走路动画**自动切换**为慢速飞行动画（两套独立 walk 动画）。

## 假设（待推翻或证实）

- 触发条件 = 持续移动时长 T（T 值未知）。
- 切换带混合窗口（时长未知，可能有专属过渡动画）。
- 战斗动作/受击会打断飞行状态。

## 取证清单（开始编码前完成）

- [ ] Vault 全量库中两套移动动画的源身份与名称（走路族 vs 飞行族，候选词：Walk / Move / Fly / Hover / Float / Air）。
- [ ] 原生 97 状态图中对应状态与过渡：条件语义（`local-only` 取证链 `condition-semantics.md`）、过渡窗口、候选队列优先级。
- [ ] T 的实测值：游戏录像逐帧计时（录像落 `../../80_References/GameplayVideos`）。
- [ ] 飞行中的移动模型：是否沿用 OpenKCC 位移，还是有垂直分量/悬浮高度。

## 验证口径

数值（状态迁移计数、动画帧序列）+ 真实 Player 帧审计；对照原版录像逐帧。

## 产出方向

结论 → `../../20_ReverseEngineering/Analysis/Parameter_Research/walk-to-fly.md`；原型 → 本目录 `Prototype/`；通过后并入 `../../40_Integration/Controller/Current.md` 对应状态机。
