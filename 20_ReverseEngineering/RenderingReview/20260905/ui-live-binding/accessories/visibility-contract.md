# 附件归属与原始显示数据

已确认旧全组件图中脚下的小环是 Remielle_Floater_02。原材质图复现其位置；27 次独立网格 GPU 编号绘制将整个小环的 259 个像素唯一归属到该网格。它包含 2,216 个顶点和左右各四根浮游骨，不是八个 Cannon 网格。

身份：源模型 block 2906705493，CAB-a54cb8ece006bf575b732cd3ab36fd55，renderer pathID 6709195539735168788。八个原始骨骼 PPtr 与当前蒙皮骨序逐项吻合。完整网格来源和哈希在 visibility-contract.json；不能把名字当作跨源身份。

## 为什么仍能看见

源默认 486 个节点和 27 个 renderer 都启用，与 9 月 4 日已有独立默认状态报告一致。Idle_Loop 的浮游骨缩放约 0.12669577，并非零；局部缩放和位置确实留下脚下小环。15 个主动作的 20,160 条相关缩放曲线、1,703,760 个键的时间、值、入出切线均与已封存 RANI 输入 float32 逐位一致。没有改写曲线。

## 已补回的显示依据

52 份旧逆向技能定义经过原证据图 SHA-256 复核，提取了 113 条相关原始动作：7 条 renderer 显示指令、96 条材质设置／中断、8 条抖动淡出／中断和 2 条实体缩放。所有具名 renderer 路径均能对应当前源模型。

RemielleOrigin_AirCombat_TriggerEvent 的 DefaultModifier/OnAdded/6 以 AirCombat_QTE05_Weapon 标签隐藏左右武器、背部武器和 Floater_02。AirCombat_ReleaseFloater_MeshVisible_Modifier 在 OnAdded 时以另一个标签显示 Floater_02，在 OnRemoved 时提交 Visible=false。后者仍是 Push，不是 Pop；不能仅按名字想当然改写。完整事件、原 JSON 指针与原生方法证据见 visibility-contract.json。

当前原生展示／商店渲染沿用采集中的七个源网格，不绘制 Floater_02，源 prefab 保留全部正确部件。旧全组件图用于资产检查，不能充当最终附件显示规则。

## 使用与边界

- 看归属与部件列表：同目录 index.html。
- 重跑：在工程关闭时执行 ../../Run-AccessoryVisibilityChecks.ps1；后台 Unity 使用 D3D11，并且只操作未保存场景副本。
- 接控制器时消费 visibility-contract.json 的 authoredActions，保留来源、目标、标签、条件所在 JSON 指针和原始动作类型。
- 本轮完成身份、默认状态、缩放源值和事件定义整理；尚未运行原始标签竞争、modifier 生命周期和战斗事件调度，不宣称完整战斗显隐已验证。
- 源模型、原始动画和已有正式渲染场景未改写。旧模型字节基线 81/82 的历史差异继续保留。

## 浮游骨缩放范围

此表为左右八根骨的 24 条轴曲线键值范围，不等同于 Visible 状态。

| 动作 | 最小 | 最大 |
|---|---:|---:|
| Walk_to_Run_01 | 1 | 1 |
| Walk_Start_End | 1 | 1 |
| Walk_Start | 0.232080162 | 0.232080534 |
| Walk_Loop | 0.126695752 | 0.126695797 |
| Walk_End | 0.202522352 | 0.202522486 |
| Run_Transform_02 | 0.268912941 | 0.26891306 |
| Run_Transform_01 | 1 | 1 |
| Run_Loop_02 | 0.15511027 | 0.155110374 |
| Run_End | 0.11501088 | 0.115010932 |
| MC_Idle_Loop | 1 | 1 |
| Idle_Loop | 0.126695752 | 0.126695797 |
| Idle_AirState_Front_Loop | 1 | 1 |
| Idle_AirState_Back_Loop | 1 | 1 |
| Idle_AFK | 0.126695752 | 0.126695797 |
| Evade_to_Run_01 | 1 | 1 |

审计使用 [Unity BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) 获取当前蒙皮快照，使用 [GetCurveBindings](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimationUtility.GetCurveBindings.html) 只读导出曲线。编号写入线性目标，使用 Vector 属性，避免颜色属性转换改变网格编号。
