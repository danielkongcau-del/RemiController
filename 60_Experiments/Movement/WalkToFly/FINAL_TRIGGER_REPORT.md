# FINAL TRIGGER REPORT —— Bool_WalkToRun Writer 与精确触发条件（RUNTIME_RECONSTRUCTED）

> 上一阶段：`FORENSIC_REPORT.md`（静态状态机+动画+姿态）。本阶段唯一目标：写入者与触发条件。
> 结论等级：**RUNTIME_RECONSTRUCTED**（终止条件 B）——写入函数的"名字"因米哈游元数据加密不可静态映射，但条件模型与阈值由 10 场存量运行时日志闭环（9 回合阈值统计）+ 单调用点原生证据 + 静态状态机交叉验证。
> 机器证据：`walk-to-run-runtime-evidence.json`；阈值回合：`Prototype/threshold-rounds.json`。

## 0. 数据来源（未启动任何游戏进程）

前人（2026-08-28~31）已部署 **AnimatorWriteObserver**（XXMI 注入、原生层拦截 SetBoolID/SetTriggerID 等全部参数写入，含 qpc 时间戳、animator 指针、**GameAssembly 调用点 RVA**）。`extracted/runtime-animator-writes` + `runtime-animator-consumers` 共 13 份、约 2.8GB 日志躺在盘上——本阶段全部结论由此提取，**零新增运行时操作**。

## 1. 十问十答

| # | 问题 | 答案 | 等级 |
|---|---|---|---|
| 1 | Writer 是谁 | **单一托管调用点** `GameAssembly+0x1761247a`（119/119 条写入全部来自它）。函数名 UNKNOWN：global-metadata.dat 为 MHY 私有加密格式（魔数 `MHY\0`）阻断 il2cpp 方法映射；DummyDll 无方法表；调用点邻域为 icall/thunk 链 | NATIVE_XREF + RUNTIME_OBSERVED |
| 2 | 判定公式 | `Bool_WalkToRun = IsMoving && timeSinceMoveStart >= 2.0s`（authored 60Hz 域，等效 ≥120 authored 帧），逐帧求值 | RUNTIME_OBSERVED |
| 3 | 阈值 | **2.0 秒**（观测 Δt=2.026~2.081s，均值 2.052，stdev 0.018s；恒 >2.02 的 26~81ms = IsMoving 写入滞后 2-5 帧 + 帧边界时机） | RUNTIME_OBSERVED |
| 4 | 单位 | 秒（authored 60Hz 游戏时钟）；等价 120 authored 帧 | RUNTIME_OBSERVED + STATIC（m_SampleRate=60.0） |
| 5 | 时间/距离/速度/组合 | **纯时间**。无距离成分（clips 无 root motion；移动由游戏运动系统驱动）；无速度成分证据 | RUNTIME_OBSERVED |
| 6 | 什么 reset | 停止移动即重置计时（观测：0.413s 停止后重新计满 2.0s）。**<0.413s 的短间隙未观测到**（UNKNOWN；保守实现可取"IsMovingDelay 落下即重置"≈0.1s） | RUNTIME_OBSERVED（单案例） |
| 7 | 方向变化是否 reset | **无证据显示与方向相关**（阈值统计中 9 回合一致，与转向行为无关的成分未单独隔离）——倾向不重置，E3 未正式执行 | UNKNOWN（倾向不重置） |
| 8 | hit/attack/evade 后计时器 | WTR 参数本身惰性落回（下降沿距 IsMoving=false 0.015s~124s 不等，事件驱动）；受击/攻击/闪避对**计时器存续**的影响未隔离观测。静态已证：Run 态可直接 Attack_Rush；闪避经 Evade_To_RunLoop_01 **绕过** WTR 直入悬浮 | RUNTIME_OBSERVED + STATIC_BYTE |
| 9 | 与 Bool_IsMovingDelay 的关系 | **两个独立机制**。IsMovingDelay=IsMoving 的 0.1s 滞后版（上升 2-4 帧、下降典型 0.098-0.115s=DelayTimer 0.1s），只管 RunLoop→Run_End 的退出宽限；不维持 WTR 计时器（0.413s 案例证明） | RUNTIME_OBSERVED × STATIC 交叉 |
| 10 | 能否按"原作机制"实现 | **能**：2.0s 公式 + reset + transition 门控 + RunLoop 变体/退出宽限全部有据（见 §4 合同）。唯函数名与帧率无关性（E2）标注为重构而非复原 | RUNTIME_RECONSTRUCTED |

## 2. 关键证据链

**2.1 单调用点**：119 条 SetBoolID(0x0d038f4d) 全部 returnAddress=GameAssembly+0x1761247a；跨 10 场次、5+ 个 animator 实例一致 → 写入者唯一。

**2.2 阈值（9 回合，4 场次）**：Δt(IsMoving↑→WTR↑) = 2.026/2.029/2.041/2.051/2.054/2.060/2.062/2.068/2.081s。
- 无 0.6s 循环量化（Walk_Loop 周期）→ 参数写入不受循环相位门控；
- 转换瞬间观测到状态内 FrameCount 重置（34,35→0,1,2）= Walk_Loop→Walk_To_RunLoop_01 切换在**循环末帧**发生（transition exitTime≈1.0 的静态证据在运行时复现）。

**2.3 重置案例（9592 场）**：移动 0.78s → 停 0.413s → 再移动 → WTR 在新段起点后 **2.081s** 置位（而非首段的 3.269s-0.413s=2.86s）→ 计时器随停止重置。

**2.4 IsMovingDelay 真值表（47144 场 463 条写入 × ability 原文）**：

| 状态 | IsMoving | IsMovingDelay（观测） |
|---|---|---|
| 稳定移动 | 1 | 1（滞后 0.035-0.069s） |
| 刚停止 <0.1s | 0 | **1（宽限保持）** |
| 停止 >0.1s | 0 | 0（典型 0.098-0.115s 落下 = DelayTimer 0.1s） |
| 重新移动 | 1 | 1（快速跟随） |

→ `RunLoop→Run_End`（Bool_IsMovingDelay IfNot）在持续停止约 0.1s 后成立。上阶段对 ability JSON 分支极性的自然语言读法有歧义，本表以运行时数据裁决。

**2.5 相位口径（承接上阶段 §6）**：Walk_Loop = 38 采样 × 60Hz，`AnimationClip.length`(m_StopTime)=0.600s，循环末帧=帧 37（exitTime 0.99999988）；WTR 写入时刻与该相位无相关。FrameCount=状态内帧计数（切换重置），GFrame(0xa13a0703)=全局帧计数。

## 3. 跨角色（generic-walk-run-comparison.csv 摘要）

- Remielle 族：Origin/PasSeul/Ramiel 主控制器 + AirCombat 变体均含 `Bool_WalkToRun`（同 hash 218337101）。
- 其他角色：通用体系走 `AnimStatic` 的 `IntMoveType/BoolIsWalking/FastRun/MoveSpeedRatio`（DummyDll 字段证据），**无 WalkToRun**——Remielle 是特化实现。
- 未提取的其他 Agent controller：UNKNOWN（索引命名不同，需另行解包）。

## 4. 最终 Implementation Contract（更新并取代 FORENSIC_REPORT §8 的未证明项）

```
每帧（authored 60Hz 语义）：
  movingTime = IsMoving ? movingTime + dt : 0        // 停止即重置（≥0.413s 已证；<0.1s 未证，建议随 IsMovingDelay 落下重置）
  Bool_WalkToRun = IsMoving && movingTime >= 2.0     // [RUNTIME_OBSERVED, 9 回合]
转换侧（静态已证）：
  Walk_Loop --[Bool_WalkToRun && 循环末帧]--> Walk_To_RunLoop_01 (0.2s) --> RunLoop_01
  RunLoop_01/02：每 1s 25% 概率 Trigger_ChangeRunType 换型
  退出：Bool_IsMovingDelay（IsMoving 的 0.1s 滞后）==false → Run_End（20 帧重启窗）
  闪避入口：Evade_To_RunLoop_01 → RunLoop_01（不经 WTR）
  Run 态受击 0.0s 抢断 / 普攻直出 Attack_Rush
```
实现时 `movingTime` 阈值 2.0 可标注为 **RUNTIME_RECONSTRUCTED 原作值**（非 FITTED）。

## 5. Unknowns（仍未关闭）

1. 写入函数的类/方法名（MHY 元数据加密；如需，另行立项 il2cpp 元数据解密或运行时调用栈深捕）。
2. E2 帧率无关性（全部存量场次同构建同帧率域）。
3. <0.413s 短间隙的重置边界（E4 细分）；E3 方向变化、E6 障碍物推墙、E8 攻击后计时存续。
4. WTR 下降沿的精确触发事件（惰性/事件驱动，观测 0.015s~124s）。

## 6. 终止条件声明

按任务 §10-B：静态无法恢复函数身份（MHY 加密），但运行时行为闭环——条件模型与阈值由 ≥2 独立证据交叉（9 回合统计 × 静态状态机门控 × IsMovingDelay 交叉），标记 **RUNTIME_RECONSTRUCTED**，未选择任何"手感"常数。
