# Body_1 第二材质槽：当前模型的后续绘制已接入

展示 draw 92、商店 draw 93 位于角色 Deferred 之后、Bloom／TAA 之前。它使用 Body_1 子网格 1 的 338 个三角形。现已接入 Unity，固定采集以及两页各 15 个主动作的当前骨骼／相机输入均通过独立原始字节码对照。正式可见场景和 Player 的最终验收仍待完成。

[验证数据](verification.json)：原始和翻译后端各比较两页合计 26,790,912 个 HDR 通道值，包含全图，均为零位差；实际改写 25,480／10,950 个像素，总计 36,430 个。alpha 和深度／模板保持不变。

独立阶段比较了原始 VS＋原始 PS、原始 VS＋翻译 PS、翻译 VS＋翻译 PS；两次比较合计 596,760 个有定义的 RGBA／深度 float32 值精确一致。未写入的其余 MRT 保持初始化值，不计入有效输出数。

新增 [Unity 固定采集验证](unity-verification.json)：两页 26,790,912 个半精度通道值精确一致，133 个原始命名常量字段已建立绑定合同。[当前模型验证](../live-late-body/verification.json)：30/30 组输入、每后端 62,208,000 个半精度值精确一致，覆盖 15 个主动作和正／侧／背面；实际改变合成像素 23,511 个。深度／模板、alpha 和 TAA 运动辅助目标保持不变，私有目标泄漏为 0。每动作只采样一个姿态，此门禁不代替全动作连续 Player 验收。

## 来源身份

| 对象 | block | CAB | pathID |
|---|---|---|---|
| Body_1 Mesh | 261957958 | CAB-060271f061d155c881cf052414809d4d | 7841027147514059888 |
| 原 renderer | 2906705493 | CAB-a54cb8ece006bf575b732cd3ab36fd55 | 3586499500754002266 |
| renderer 槽 1 的原材质 | 2243828476 | CAB-de00779273c02454df66b8ff37ccbacf | -7413843588448389930 |
| 对应 UI 源材质 | 2932222405 | CAB-bda2c57866b36c6623401cecf81ee3e7 | 7815526188352471457 |
| NapAvatarUITransparent | 2507100060 | CAB-c03b770940fd4cba37836f6f146c827c | -97646000100089233 |

原材质指针由 renderer 所属 CAB 的 external 12 解析；UI 材质的 Shader 由其所属 CAB 的 external 6 解析，再由原 block 对象目录校验。原 renderer 保存的是普通场景材质；UI 材质有独立源身份，不能把两者写成同一个指针。游戏在运行时切换材质的具体事件和实时对象指针没有被直接捕获，表中不作此声明。

VS `1f6ab42231416fdb`、PS `345d8de4fe86facc` 均回到 `HalfResCharacterToon` Pass。虽然 Pass 名包含 HalfRes，本次两页实际目标是 **2448×1368 全分辨率**。低／中／高质量的相关 shader 变体字节相同，不凭哈希推断实际全局质量档。

IB、VB0／1／3 与 Body_1 先前 G-buffer 绘制逐字节相同，firstIndex 57612、indexCount 1014 与源 Mesh 的第二个子网格相同。没有修改模型资产，也没有增加新源网格。

## 修复和状态对证

原始反编译 VS 将同一个输出寄存器拆成 `TEXCOORD5` 的标量和 `TEXCOORD8` 的三分量，却仍输出 `o5.xyzw = r4.wxyz`，导致无法编译。派生翻译改为分别写入两个声明；原始字节码和 HLSL 保持原样，原始汇编的寄存器写入与独立 GPU 比较共同验证该修复。

动态检查还发现 PS 的整数标志错误：原指令为 `and r0.z, r5.x, l(1)`，反编译结果却是 `r0.z = r5.x ? 0.000000 : 0;`。固定采集没有触发差异，部分新姿态却在 1～2 个像素选错着色分支。新增“原 VS＋翻译 PS”独立对照将其定位到像素阶段；沿用已有的整数布尔修复器，把该条件写为数值 1／0 后，30 组动态输入全部归零差异。没有提高测试容差。

这里必须保留布尔含义，而不是将整数位型 `1` 重解释为极小浮点数后再比较。微软的 [movc 指令定义](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/movc--sm4---asm-) 按是否有非零位选分支；[浮点比较定义](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/direct3dhlsl/ge--sm4---asm-.md) 则会在比较前将非正规数归零。具体修复仍以本地原始指令和独立 GPU 对照为依据。

源 Pass 的 `_Cull`／`_ZWriteOn` 从 UI 材质分别得到 Back／Off，反向深度使用 GreaterEqual。混合因子来自 `_HalfResDstBlend`、`_HalfResAlphaSrcBlend`、`_HalfResAlphaDstBlend` 三个运行时全局属性，不能将它们在序列化结构中的占位零直接作为实际值。使用 RGB One／InvSrcAlpha、alpha Zero／One 的候选后，两页完整 HDR 与采集逐位一致。

采样器 A 使用此前证实的双线性重复，LUT 使用三线性钳制；s4 在两份日志中没有创建或绑定记录，夹具明确使用三线性钳制，当前两页合成输出与其一致。这也符合 [D3D11 对 NULL sampler 的默认状态](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-pssetsamplers)，但日志缺失不能证明当时 s4 必定为 NULL，更不能据此宣称恢复了原始创建描述符。

## 使用与重跑

目录根：`E:\ZZZ\local-only\RemielleRenderingReview\20260905`。

- `prepare_ui_late_body.py`：网格／renderer／捕获输入来源；[来源准备报告](source-preparation.json) 中 runtime／GPU 字段只描述准备阶段，其 `gpuPassVerified=false` 不代表后来独立合成验证失败。
- `prepare_ui_late_body_replay.py`：完整纹理链复用、常量和输入绑定、必要的派生 VS／PS 修复；原始资源保持不变。
- `prepare_ui_late_body_forward.py`：原颜色／深度初始目标、限定身份以及显式状态候选。
- `NativeUIForwardReplay.cpp`、`Build-NativeUIForwardReplay.ps1`：独立原始／翻译字节码全分辨率前向合成，结果在 `forward-gpu`。
- `Run-UILateBodyChecks.ps1`：顺序执行来源准备、两个独立阶段、两个前向合成及 `verify_ui_late_body.py`。
- `generate_ui_late_body_shader.py`：生成 Unity 适配 shader 和 133 个命名字段的合同。
- `Run-UILateBodyUnityChecks.ps1`：构建两个独立配置、执行 Unity 两页捕获和 30 个当前姿态，再与原始 VS／PS、原始 VS／翻译 PS、翻译 VS／PS 三种结果比较。
- 原始 Pass 状态：[shader-state-evidence.json](shader-state-evidence.json)；绑定和来源：[forward-inputs.json](forward-inputs.json)、[isolated-inputs.json](isolated-inputs.json)。

工程资源位于 `E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\NativeUILive\LateBody`，两个 profile 只引用来源纹理并保存当前绑定模板；同目录私有模板 Mesh 保留原索引／UV。运行时入口为 `Assets/RenderingReview/Runtime/RemielleNativeUILateBody.cs`，shader 在 `Assets/RenderingReview/Shader/GeneratedNativeUILateBody`。

每帧先 `geometry.Prepare`，然后 `late.Prepare(geometry, driver)`，再 `geometry.Render`、`lighting.Render`、`late.Draw(lighting.Hdr, geometry.Depth)`，最后执行 TAA 与后处理。Late 的 Prepare 必须早于几何 Render 的上一帧状态提交；所有资源单元用完后均需 Dispose。实际完整顺序见 [TAA 使用说明](../temporal-rendering.md)。

这些执行均为本地离线，不启动游戏、不注入。来源准备、独立捕获、Unity 捕获和动态输入分别出具报告；早期报告中的 runtime／live 字段只描述其检查阶段，不能代替最新动态验证。动态光照、辅助层、附件和可见连续 Player 继续推进。
