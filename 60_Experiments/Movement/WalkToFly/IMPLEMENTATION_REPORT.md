# IMPLEMENTATION REPORT —— Remielle 普通移动 Walk→Run/Fly 正式接入

> 证据基线：`FORENSIC_REPORT.md`（静态取证）+ `FINAL_TRIGGER_REPORT.md`（运行时闭环，RUNTIME_RECONSTRUCTED）。
> 本报告记录正式实现、验证与边界。日期：2026-09-10。

## 1. 修改文件清单

| 文件 | 变更 |
|---|---|
| `Assets/ControllerIntegration/SourceWalkToRunPolicy.cs` | **新增**。纯策略类：秒累计、2.0s 阈值、rising-edge 请求闩锁、reset 信号 |
| `Assets/ControllerIntegration/SourceIsMovingDelay.cs` | **新增**。纯策略类：Bool_IsMovingDelay 写入者复刻（跟随 + 0.1s 停止宽限） |
| `Assets/ControllerIntegration/SourceChangeRunType.cs` | **新增**。纯策略类：RunLoop 01/02 随机换型（1s / 25%，STATIC_BYTE） |
| `Assets/ControllerIntegration/RemielleSourceController.cs` | 接线：`TickInput` 内泵三个策略；`Bool_WalkToRun=runHeld` 占位已删除（`runHeld` 参数保留但被忽略，注释标记历史占位）；`ChangeRunType` 在 RunLoop 态（含混合目标）计时 |
| `Assets/ControllerIntegration/SourceActionSession.cs` | 新增 `StateName(int)`（策略与审计用） |
| `Assets/ControllerIntegration/SourceWalkToRunProbe.cs` | **新增**。PlayMode 剧本探针（T1-T7 实机会话链） |
| `Assets/ControllerIntegration/Editor/SourceWalkToRunAudit.cs` | **新增**。批处理审计：策略单测（T8/T1/T2/reset/真值表）+ router 级图语义（T3-T7），31 项检查 |

## 2. 十项汇报

1. **修改文件**：见上表。未触碰 Dash/攻击/特殊技/特效/镜头系统。
2. **Timer 位置**：`SourceWalkToRunPolicy` 独立类，由 `RemielleSourceController.TickInput` 每帧驱动；不在 Update 里堆逻辑，可脱离 Unity 单测。
3. **Threshold**：`ThresholdSeconds=2.0f`，`elapsed += delta` 秒累计（帧率无关，T8 实证 30/60/120fps 均在 2.0+δ 触发）。代码注释标注 `RUNTIME_RECONSTRUCTED`（9 回合 2.026-2.081s）。
4. **Latch 生命周期**：`Tick` 返回 rising-edge 恰一次；参数 `Bool_WalkToRun` 与 `Requested` 同步写 false/true（宿主每帧 SetBool——与原作观察一致：writer 每帧重复调用，119 条中 108 条写 false）。不实现持续组合 Bool；false 侧为 `REPLICA_LIFECYCLE_POLICY`（跟随 reset），不冒充原作惰性 false writer（0.015s~124s，已登记未复刻）。
5. **Reset 策略**：`Bool_IsMovingDelay == false → reset`（≈持续停止 0.1s）。标记 `REPLICA_POLICY_PENDING_RUNTIME_CONFIRMATION`；运行时已证 0.413s 停止重置、<0.413s 边界未证。宽限窗口内（<0.1s 停止）计时暂停不清零（replica 细节，E4 未证）。
6. **原始证据部分**：全部转换复用恢复的原生状态机（walk exit gate、0.2s、桥接、Run_End、Attack_Rush、Evade 入口）；IsMovingDelay 0.1s（ability STATIC_BYTE + 运行时真值表）；ChangeRunType 1s/25%（ability STATIC_BYTE）；2.0s 阈值（RUNTIME_RECONSTRUCTED）。
7. **Replica policy 部分**：reset 时机（IsMovingDelay 联动）、宽限内暂停累计、WTR=false 生命周期。均已在代码注释与本报告标注。
8. **Unity 实测**：
   - 策略单测+router 级审计 `SourceWalkToRunAudit.Run`：**31/31 全绿**（exit 0），含 T8 三帧率、T3 门控三断言、T4a 帧比门（发现并修正取证口径：useFrameCount=174/174，有效门=片尾帧而非 raw exit 0.9227）、T4b 换型桥、T5 宽限退出、T6 闪避入环、T7 Run 态攻击。
   - PlayMode 实机会话（首轮成功捕获）：T1-T5、T2、阈值全过；三时刻实测：request→WalkToRun 转换 0.5s（等 Walk_Loop 循环末帧门）、Walk_To_Run→RunLoop 进入 2.9s（片尾帧门+混合）、RunLoop 内变体切换 1.0s。PlayMode T6 场景脚本对入环边名的假设错误（已改语义断言）；后续重跑遇编辑器 SearchDatabase 启动崩溃（Unity 6.0.3 已知栈，与本功能无关），故 T6/T7 以批处理 router 级为准。
   - 回归：`QualifiedIntegrationAudit.Run` 22/22 绿；`NativeMotionCodecAudit.Run` pass=true。
9. **既有系统**：集成审计回归全绿证明 Walk/Evade/Dash/Attack/Special 决策链未破坏；探针 T5/T6/T7 分别验证停止退出、闪避共存、Run 态攻击。
10. **GAP-005 处置**：主链（PlayMode 实机+router）、退出（双证）、打断（router T7 + 集成回归）、回归（22+31+codec）全部通过 → **关闭**。两个遗留转新条目：GAP-022（PlayMode 探针 T6 重跑受编辑器崩溃阻塞，语义已由 router 级覆盖）、GAP-023（demo Player 重建被相机包哈希护栏拦截——游戏本体已更新，相机包需按新 GameAssembly 重新取证，不绕过护栏）。

## 3. 实现合同履行对照（任务 §四-§八）

- 独立可测试策略类 ✓（三个，纯 C#，无 Unity 依赖）
- 不持续组合 Bool ✓（rising-edge/latch）
- 秒而非 120 帧 ✓（T8 实证帧率无关）
- reset 封装单点 ✓（reset 信号仅一处）
- RunLoop 变体沿原生 Trigger_ChangeRunType ✓（1s/25%，经恢复桥接边消费）
- 停止走 IsMovingDelay ✓（0.1s 宽限 → Run_End，PlayMode+router 双证）
- Evade 独立入口 ✓（router T6：Evade_To_RunLoop_01→RunLoop_01 无 WTR 参与）
- Run→Attack_Rush ✓（router T7）
- 未重做 Dash/攻击/特效/镜头 ✓

## 4. 取证修正（本次审计发现）

`Walk_To_RunLoop_01→RunLoop_01` 与 `Evade_To_RunLoop_01→RunLoop_01` 的无条件边为 **useFrameCount 模式**（174/174、210/210），有效出口=片尾帧（归一化 1.0），raw `exit` 字段（0.9227）不生效。FORENSIC_REPORT §2 的 exit 标注按此更正；审计已按帧比门验证通过。
