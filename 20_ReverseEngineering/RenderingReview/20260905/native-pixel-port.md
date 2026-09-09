# 六套原生像素 Shader：翻译验证与修复

2026-09-05。本轮将六套像素程序与游戏原始 DXBC 放到同一 GPU 上对照，并把确认的修复写入 Unity 材质候选。**这验证 Shader 翻译，不代表整张画面已经与游戏一致，也没有把测试 sampler 当成真实采样器描述符。**

## 结果

| 项目 | 结果 |
|---|---|
| PS 变体 / draw 绑定 | 6 / 19 |
| 明确记录的测试 sampler 组 | 3：线性、点过滤、各向异性；比较过滤也覆盖 LE/GE |
| 正向用例 | 57 |
| 有效像素 / float32 输出 | 465,915 / 7,454,640 |
| 四个输出的位差异 / 最大绝对误差 | **0 / 0** |
| 未修复反编译负向对照 | Body_1、Body_2、Face 三组均检测到分类错误 |
| Unity 实际激活 | 六个 Pass 加 draw 374 的 s3 变体，共 7 组，通过 |

像素按每个独立 draw 及测试配置统计，绘制之间重叠的像素会重复计数；不是最终角色可见面积。四个输出均以 RGBA32Float 接收，避免 Half、8 位颜色或 R10 量化隐藏翻译错误；不把 RGBA32Float 测试目标当成游戏原始 G-buffer 格式。

## 修复的错误

六份反编译代码都有类似这一行：

```hlsl
r5.y = r5.x ? 0.000000 : 0;
```

其原始 DXBC 实际为 `and r5.y, r5.x, l(1)`，生成的是整数布尔标志。反编译器按 float 六位小数打印整数原始位，误变成恒零。这个标志参与皮肤分类和附加光照选择。修复为用于条件判断的数值 `1.0`，并要求源反汇编存在对应寄存器的原始 AND 指令，否则生成器拒绝转换。没有全局替换其他零值，也没有更改源文件。

保留原错误代码的三个负向用例仍会失败，确认新门禁能检测该缺陷。修正后的六个 PS 在全部 57 个用例中逐位通过，不使用误差容限放行。

这些代码位于尚未接入可见相机的原生材质候选中，不能据此声称先前用户窗口中的偏暗由该错误造成。本轮未重建 Player。

## 输入与仍缺少的原始数据

- 使用原始 VS DXBC、原始 VB0/VB1/VB3、IB 及正确的子网格索引区间；使用捕获的五组 PS 常量和 64×128 字节实体数据。
- 使用全部 36 份静态纹理的完整源 mip/数组链，包括原始 BC1/BC6H/BC7、sRGB 标志、RGBAHalf。没有重压缩或自动生成 mip。
- 原四层主阴影只捕获了第一层，测试为此提供明确的四层合成深度图；该文件放在 `native-pixel-replay` 内，绝不回填为游戏资产。角色专用单层阴影使用捕获数据。
- 三个原始 sampler 只有 handle/逐 draw 槽位证据，原始 `Filter/Address/Comparison/LOD` 仍未知。两侧都使用相同的三组受控测试状态，证明当前输入范围内的翻译一致性。
- 无裁面、独立反向深度、禁用混合及 RGBA32F 输出是翻译夹具的渲染状态；没有冒充整帧原始深度、模板、混合或可见性。

采样器类型与字段参考 [Microsoft D3D11_SAMPLER_DESC](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_sampler_desc)；纹理 SRV 类型遵循 [CreateShaderResourceView](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-createshaderresourceview) 的有类型视图规则。

## Unity 中的变化

生成器现在使用与对照程序相同的像素翻译函数。采样器从六个槽各自猜测，改为保留捕获的三个 handle 分组。标准材质 PS `77bdd348772c62c8` 的 s3 有两种真实分组：draw 374 启用 `CAPTURED_S3_HANDLE_B`，draw 375–383 关闭。Unity 门禁实际激活六个默认 Pass 与额外 s3 变体，并记录所有生成文件 SHA256。

后续 Unity 实际绑定已完成：19 组、2,484,880 个 float32 输出逐位一致，见 [Unity 绑定报告](unity-native-material-binding.md)。实际 inline sampler 为新增 mode 3：A/B 三线性过滤，C 线性 GREATER 比较；这是测得的 Unity 测试配置。分组已经有捕获依据，具体采样描述符没有；后续不能把编译成功视为参数恢复完成。原有 28 条反编译真值转 unsigned 警告仍原样记录，未通过屏蔽 warning 隐藏。

## 文件与复现

根目录：`E:\ZZZ\local-only\RemielleRenderingReview\20260905`。

- `native-pixel-gpu-verification.json`：57 个正向与 3 个负向结果、每个原始浮点/像素索引文件 SHA256。
- `native-pixel-replay/manifest.json`：逐 draw 完整输入、测试状态、来源文件 SHA256。
- `native-pixel-replay/gpu/`：`*-native.f32`、`*-ported.f32` 为每像素四个 float4；`.u32` 是原始 2544×1440 坐标的线性像素索引；另存重新编译 DXBC 与反汇编。
- `native-pixel-replay/negative-control/`：独立保存未修复代码及预期失败的三组输出。
- `translate_native_pixel_shader.py`：带原始指令断言的修复函数；同时供测试与 Unity 生成器使用。
- `NativePixelReplay.cpp` / `Build-NativePixelReplay.ps1`：独立 D3D11 对照程序，无游戏启动或注入操作。

在 `E:\ZZZ` 下：

```powershell
& local-only/RemielleRenderingReview/20260905/Build-NativePixelReplay.ps1
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/prepare_native_pixel_replay.py --modes 0 1 2
& local-only/RemielleRenderingReview/20260905/bin/NativePixelReplay.exe local-only/RemielleRenderingReview/20260905/native-pixel-replay/inputs.txt local-only/RemielleRenderingReview/20260905/native-pixel-replay/gpu
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/prepare_native_pixel_replay.py --negative-control
& local-only/RemielleRenderingReview/20260905/bin/NativePixelReplay.exe local-only/RemielleRenderingReview/20260905/native-pixel-replay/negative-control/inputs.txt local-only/RemielleRenderingReview/20260905/native-pixel-replay/negative-control/gpu
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/verify_native_pixel_replay.py
```

## 接下来

Unity 固定输入绑定与 GPU 读回现已完成。接下来接当前/上一帧蒙皮、对象/相机矩阵、实体缓冲与动态光照，再替换实时 G-buffer o0/o1。原始 sampler 描述符、完整场景阴影/环境/反射/AO、后续轮廓/眼眉/发丝步骤和可见相机集成继续作为独立缺口保留。旧模型字节基线仍是 81/82，历史 prefab 差异没有被本轮修复。
