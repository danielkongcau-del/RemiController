# 展示与商店原生基础材质：补全及验证

日期：2026-09-05。结果：[verification.json](verification.json)。旧数据复核：[../prior-reverse-reuse.md](../prior-reverse-reuse.md)。

本轮完成了展示与商店基础表面阶段的实际 Unity 移植。当前可见 Player 仍沿用已发布的 HoyoToon 预览；本报告不表示整条原生 UI 渲染链已经替换到可见相机。

## 通过的范围

| 验证项 | 结果 |
|---|---|
| 捕获页面 | 角色展示、时装商店，共 2 页 |
| 基础绘制 | 每页 8 次，共 16 次；各页总序列为 24 次 |
| 原生程序 | 2 个 VS、5 个 PS；保留 block/CAB/pathID/序列化记录与 DXBC 来源 |
| VS 原生对比 | 110,068 次缓冲顶点调用，4,260,444 个定义有效的 float32 分量逐位一致 |
| PS 原生对比 | 1,500,428 个逐 draw 写入像素，24,006,848 个 float32 分量逐位一致 |
| Unity 实际绑定 | 同一 16 次绘制，覆盖像素一致，24,006,848 个 float32 分量逐位一致 |
| 未修复负向对照 | Body_2、Body_1、Face 三项均检出原错误 |
| 当前纹理覆盖 | 23 份唯一纹理中 21 份来源完整；红色小图缺低级 mip，独立 UI 数组的未用第 7 层待证 |

像素数是各次绘制分别计算后相加，同一屏幕位置可能重复出现，不是整幅画面的独占像素数。VS 数量按绑定缓冲中所有顶点计算，包含该 draw 索引范围之外的顶点。未定义的原生 VS 输出分量由反射掩码排除。

## 本轮修复

UI 脸部 VS 的反编译文本把负 π 截短为 `-3.141593`。现在根据原始 `and ... l(0xc0490fdb)` 恢复同一 float32 位模式。

五份 UI PS 的向量条件赋值被反编译成了 `? float2(0,0) : 0`；原始指令实际保存了整数标记 1、2。它们参与材质分类及边缘着色，不能截成零。生成器逐项核对原始 AND 指令，恢复可保留条件语义的数值；三项负向对照证明旧文本确实改变了输出。

UI 的 VS b0/b1/b2 与 PS 同编号缓冲逐字节相同，VS t0 对应 PS t1 的实体数据。没有套用战斗 VS b2→PS b3 的不同规则。Unity 使用统一四组常量缓冲，但仅对原 shader 声明以外的尾部补零；实际读取范围全部来自捕获字节。

该单阶段夹具在两端使用一致的正面约定，Unity 对应 `SetInvertCulling(true)`，绘制结束后恢复。这不是完整游戏序列的状态；完整序列使用默认 Unity 剔除约定，详见新报告。D3D11 四路 RGBA32F MRT、反转深度、相同纹理和明确的采样器配置用于两端比较。

## 补齐的翅膀纹理

原皮 UI 材质的 `_LightTex` 指针实际引用共享的 `Remielle_Ramiel_Wings_N`，pathID 为 `4927106087706469158`。从旧 `remielle_entries.json` 定位原包，再扫描并验证捕获 mip0 的完整字节，得到：

- 源包：`E:\ZZZ\miHoYo Launcher\games\ZenlessZoneZero Game\ZenlessZoneZero_Data\StreamingAssets\Blocks\1876963883.blk`。
- CAB：`CAB-10378eee1e9a063fbc852adb18dd4efb`；RGB24，1024×1024，11 级 mip，原始流 4,194,303 字节。
- 完整 DDS：[full-textures/WingsLightTex.dds](full-textures/WingsLightTex.dds)，RGBA 数据 5,592,404 字节，另加 DDS 头。
- 来源、各级偏移和哈希：[source-texture-completion.json](source-texture-completion.json)；源流位于 `source-texture-scan/streams/`。

转换只给原 RGB 字节加上不透明 alpha，最高级与捕获 RGBA 像素完整匹配；其余 mip 直接取原始流，没有缩放、插值、重压缩或生成 mip。换用完整链后再次执行 PS 和 Unity 对照，仍为零位差异。这个文件是补全的新证据产物，正式模型材质未被重建。

## 采样器与剩余纹理边界

A/B/C 是三个已捕获句柄的身份关系，原始 `D3D11_SAMPLER_DESC` 仍然没有拿到。本测试使用已测过的 Unity 配置：A 为 trilinear repeat，B 为 trilinear clamp，C 为 linear clamp comparison GREATER。

商店 s2 有记录，属于 A；角色展示 s2 的状态未在该帧日志中出现，测试显式选择 B，并使用单独 keyword。因此本报告证明相同测试输入下的程序/绑定等价，不证明这些测试采样参数等于原游戏。

剩余 4×4 红色 RGBA 资源描述符声明 3 级 mip，但捕获只包含最高级。其 mip0 为均匀 `(255,0,0,0)`。即使可以构造相同颜色的低级 mip，也不能把构造值登记为取回的原始资源；当前两端都只上传捕获到的一级。

## 文件与复现

| 位置 | 用途 |
|---|---|
| [manifest.json](manifest.json) | 每页逐 draw 的输入布局、VB/IB、CB、纹理、采样器身份及未知项 |
| `translated/` | 有原始指令依据的两套 VS、五套 PS HLSL |
| `vertex-gpu/`、`pixel-gpu/`、`unity-gpu/` | 原生与翻译/Unity 的原始浮点结果和覆盖索引 |
| `negative-gpu/` | 未修复反编译文本的失败对照，作为回归证据保留 |
| `E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Shader\GeneratedNativeUI` | Unity shader 和七个 include |
| `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Editor\RemielleNativeUIMaterialGpuAudit.cs` | 仅在内存创建捕获网格、测试材质、附加纹理与缓冲的 Editor 检查 |
| [../Run-UINativeMaterialChecks.ps1](../Run-UINativeMaterialChecks.ps1) | 重复执行本轮补全、原生对比、Unity 绑定与汇总检查 |

运行完整脚本前关闭 Unity。脚本使用隐藏 D3D11 批处理，不调用游戏，不重建模型/动画/Player。它复用已编译的共享原生测试程序；若更改或重编译 `NativePixelReplay`，战斗 PS 与 Unity 参考门禁也需要刷新。本轮已重新执行 57 项战斗 PS、3 项负向对照和 19 项 Unity 原生参考，原门禁继续通过。

纯读取复核可运行 `D:\Anaconda\python.exe E:\ZZZ\local-only\RemielleRenderingReview\20260905\verify_ui_native_material.py`。汇总中的旧模型基线继续保留 81/82，不会因为新渲染门禁通过而变绿。

原始 mip 上传按 Unity 的格式、布局与 mip 总大小要求逐项检查，并使用 `Apply(false,false)` 保留输入 mip：[Unity LoadRawTextureData 文档](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html)。采样器命名和独立纹理/采样状态绑定参考 [Unity sampler states 文档](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html)，原始游戏描述符是否取得则以本地证据记录为准。

## 下一步

每页完整 24 次绘制、渲染状态与颜色对照现已通过，见 [完整序列报告](../ui-full-sequence/README.md)。本基础夹具仍保留原来的测试采样配置，不把它误记为游戏采样状态。接下来用当前骨架、相机和展示／商店光照配置生产动态输入，接入已验证的 Deferred 与后处理，停在接控制器之前。
