# Unity 原生材质实际绑定与 GPU 读回

2026-09-05。两套 VS、六套 PS、19 组捕获绘制已经在 Unity 6000.3.17f1 / D3D11 中完成实际纹理与缓冲绑定，并与同一显卡执行的游戏原始 DXBC 逐位对照。**固定输入的绑定环节通过；尚未把这套完整材质程序替换到可见相机，也没有完成实时动态输入。**

## 验证结果

| 项目 | 结果 |
|---|---|
| 显卡 | NVIDIA GeForce RTX 5070 Ti Laptop GPU |
| 原始顶点 / 像素变体 | 2 / 6 |
| 独立绘制 | 19，含 draw 374 的独立 s3 分组 |
| 有效像素 | 155,305；独立 draw 重叠区域重复计数 |
| 四目标 float32 值 | **2,484,880** |
| 原生与 Unity 像素位置 | 完全一致，无翻转、对齐或裁剪补偿 |
| 位差异 / 最大绝对误差 | **0 / 0**，无误差容限 |
| 静态纹理 | 36 份完整源 mip/数组链；沿用已验证 `.asset` |
| 正反面回退对照 | Body_1/Body_2 能检出错误；Face 作为不受此分支影响的对照 |
| 观察器部署 | 仅 Editor；Any Platform 与 Windows Player 均禁用 |

三组已有原生 PS 测试的 57 用例继续保留；本页新增的是 Unity 引擎内绑定测试。四个目标均为 RGBA32Float，避免目标量化掩盖差异。这里的 155,305 像素不是游戏最终角色面积。

## 实际接入中修复的问题

1. **正反面约定。** 捕获的 IB 在原生夹具中采用顺时针正面；Unity 的实际 Rasterizer 是 `FrontCounterClockwise=TRUE`。`Cull Off` 只关闭裁面，不会统一 `SV_IsFrontFace`。这会改变双面材质 UV 分支和法线方向。离线捕获绘制以 `CommandBuffer.SetInvertCulling(true)` 对应原始绕序，绘制后恢复；状态读回确认 FrontCCW=0。该处只用于原始捕获几何，不能不经判断套到镜像/旋转后的正式模型上。
2. **比较采样器。** 在本机 Unity / reversed-Z 下，inline `LinearClampCompare` 实际是 `COMPARISON_MIN_MAG_LINEAR_MIP_POINT + GREATER`，不是之前离线 mode 0 的 LESS_EQUAL。新增 mode 3 原始 DXBC 夹具，明确匹配这组被观察到的 Unity 状态。
3. **Mip 过滤。** 普通 A/B inline sampler 明确命名为 `TrilinearRepeat` / `TrilinearClamp`，对应 MIN_MAG_MIP_LINEAR，保留捕获的三组 handle 关系。比较 sampler 保持线性比较、mip 点选。
4. **常量缓冲长度。** Face 的原始缓冲比统一布局短。上传先检查足以覆盖该 PS 原始声明，再仅对未引用尾部补零。所有被程序消费的常量保持源字节；不会以零填充掩盖缺少的有效数据。
5. **深度清除。** Unity CommandBuffer 采用逻辑清除深度 1，对应此 reversed-Z D3D11 测试的实际远端 0；ZTest LEqual 对应实际 GREATER_EQUAL。实际覆盖及颜色共同通过对照。

这些修复发生在原生材质候选和离线测试中，不能据此认定用户之前所见可见 HoyoToon 窗口的偏暗由它们引起。当前 Player 没有重建。

## 证据边界

- 原始游戏 sampler 描述符仍是 **0/3**。本次读取的是我们自己的 Unity D3D11 上下文，不能替代游戏 `GetDesc` 证据。A/B/C 的 handle 分组有捕获来源，具体过滤与比较条件仍为明确测试配置。
- 四层主阴影使用明确标记的合成 R16 数组，角色单层阴影使用捕获数据。合成层没有回填到正式资产。
- Unity 纹理和缓冲的正确性由四目标全量像素对照证明。观察回调发生前 Unity 已清空 SRV，状态 JSON 的 `resources: []` 不表示材质没绑纹理，也不能据此声称已经读到逐纹理 SRV 描述符。Sampler 槽会被编译器重排，未使用槽也可能保留前一 Pass 状态；不得按槽号反推原始游戏 sampler。
- 顶点和常量来自固定捕获帧。还需接当前/上一帧蒙皮、对象/相机矩阵、实体数据、场景灯光/阴影，并验证实时材质主体与 Deferred 链；之后才能接可见相机。
- 关卡环境、反射/AO、轮廓/眼眉/发丝后续步骤、特效和 TAA 仍有独立缺口。模型旧字节基线仍为 81/82，不修改旧基线。

## 文件与使用

根目录：`E:\ZZZ\local-only\RemielleRenderingReview\20260905`。

- `unity-native-material-gpu-verification.json`：严格数值结果与文件 SHA256。
- `unity-native-material-readback.json`：Unity 设备、实际使用的代码/纹理文件、19 组输出路径。
- `native-pixel-replay/unity-gpu/`：每个 draw 的 `.f32`、`.u32` 和状态 JSON。每像素四个 float4；像素索引为原始 2544×1440 图像的 `y*2544+x`。
- `native-pixel-replay/unity-reference/`：19 组匹配 Unity 状态的原始 DXBC / 重编译 HLSL 输出及完整输入清单。
- `native-pixel-replay/unity-winding-negative/`：刻意恢复错误正反面设置的两组失败用例和 Face 对照，不能作为正确渲染结果使用。
- `UnityNativeStateProbe.cpp` / `Build-UnityNativeStateProbe.ps1`：通过 Unity 官方原生插件 API 读取本工程测试状态；不附加其他进程。
- Unity `Assets/Editor/RemielleNativeMaterialGpuAudit.cs`：构造临时捕获 Mesh、常量/实体缓冲、MRT，执行实际 VS/PS 并 GPU 读回；不保存模型、场景或动画。

先关闭同一工程的 Unity，按顺序执行。所有 Unity 批处理必须保留 D3D11，不能加 `-nographics`；从脚本启动应使用隐藏窗口。

完整重跑入口为 `Run-NativeMaterialBindingChecks.ps1`，包含原生 57 用例、负向对照、Unity 19 用例和总报告检查；不重建模型/动画/场景/Player。以下是分步命令。

```powershell
& local-only/RemielleRenderingReview/20260905/Build-NativePixelReplay.ps1
& local-only/RemielleRenderingReview/20260905/Build-UnityNativeStateProbe.ps1
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/generate_native_material_shader.py
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/prepare_native_pixel_replay.py --unity-fixture
& local-only/RemielleRenderingReview/20260905/bin/NativePixelReplay.exe local-only/RemielleRenderingReview/20260905/native-pixel-replay/unity-reference/inputs.txt local-only/RemielleRenderingReview/20260905/native-pixel-replay/unity-reference/gpu
```

随后 Unity batch 依次执行 `RemielleNativeMaterialGpuAudit.Run`、`RemielleNativeMaterialGpuAudit.RunWindingNegativeControl`、`RemielleNativeMaterialCompileAudit.Run`，等待每次结束。最后运行 `verify_unity_native_material.py`，再执行 `collect_review.py` 更新总报告。正常使用模型不需要运行这些离线测试。

接口依据：[Unity SetInvertCulling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetInvertCulling.html)、[Unity IssuePluginEvent](https://docs.unity3d.com/kr/6000.0/ScriptReference/Rendering.CommandBuffer.IssuePluginEvent.html)、[Unity Texture samplers](https://docs.unity.cn/6000.1/Documentation/Manual/SL-SamplerStates.html)、[D3D11 比较函数枚举](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_comparison_func)。本机具体状态以保存的 GPU 回调记录为准。
