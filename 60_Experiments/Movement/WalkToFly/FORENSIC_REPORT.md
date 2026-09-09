# Walk → Run/Fly 自动转换机制 · 完整取证报告

> 任务边界：仅普通 locomotion 的 Walk→Run 自动切换。不含 Dash/Sprint 高速飞行与特殊技 Phase Flow / Flight Stance（三机制关系见 §7）。
> 证据优先级遵循 ASSET_LOOKUP；本报告全部结论出自：原始 controller 序列化数据（ZZZ compiled ControllerConstant）→ controllers-v2-corrected 权威运动绑定 → highest-quality npz 逐帧解码 + 骨架 FK → ability 序列化数据。机器可读版：`walk-to-fly-evidence.json`；全字段过渡表：`Prototype/family-states-raw.json`。

## 1. Final Verdict

| 问题 | 结论 | 置信度 |
|---|---|---|
| 是否存在自动 Walk→Run | **存在**。`Walk_Loop --[Bool_WalkToRun==true 且走完当前循环]--> Walk_To_RunLoop_01 --> RunLoop_01`，另有闪避独立入口 | 高（原始字节） |
| Run 是否就是慢速飞行 | **是悬浮式移动，非跑步**：RunLoop_01/02 的髖部高度方差为**数学零**（0.0，388 骨骼逐帧 FK 实测），双足高位轻摆、从不下探；跑步步态不可能零起伏。髖部高度与 Walk 相同——**贴地滑行式悬浮**（非高空飞行）。`Walk_to_Run_01` 含一次 +0.28m 的垂直起飞机动后回落 | 高（曲线证据）；建议录像逐帧终验 |
| 触发是时间/距离/速度/输入还是组合 | **UNKNOWN（写入者未落网）**。已排除：过渡条件本身无计时/距离/速度（仅 Bool）；阈值逻辑在 `Bool_WalkToRun` 的**原生代码写入者**中，静态数据全穷尽未找到（见 §3）。结构约束：Bool 置位后**必须等 Walk_Loop 循环末帧**才切换——切换粒度 = 38 帧 / 0.6s（walk 循环周期） | 结构=高；条件=待运行时取证 |
| 精确阈值 | **UNKNOWN**。不存在于任何静态数据；连"是阈值类条件"都未被证明（H1/H3/H5 均无证据，H4 是复刻占位） | — |

**一句话裁决**：机制真实存在且形态已完全复原（走路 ⇄ 贴地悬浮滑行，含起飞过渡与闪避捷径），但"什么时候自动切"的判定逻辑在原生代码里，静态证据链到 `Bool_WalkToRun` 的写入者为止断线。

## 2. State Graph（原始 ControllerConstant 提取，全字段见 Prototype）

```
进入 Walk：  Walk_Start ──(exitTime .9167)──> Walk_Loop
            快速松开：Walk_Start ──[FrameCount<12 且 !Bool_IsMoving]──> Walk_Start_End ──> 退出口
进入 Run：  路径A：Walk_Loop ──[Bool_WalkToRun==true；exitTime≈1.0(38/38帧)；0.2s]──> Walk_To_RunLoop_01 ──(exitTime .9227)──> RunLoop_01
            路径B：闪避族 ──> Evade_To_RunLoop_01 ──(exitTime .9227，无条件，不经 Bool_WalkToRun)──> RunLoop_01
Run 内循环： RunLoop_01 <──Trigger_ChangeRunType──> RunLoop_01_To_02/02_To_01 桥 ──> RunLoop_02（每秒25%随机换型）
            桥内出口：FrameCount mode9 166 / 162（authored 帧）
退出：      RunLoop_*/桥 ──[Bool_IsMovingDelay==false；0.1s]──> Run_End ──[FrameCount mode9 20 且 IsMoving=重启窗 | 无条件收尾]──> 退出口
            Walk_Loop ──[Bool_IsMoving==false]──> Walk_End
回 Walk：   Run 态 ──[Trigger_EtherEyes；0.2s]──> Walk_Loop（特殊机制，非普通退出）
```
所有过渡：fixedDuration=1、interruptionSource=2、atomic=1；优先级=列表序（Hit(0.0s) > Evade(0.1s) > ExQTE(0.0s) > AttackB/HoldB(0.1s) > AttackA→Attack_Rush(0.1s) > Switch_Out > 语义过渡）。

## 3. Parameter Writers

| 参数 | 写入者 | 状态 |
|---|---|---|
| `Bool_WalkToRun` (id 218337101) | **未找到（原生代码）**。全控制器唯一消费者=上述路径A过渡；Dash/Evade **不复用**它 | 穷尽清单：51 个 ability（两侧集合一致）→ 无；角色脚本 .dat（ASCII 名 + u32 参数哈希双扫描）→ 零命中；39 个 DummyDll 反编译程序集 → 'WalkToRun' 零命中；behavior-tree/battle-assets 二进制 → 零；condition-semantics/state-contract/RuntimeRepair → 仅消费者侧记载 |
| `Bool_IsMovingDelay` | **已破译**（`RemielleOrigin_IsMovingDelay` ability）：0.02s ThinkInterval；停止移动有 **0.1s 宽限**（DelayTimer 修饰器）——这就是"瞬时归零一帧不掉出 Run"的设计证据 | 完整 |
| `Trigger_ChangeRunType` | **已破译**（`RemielleOrigin_ChangeRunType` ability）：挂在 RunLoop_01/02 状态，每 1s 以 **25% 概率**触发换型（specials MaxTime=8/MinTime=5）→ **RunLoop_01/02 是同一悬浮移动的两个随机表现变体，非速度分级** | 完整 |
| `FrameCount` | 游戏泵入的状态内帧计数器；时钟 = **authored 60fps**（全部相关 clip `m_SampleRate=60.0` 实测） | 语义已证，泵源原生 |
| 现行复刻 | `RemielleSourceController.TickInput(runHeld)` 固定 false——占位，非原作逻辑（与本取证一致地被标记） | — |

## 4. Animation Identity（v2-corrected 权威绑定；完整 CAB 见 evidence JSON）

| 状态 | 剪辑（Origin_Ani_*） | pathID | 帧/时长/循环 |
|---|---|---|---|
| Walk_Loop | …Ani_Walk_Loop | -2893577299971434832 | 38f / 0.60s / loop |
| Walk_Start | …Ani_Walk_Start | -1678135712426977003 | 88f / 1.43s |
| Walk_To_RunLoop_01 | …Ani_Walk_to_Run_01 | 8206013166952976172 | 176f / 2.90s |
| RunLoop_01 | …Ani_Run_Loop_01 | 7376968608536389763 | 212f / 3.50s / loop |
| RunLoop_02 | …Ani_Run_Loop_02 | -3374308206746047625 | 212f / 3.50s / loop |
| RunLoop_01_To_02 | …Ani_Run_Transform_01 | -3494025788189807306 | 174f / 2.87s |
| Run_End | …Ani_Run_End | -4712315809483842211 | 362f / 6.00s |
| Evade_To_RunLoop_01 | …Ani_Evade_to_Run_01 | 9167602165747736858 | 212f / 3.50s |

每态另有 `Origin_Default_Ani_*` 第二变体（同 CAB）。注意：**v1 controller JSON 的状态→运动绑定已知错位**（Walk_Loop 误绑 Run_Transform 等），一切身份以 v2 为准——本次已实测验证。

## 5. Timing / Distance Evidence

- **时钟**：authored 60fps（`m_SampleRate=60.0`）。FrameCount 各阈值（12/20/50/60/70/162/166）均为 authored 帧；60fps 下 N 帧 = N/60 秒，此换算**已获证明**（非假设）。
- **切换粒度**：Walk→Run 过渡 exitTime≈1.0 —— 即使 Bool 中途置位，也只会在 Walk_Loop 末帧（每 0.6s 一次机会）触发。
- **桥接出口**：166f≈2.77s / 162f≈2.70s（与桥动画 174f×exitTime .9651≈168f 自洽）。
- **距离/速度**：clips 无 root motion（stopX/startX=0）——移动速度完全由游戏运动系统驱动，剪辑不含速度证据。

## 6. Interrupt / Exit Rules（出自过渡表）

| 情形 | 规则 |
|---|---|
| 停止移动 | Run 族：`Bool_IsMovingDelay==false`（含 **0.1s 宽限**）→ Run_End；Walk：`Bool_IsMoving==false` → Walk_End |
| 受击 | 全族 `Trigger_Hit` 0.0s 立即退出（hitstop 内计时是否继续=UNKNOWN，写入者原生） |
| 闪避 | `Trigger_PressEvade` → Evade_Front_02；**闪避后经 Evade_To_RunLoop_01 直接落回悬浮**（Run 的第二入口）；Walk_To_RunLoop_01 前 6 帧内按闪避走特例 |
| 普攻 | `Trigger_PressAttackA` → Attack_Rush（Run 态可直接出攻击，无需先退出） |
| 特殊技/强化 | AttackB/HoldB → Exit（父选择器路由至 Sub_Attack；Sub_Attack 内部不读 WalkToRun） |
| 换人 | Trigger_Switch_Out → Exit |
| 瞬时归零一帧 | IsMovingDelay 0.1s 宽限 = 设计上不清零（Run 侧）；Walk 侧直接掉 Walk_End |
| 改变方向 | 过渡条件无方向参数（Float_JoyStickDir 存在但不门控本族）→ 状态层面**不重置**；写入者层面 UNKNOWN |

## 7. 三种"飞行"的区分

- **Locomotion Run（本报告）**：Sub_Move 内 Walk⇄RunLoop 贴地悬浮滑行；唯一入口参数 `Bool_WalkToRun`。
- **Dash/Sprint flight**：Dash_Start/Loop/Evade/End 独立链（`Trigger_PressEvade` 高速段）；与 Run 的唯一交点 = Evade_To_RunLoop_01（闪避收尾直接汇入悬浮）。
- **Phase Flow / Flight Stance**：AirCombat 控制器（Bool_BoostFlying/Bool_ReverseFlying/AirCombat_* 状态族）+ 特殊技系统；与 `Bool_WalkToRun` **零耦合**（全控制器唯一消费者已证，AirCombat 控制器中的 WalkToRun 出现属同名参数不同机器，且本报告主控制器内无交叉过渡）。

## 8. Implementation Contract（后续实现必须满足；本任务未实现任何代码）

已证明、可直接实现的部分：
1. `Bool_WalkToRun==true` 且 Walk_Loop 到达循环末帧 → 0.2s 固定时长过渡进 Walk_To_RunLoop_01，其 .9227 处无条件进 RunLoop_01。
2. RunLoop_01/02 同为悬浮循环；进入 RunLoop 态后每 1s 以 25% 概率 SetTrigger(ChangeRunType) 换型，经 0.2s/0.1s 桥接。
3. Run 退出用 **Bool_IsMovingDelay**（0.1s 宽容）而非裸 IsMoving；Run_End 内 20 帧重启窗。
4. RunLoop 态可直接 Trigger_PressAttackA 进 Attack_Rush；受击 0.0s 抢断；闪避经 Evade_To_RunLoop_01 汇回。
5. FrameCount 阈值按 60fps authored 帧实现（12/20/162/166）。

**未证明、禁止凭猜测实现的部分**：`Bool_WalkToRun` 的置位条件与阈值（H1-H5 均未证实）。在运行时取证落网前，任何"持续移动 T 秒后置 true"的实现都必须在 UI/代码中显式标注为**假设参数**，不得声称为原作机制（现行 TickInput 的固定 false 同样是占位）。

## 9. Unknowns（如实清单）

1. `Bool_WalkToRun` 写入者身份与精确判定逻辑（原生代码；建议按 ASSET_LOOKUP 第④站做运行时参数追踪：游戏内持续直线移动，逐帧记录该参数翻转点）。
2. hitstop / 受击期间自动切换计时是否暂停。
3. 方向改变是否影响写入者侧计时。
4. Walk_End（5.83s）/Run_End（6.00s）超长时长的用途细节（疑似含多段收尾，未逐段考证）。
5. 姿态判定的录像终验（静态曲线证据已足够强，但任务要求的"画面确认"建议补一段原作录像逐帧对照）。
