# 原生顶点 Shader 移植与 GPU 验证

2026-09-05。本轮完成两套顶点程序的数学移植，并接入 Unity 原生材质编译候选。当前可见 Player 仍使用原来的审查渲染链；本轮没有重建模型、动画、场景或 Player。

## 已完成与验证范围

| 检查 | 结果 |
|---|---|
| 原始 DXBC → 移植 HLSL，同 GPU 输出 | 19 个 draw 绑定全部通过 |
| 测试量 | 68,185 次缓冲顶点调用，2,643,111 个有效 float32 值 |
| 数值与原始位比较 | 最大绝对误差 0，位差异 0 |
| 合成分支输入 | 44 组全部通过，位差异 0 |
| 旧反编译负向对照 | 第二实体输入下，面部 TEXCOORD0 有 2,642 处差异；修正版为 0 |
| Unity 编译 | 六个 Pass supported，0 error；已有 28 条 PS 真值转换 warning 仍记录在门禁中 |

两套 VS 是 `26214fb5eedfcbdd`（标准表面）和 `ed2f36604db4b10f`（面部）。源 DXBC、HLSL、VB0/VB1/VB3、三组 VS 常量缓冲及结构化实体缓冲均来自已有本地采集。

统计的是每次绑定下整个顶点缓冲的调用数，包含当前子网格没有引用的顶点；同一缓冲在不同 draw 下会重复验证。因此该数字不是模型独立顶点总数。按原始 DXBC 输出签名排除未定义的面部 TEXCOORD2/3/4.w 和 TEXCOORD8.w，未把这些空位当成有效输出。

## 改正了什么

1. 去掉 `CapturedNativeVertexStub.hlsl` 及其 meta，用两套真正的 VS 计算替换占位 UV、切线/视线向量、顶点色标志、面部 SDF、附加光及前后帧位置。
2. 修复面部反编译的地址覆盖：原 `ld_structured r1.xyz, r1.x, 16` 是同时读取 XYZ；反编译却先把 X 写回 `r1.x`，再把它当作 Y/Z 的新地址。现在保存索引后一次组装 XYZ。捕获中的实体索引 0 恰好掩盖了错误，故另设第二实体的负向对照，不能把该错误归因于先前可见窗口的偏暗。
3. 将面部条件分支里截断为六位小数的负 π，恢复为 DXBC 的 float32 原始位 `0xc0490fdb`。
4. 修正 VS/PS 共享绑定映射，并对 19 个 draw 的缓冲字节逐项断言：

| VS 槽 | 共享 PS 槽 | 内容 |
|---|---|---|
| b0 | b0 | 全局参数、相机和投影矩阵 |
| b1 | b1 | 当前/上一帧对象矩阵、运动参数 |
| b2 | b3 | UnityNapCB，角色和附加光参数 |
| t0 | t1 | 64 × 128 字节实体数据 |

VS b2 **不能**直接绑定 PS b2。源代码阶段槽号相同并不代表缓冲内容相同。

5. 删除绑定文档里“实验 DLL 已安装、下次 F8 就能取得 sampler”的过时描述。目前是官方原版 DLL，缺少的三个 sampler 描述符仍没有新增证据。

## 方法与复现

独立 C++ 程序在 NVIDIA GeForce RTX 5070 Ti Laptop GPU 上分别运行原始 DXBC 与重新编译的移植 HLSL，通过 D3D11 stream-output 保存每个顶点的 TEXCOORD0–8 和 SV_POSITION 原始浮点位。两侧使用相同的原始缓冲、输入布局和常量，不经过截图、8 位图像转换、材质纹理或 sampler。

实现依据 [Microsoft CreateGeometryShaderWithStreamOutput 文档](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-creategeometryshaderwithstreamoutput)：可使用上一阶段 VS 字节码提供输出签名，并关闭光栅化。重编译使用 Shader Model 5、严格语法及 O3；[编译参数说明](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/d3dcompile-constants)。没有向游戏注入 DLL，也没有修改启动器或保护设置。

在 `E:\ZZZ` 下执行：

```powershell
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/prepare_native_vertex_replay.py
& local-only/RemielleRenderingReview/20260905/Build-NativeVertexReplay.ps1
& local-only/RemielleRenderingReview/20260905/bin/NativeVertexReplay.exe local-only/RemielleRenderingReview/20260905/native-vertex-replay/inputs.txt local-only/RemielleRenderingReview/20260905/native-vertex-replay/gpu
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/verify_native_vertex_replay.py
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/check_native_vertex_branches.py
D:\Anaconda\python.exe local-only/RemielleRenderingReview/20260905/generate_native_material_shader.py
```

Unity 工程关闭时，可用隐藏批处理运行 `RemielleNativeMaterialCompileAudit.Run`，参数保留 `-force-d3d11`，不要加 `-nographics`。不要运行任何模型/动画生成入口。

## 文件位置

以下相对路径均以 `E:\ZZZ\local-only\RemielleRenderingReview\20260905` 为根：

- `native-vertex-gpu-verification.json`：19 draw 的逐位比较、输入/输出 SHA256。
- `native-vertex-branch-verification.json`：44 个合成分支与 1 个负向对照，明确区别于游戏真值。
- `native-vertex-replay/manifest.json`：所有原始输入的内容地址、输入布局与逐 draw 绑定。
- `native-vertex-replay/gpu/`：原始及移植输出 `.f32`、有效位掩码、重新编译的 DXBC。
- `native-vertex-replay/branch-tests/`：独立合成参数与 GPU 输出，禁止当作原游戏参数导入。
- `NativeVertexReplay.cpp` / `Build-NativeVertexReplay.ps1`：独立 GPU 对照程序。
- `prepare_native_vertex_replay.py` / `verify_native_vertex_replay.py` / `check_native_vertex_branches.py`：可复现入口。
- `generate_native_material_shader.py`：正式编译候选生成器，调用同一份经过测试的顶点转换函数。
- Unity 产物：`E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Shader\GeneratedNative\CapturedNativeVS_*.hlsl` 与 `CapturedNativeMaterialBodies.shader`。

## 当前边界与下一步

这次通过的是**捕获输入下的顶点数学**，不意味着完整角色画面已原生还原，也没有验证整个游戏所有 Shader 变体。44 个合成用例覆盖当前/上一帧位置选择、0–4 盏附加光、无实体/双实体、实体索引选择和上下界截断；它们仅用于翻译正确性检查。

后续进展：六套 PS 已在三组明确测试 sampler 下完成 57 个用例，7,454,640 个 float32 输出逐位一致，修复整数标志变零；详见 [像素翻译报告](native-pixel-port.md)。下一步核对 Unity 自身编译和实际绑定读回。三个真实 sampler 的 Filter/Address/Comparison/LOD 描述符仍缺证据；当前编译候选的 inline sampler 只服务编译，不应作为原生画面验收依据。

之后把已经验证的顶点程序接到实时蒙皮数据，持续供应当前/上一帧位置、对象/相机矩阵、实体缓冲和附加光，再替换离屏 G-buffer 的 o0/o1。动态级联/角色阴影、环境/反射/AO、后续轮廓/眼眉/发丝绘制与可见相机接入仍分别验收。控制器在渲染链稳定后推进。

受保护模型旧基线保持 81/82；历史 prefab 字节差异未被本轮修复或重新定基线。
