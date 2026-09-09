# 实验：Walk → Fly 移动切换

立项：2026-09-09　状态：**未开始（取证阶段）**

## 目标

复刻 Remielle 的二段移动：按住移动键一段时间后，从走路动画**自动切换**为慢速飞行动画（两套独立 walk 动画）。

## 代码线索（2026-09-09 云端审查从已入库代码中发现，优先级高于泛搜）

- `RemielleSourceController.Update()` 正常键盘路径调用 `TickInput()` 时 **`runHeld` 固定传 `false`**，而 `TickInput()` 内 `p.SetBool(p.Hash("Bool_WalkToRun"), runHeld)` ——该参数在正常输入路径被持续写 false。
- `SourcePreviewMotions` 已预热状态：`Walk_Start / Walk_Loop / Walk_End / Walk_To_RunLoop_01 / RunLoop_01 / RunLoop_02 / RunLoop_01_To_02 / RunLoop_02_To_01` ——移动状态族远不止一条 Walk。
- **取证优先级**：核对 `Bool_WalkToRun` 被哪些过渡条件读取 → `Walk_To_RunLoop_01` 实际进入什么姿态 → `RunLoop_01/02` 的真实移动表现（**名字叫 Run 不排除实为悬浮移动**）→ 原作在什么条件下更新该参数。数据源：`condition-semantics.md`、原生 97 状态图取证链、游戏录像逐帧。

## 定位协议（六层定位法，2026-09-09 并入——来自云端审查建议）

"只能走不能飞"可能断在任何一层，取证按序检查，命中哪层修哪层：

```
1. 正确的飞行动画是否已找到？（库内线索：V3 示例集已见 Idle_AirState_Front/Back_Loop）
2. 它属于哪个角色/场景/控制器变体？（Origin/PasSeul/Ramiel × MainCity/NPC/UI/Timeline）
3. 原状态图（97 状态）里是否存在对应状态或混合分支？
4. 过渡条件是否已解析？（condition-semantics.md）
5. 条件参数在当前工程中是否被正确更新？
6. 进入后是否被其他逻辑立即打断或覆盖？
```

**先区分目标形态**：贴地移动中的悬浮表现（动画+模型高度+过渡规则）vs 真正改变碰撞体运动方式的三维飞行（OpenKCC 空中运动逻辑）。取证前不预设。

## 假设（待推翻或证实）

- 触发条件 = 持续移动时长 T（T 值未知）。
- 切换带混合窗口（时长未知，可能有专属过渡动画）。
- 战斗动作/受击会打断飞行状态。
- **禁止**把猜测的定时器实现直接写成"原作机制"——原型期参数必须标记为假设值。

## 取证清单（开始编码前完成）

- [ ] Vault 全量库中两套移动动画的源身份与名称（走路族 vs 飞行族，候选词：Walk / Move / Fly / Hover / Float / Air）。
- [ ] 原生 97 状态图中对应状态与过渡：条件语义（`local-only` 取证链 `condition-semantics.md`）、过渡窗口、候选队列优先级。
- [ ] T 的实测值：游戏录像逐帧计时（录像落 `../../80_References/GameplayVideos`）。
- [ ] 飞行中的移动模型：是否沿用 OpenKCC 位移，还是有垂直分量/悬浮高度。

## 验证口径

数值（状态迁移计数、动画帧序列）+ 真实 Player 帧审计；对照原版录像逐帧。

**首个功能验收场景集（最低集）**：持续移动触发、短按后释放、移动中转向、攻击打断、闪避打断、退出动作后恢复原状态。六场景全过才算功能闭环。

## 产出方向

结论 → `../../20_ReverseEngineering/Analysis/Parameter_Research/walk-to-fly.md`；原型 → 本目录 `Prototype/`；通过后并入 `../../40_Integration/Controller/Current.md` 对应状态机。
