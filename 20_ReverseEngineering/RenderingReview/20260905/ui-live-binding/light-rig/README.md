# 原生展示／商店灯组

2026-09-06：采集灯光已经能在当前原生渲染链中随角色平移、独立转向和调整强度。字段与 shader 消费端来自原始数据；灯组的刚性变换是本工程明确实现的展示功能。没有将它冒称为游戏 CPU 天气／灯光混合程序的完整复刻。

入口：[十张 1080p 动态灯光对照](preview/index.html)、[原始 GPU 对照](verification.json)、[字段来源合同](source-contract.json)。正式可见 Player 仍沿用之前的 HoyoToon 路线，原生场景和 Player 尚待接入。控制器未开始。

## 复用了什么

- 原始两页各 24 次绘制的常量、结构化灯光缓冲、反射字段和 shader。两页各有一份 64 × 128 字节的实体缓冲；本角色选中第 0 项，本页附加灯数量为 0。
- `_MiddlePointPosition`、`_AmbientGradientShape`、身体／头发主光方向，以及全局主光颜色与方向的准确字段位置。
- 实体第 0 项中的主光颜色、两个灯光代理位置和两组环境光颜色。其他 63 项、半径、权重、标志与未知参数保持字节不变。
- 之前的 `NapRenderEntity` 字段逆向记录：存在 MiddlePoint、覆盖主光色、灯光混合尺寸等结构。此记录证明有关数据结构存在，不足以证明本实现就是原游戏 CPU 算法。

原始两页数值都符合“代理点接近角色中点 + 25 × 主光方向、渐变法线接近 −0.5 × 主光方向”的关系，误差记录在合同中。这只是两个样本的数值观察，没有用它代替尚缺的 CPU 代码证据。旧 WeatherConfig 的历史布局问题仍然保留，未按新版字段强行解读。

## 实现与使用

运行时代码位于 `E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Runtime`：

- `RemielleNativeUIEntityBuffer.cs`：私有 GPU 缓冲、原始模板和本帧字节，保持 128 字节步长，按实际生命周期释放。
- `RemielleNativeUILightRig.cs`：灯组状态、命名常量更新、实体第 0 项更新、Deferred 主光同步。
- `RemielleNativeUIRenderer.cs` / `RemielleNativeUILateBody.cs`：准备每帧时恢复自己的捕获模板；灯组随后覆盖本帧动态字段。
- `RemielleNativeUILighting.Render(geometry, camera, rig)`：让 Deferred 使用与本帧角色绘制相同的灯组。

一次性创建灯组，逐帧在几何准备完成后、任何绘制开始前应用：

```csharp
var rig = new RemielleNativeUILightRig(geometry.Profile);

// 每一帧：pelvis 为当前原始 Bip001 Pelvis Transform。
var jitter = temporal.BeginFrame(width, height, resetHistory);
geometry.Prepare(width, height, jitter);
late.Prepare(geometry, driver);
var anchor = geometry.CurrentFrame.sceneToProfile.MultiplyPoint3x4(pelvis.position);
rig.Apply(geometry, late, anchor, Quaternion.Euler(0, lightYaw, 0),
          Vector3.one, 1f);
geometry.Render();
// 若需要深度辅助层，在此调用 depthHierarchy.Render(geometry, camera)。
lighting.Render(geometry, camera, rig);
late.Draw(lighting.Hdr, geometry.Depth);
temporal.Render(lighting.SampledDepth, lighting.Auxiliary, lighting.Hdr);
post.Render(lighting.Hdr, temporal.Color);
```

`anchor` 与 `rotation` 都在 profile 世界坐标系中；不能把旧 HoyoToon 页面为截图镜头换算过的方向直接回填。灯组旋转与镜头／头骨旋转独立。面部继续使用当前头骨逆矩阵，不能为了转灯重新校准骨骼。

光照代理点采用 `anchor + R × (capturedPoint − capturedAnchor)`；渐变按 shader 的 `dot(n,p) − w` 定义进行协变换。每帧从原始模板计算，不累计旋转或强度。传入捕获锚点、单位旋转、单位强度时，灯光常量和结构化缓冲保持逐字节不变。缺少实体索引、非零附加灯或非当前缓冲布局会明确报错，不会修改其他记录来勉强兼容。

GPU 上传使用 Unity 的 blittable 结构和显式释放，依据：[GraphicsBuffer.SetData](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GraphicsBuffer.SetData.html)、[Unity 绑定实现](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/GraphicsBuffer.bindings.cs)。

## 检查范围

运行 `E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-UILightRigChecks.ps1`。关闭此 Unity 工程后，脚本会在后台用 D3D11 构建临时审计场景、执行原始 shader 对照并生成本地网页。不会重建正式模型或动画。

两页各五种状态：原始灯光不变、模型平移并跟随骨盆、旋转 90°、旋转 180°、主光 RGB 与环境光强度改变。

| 检查 | 结果 |
|---|---|
| 当前 G-buffer 240 次绘制／10 帧 | 870,912,000 个打包 32 位值逐位一致 |
| 当前 Deferred 10 帧 | 2,484,555 个归属区域输出值逐位一致 |
| 当前后续 Body_1 10 帧 | 每后端 20,736,000 个半精度值逐位一致；三个 shader 组合均通过 |
| 缓冲更新 | 当前 CPU 字节与真实 GPU 回读一致；无关实体逐字节保留 |
| 后续绘制 | 深度、模板、alpha 和运动辅助目标不变，私有渲染目标无泄漏 |
| 最终画面 | 十张 1920×1080，16 帧 TAA／图；有限值、完整取景与灯光变化传递检查 |

这些是“原始 shader 对相同当前输入”的验证。生成型 HairShadow 原始 DXBC 仍缺，保留捕获汇编来源边界；没有将其计成已找回原始字节码。完整连续动作 Player 检查仍需在可见运行链接入后完成。

## 仍需推进

下一步连接正式可见原生场景／Player，核实辅助层消费者与附件显示，再做全动作连续帧验收。原游戏 CPU 灯光选择／混合、历史 WeatherConfig、动态场景阴影／GI 和附加灯选择仍未完整恢复；单独采集 sampler 原始描述符的缺口也未改变。受保护模型字节基线仍为 81/82，不能刷新旧哈希掩盖历史 prefab 差异。
