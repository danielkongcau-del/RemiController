# Remielle 移动设计（Movement Design）

## 需求来源（用户，2026-09-09）

Remielle 有**两套 walk 动画**：一套是走路，一套是慢速飞行。**按住移动键一段时间后，会从走路自动切换为飞行**。当前工程只有走路移动，缺这一切换。

## 假设状态图（待取证修正）

```
Idle
  └─ move ─→ Walk（走路）
                ├─ 按住时长 > T（T 待取证）──→ Hover/Fly（慢速飞行）
                ├─ 松开 ───────────────────→ Idle
                └─ 战斗/受击打断？（待取证）
```

## 待取证问题（实现前必须回答）

1. 切换触发条件：仅按住时长？还是时长 + 速度/位移距离 + 战斗状态 + 上一动作 + 资源？
2. 精确阈值 T 与过渡方式（瞬切 / 混合窗口时长 / 是否有专属过渡动画）。
3. 两套动画在 Vault 全量库中的准确源身份与名称（走路 vs 慢速飞行）。
4. 飞行中的转向/停止/打断规则；飞行→停止是否有专属回落动画。
5. 冲刺/闪避与飞行的优先级关系。

## 取证数据源

- `RemielleAssetVault/recovered/animations`（全量动画命名与内容）
- `local-only` 控制器取证链（原生 97 状态图、条件语义 `condition-semantics.md`、过渡窗口/时序证据）
- 游戏内实测录像（可落 `80_References/GameplayVideos`）

## 实现路径

`60_Experiments/Movement/WalkToFly`（原型）→ `40_Integration/Controller/Current`（并入现行状态机）→ 主工程。禁止直接在主工程试错。
