# 当前模型的深度与法线辅助层

展示 draw 63／64、商店对应 draw 64／65 的半分辨率处理现已恢复为独立 Unity 运行单元。它可以消费当前骨骼、镜头、抖动和尺寸对应的深度／法线；正式可见 Player 和后续辅助层消费者尚未接入。

## 验证结果

| 范围 | 结果 | 证据 |
|---|---|---|
| 两页游戏捕获 | 线性深度、法线及样本标记、最大／最小范围、交错深度和模板共 10,046,592 个值，逐位一致 | [固定采集验证](verification.json) |
| 当前输入 | 两套配置，各含绑定姿势／Idle_Loop／Walk_Start 与正／侧／背面，三种尺寸和裁剪距离 | [动态验证](../live-depth-hierarchy/verification.json) |
| 动态精确选择 | 法线／样本、范围、交错深度、模板和下一级范围，共 4,872,384 个值，与原汇编语义精确一致 | 同上 |
| 动态线性深度 | 885,888 个值；相对正确舍入 CPU 倒数最多相差一个 float32 相邻值 | 同上 |
| 资源释放 | 两套尺寸切换检查结束后私有目标泄漏为 0 | 同上 |
| 下一级范围 | 固定输入的 837,216 个值符合原汇编归并语义；**该级没有游戏导出真值** | [固定采集验证](verification.json) |

四个运行时生成 shader 仅有捕获汇编，未获得原始 DXBC。因此动态语义检查不写成“原始字节码回放”，下一层 mip 也不写成“采集已恢复”。固定捕获的实际输出仍是独立原游戏真值，其验证保持零位差。

## 原始行为

- 源哈希：Resolve VS `88dbc503fb7bb29a`、PS `52892a9ee3f22e47`；Reduce VS `f6629881b97d4608`、PS `4cffd0d19d95e5e8`。源文件与 SHA-256 在 [manifest.json](manifest.json)。
- 原始全分辨率为 2448×1368，首级 1224×684。每个 2×2 深度块选择最小深度，以 `1 / (Z.x * minimum + Z.y)` 线性化，同时保存该位置的法线和 2 位样本编号。相等时保留原始选择优先级。
- 范围目标为 RG16F，保存最大和最小原始深度。独立深度目标使用按输出像素奇偶交替选取的样本，**并非统一写最小值**。
- 第二步从独立范围副本读取，写范围目标 mip 1，避免同一资源输入／输出视图冲突。原指令从 `SV_Position * 2 + (0,0,1,1)` 取样，首个像素中心使首个范围块从索引 1 开始；不能擅自替换为普通 0／1 区块归并。
- 原始正常 UV 采样不需要修改模型或贴图。Gather 的 xyzw 顺序有 [微软指令定义](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/gather4--sm5---asm-) 支持。

## GPU 格式与数值处理

线性深度 R32F、法线 A2B10G10R10、范围 RG16F 两级、深度 D32/S8。Unity 在本后端不能直接创建 R10 格式的 sampled Texture2D；捕获夹具通过精确归一化上传后写入 R10 RenderTexture，并逐位回读确认输入没有变化。实际模型已经产生原生 R10 目标，直接引用它。

CPU 校验必须区分两种浮点规则。D3D 向低精度浮点格式转换使用向零舍入；NumPy 默认 float16 转换为就近偶数舍入，直接使用会把正确的范围输出误报为错误。修正参考转换后，原始两页范围和当前六组范围均精确一致。[D3D11.3 格式转换规范](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm) 是这一规则的依据。倒数计算另外遵循 [D3D11 浮点规则](https://learn.microsoft.com/en-us/windows/win32/direct3d11/floating-point-rules)；CPU 相邻值界限只用于动态数学检查，不改变固定捕获的逐位验收。

## 位置与调用

工程：`E:\ZZZ\local-only\RemielleHoyoToon`。

- `Assets/RenderingReview/Runtime/RemielleNativeUIDepthHierarchy.cs`：独立可释放运行单元。
- `Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIDepthHierarchy.shader`：两道原汇编推导的适配入口。
- `Assets/Editor/RemielleUIDepthHierarchyAudit.cs`、`RemielleUILiveDepthHierarchyAudit.cs`：固定捕获与当前输入 GPU 导出。

```csharp
geometry.Prepare(width, height, jitter);
late.Prepare(geometry, nativeAnimation);
geometry.Render();
hierarchy.Render(geometry, camera);
lighting.Render(geometry, camera);
late.Draw(lighting.Hdr, geometry.Depth);
temporal.Render(lighting.SampledDepth, lighting.Auxiliary, lighting.Hdr);
post.Render(lighting.Hdr, temporal.Color);
```

用完后 Dispose 各单元。`hierarchy.LinearDepth`、`NormalAndSample`、`Range` 和 `Depth` 是本帧私有输出；不能把它们代替 TAA 所需的全分辨率深度或 Deferred Auxiliary。当前已验收尺寸为 960×540、1280×720、768×432 和原始捕获尺寸；奇数尺寸及极小窗口策略尚未验收。

关闭该工程后运行 `E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-UIDepthHierarchyChecks.ps1`。脚本顺序准备源数据、执行两个后台 D3D11 Unity 检查并验证；不启动游戏。来源准备与运行时生成均保持原始资产不可变。
