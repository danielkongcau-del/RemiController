# Remielle 静态纹理完整源数据恢复

2026-09-05。本轮 **8/8 缺失 mip 链全部恢复，正式 Unity 工程中的原生静态纹理达到 36/36 完整**。不再需要为了这八份纹理重复尝试 RenderDoc 游戏启动。

## 恢复结果

| 捕获 SHA 前缀 | 精确源对象 | 原存储格式 | 尺寸 | mip 数 |
|---|---|---|---:|---:|
| `30cb05c54363` | Eff_Mask_001_LD_09 | BC7 | 512×512 | 10 |
| `378025e2fe3e` | Female_Face_Lightmap_02 | RGBAHalf | 256×256 | 9 |
| `43e46f09807f` | Remielle_Wings_D | BC7 | 2048×2048 | 12 |
| `9f8d9fac108d` | Eff_Mask_1591 | DXT1Crunched | 2048×2048 | 12 |
| `a9802c448a22` | Eff_Mask_1590 | DXT1Crunched | 2048×2048 | 12 |
| `b1c1cb6f3923` | Eff_Mask_1587 | DXT1Crunched | 2048×2048 | 12 |
| `e07354a17c9a` | Eff_Mask_1588 | DXT1Crunched | 2048×2048 | 12 |
| `fc43364814f9` | Eff_Mask_1589 | DXT1Crunched | 2048×2048 | 12 |

这八份原来已有正确 mip0，缺的是下级数据。现在追加了 83 个原始下级 mip，增加 5,155,616 字节；36 份暂存纹理共 149,987,032 字节。原先完整的 28 份 `.bytes` 内容保持相同。当前 48 个源流包括原有 40 份和新恢复的 8 份。

## 为什么先前没有找到

旧匹配器只查角色材质指针导出的 40 个源流，缺少 DXT1/BC1 分支，也没有解开 DXT1Crunched。新扫描找到匹配当前捕获的脸部光照图、翅膀漫反射图及效果遮罩。名称仅用于展示，身份始终保留绝对 source block、CAB、pathID。

本次检查 StreamingAssets/Blocks、Persistent/Blocks、Bundles 和引擎序列化候选，共 9,934 个文件、85,886 个 Texture2D 对象。读取比较 12,176 个普通格式候选流，另检查 4,589 个 Crunch、较大尺寸或较少 mip 的 BC1 候选。25 份既有二维纹理的阳性对照全部重新命中。

有 6 个输入未加载出序列化对象，原样记录在 source-completion-verification.json 的 sourceErrors 中，没有计为已完整解析。八份目标均来自成功读取的明确源身份，候选读流错误为 0。本报告证明目标纹理完整，不声称全游戏所有文件都可解析。

## 资产位置与使用

- `source-completion-verification.json`：八份完整 SHA、源文件 SHA、CAB/pathID、TextureSettings 和恢复结果。
- `verified-source-streams.ndjson`：正式暂存程序消费的增补清单；普通源流位于 `full-v2/streams`，解包后的 BC1 完整链位于 `crunch-matches`。
- `crunch-verification.json`：4,589 个候选的检查记录。五个匹配对象的 `originalStream` 与 `originalStreamSha256` 指向 `crunch-streams` 中未改写的 Crunch 原流。
- `full-v2/scan.ndjson`：完整元数据索引。`all-sources.json`、`scan-targets.json` 定义检查范围。
- `unity-source-mip-verification.json`：最终 GPU 验收。`unity-mip-raw` / `native-mip-reference` 保存两条独立上传路径的二进制浮点读回。
- Unity 正式资产位于 `E:/ZZZ/local-only/RemielleHoyoToon/Assets/RenderingReview/CapturedNativeMaterial/Textures/<完整捕获SHA>.asset`；对应 `.bytes` 保存完整 DDS 头和子资源字节。按渲染目录的 `native-material-binding-manifest.json` 连接材质槽，不能靠名字重绑。

## 恢复方法与验证

1. `prepare_scan.py` 生成目标、范围和阳性对照。`AssetGraphProbe @scan-texture-sources` 只解析 Texture2D，关闭依赖扩展、split-file 合并和无关模型处理，避免写源目录。
2. 按格式、尺寸、mip 数筛选后逐字节比较 mip0；同 mip0 的所有候选必须给出同一条完整链，否则拒绝回填。
3. `@export-texture-subset` 导出原始 Crunch 候选。`scan_crunch.py` 使用 texture2ddecoder 1.0.6 的 `unpack_unity_crunch(data, level)` 逐级解开封装，恢复其中存储的 BC1 块。没有 RGB 重压缩、缩放、插值或 GenerateMips。
4. `verify_scan.py` 核对源文件身份、时间戳、长度、哈希及每级字节数；`prepare_native_material_textures.py` 消费增补清单，并阻止已完整纹理内容变化。
5. `RemielleNativeTextureImportAudit.Run` 导入并保存 36 份原格式纹理。`RemielleSourceMipGpuAudit.Run` 在 Unity D3D11 中以 Texture.Load 抽查新恢复的全部 91 级 mip，每级覆盖规则网格，共 17,467 个 texel、69,868 个 float32 通道值。
6. 独立 `NativeMipReference.exe` 直接调用 D3D11 创建原格式纹理。在同一 NVIDIA GeForce RTX 5070 Ti Laptop GPU 上，两条路径的 GPU 读回逐位相同，包括半精度源中的负零。CPU 解码参考另按 D3D11 的 BC1/sRGB 精度规则核验。

BC1 允许受限硬件解码误差，CPU 的 8 位预览不能作为所有 GPU 的逐位标准；默认 D3D11 适配器也可能是另一块集成显卡。最终对照明确选择与 Unity 相同的 NVIDIA，保存原始 `.f32`，避免 JSON 丢失负零符号。

## 重复运行

在 E:/ZZZ 下使用 D:/Anaconda/python.exe 运行本目录 `verify_scan.py`，再运行渲染目录的 `prepare_native_material_textures.py`。关闭同一 Unity 工程后，以 `-batchmode -quit -force-d3d11 -executeMethod RemielleSourceMipGpuAudit.Run` 运行正式工程，禁止 `-nographics`。随后运行 `Build-NativeMipReference.ps1`，将 `native-reference-inputs.txt` 和 `native-mip-reference` 作为两个参数传给生成的 exe，最后执行 `verify_unity_mips.py`。

现有原始扫描已保存，正常验证无需重扫 9,934 个文件。游戏资源更新后必须重新检查来源，不能沿用旧时间戳和哈希。

## 还剩什么

- **原生 sampler 描述符**：旧抓帧只有 3 个运行时句柄。新恢复的 TextureSettings 已保存，但不能当作实际 CreateSamplerState/GetDesc 证据。下一步确认原版 XXMI 可提供的状态来源或验证 GPU 采样行为，未确定字段保持未确定。
- **原生实时材质**：继续接入 t1 场景结构缓冲、真实 TEXCOORD0–8 顶点输出和六个原生 PS，再接可见相机。当前可见画面仍是已适配 HoyoToon 路径，资产完整不等于最终画面完全复刻。
- 本轮完成正式 Unity 资产和专项 GPU 验收；现有独立 Player 是前轮可见渲染构建，本轮未重打包。将原生材质接到可见路径时再构建新的 Player 并进行同条件对照。
- RenderDoc 游戏启动退出仍是未解决兼容性记录，已不阻塞八份纹理；官方 XXMI DLL 保持原样，不再为纹理安排 F12。
- 模型和原始动画未重建，已有 prefab 事件继续按 81/82 记录；附件显隐和完整控制器接在渲染对齐之后。

接口依据：[Unity DXT1/BC1](https://docs.unity3d.com/ja/2021.3/ScriptReference/TextureFormat.DXT1.html)、[Crunch 逐级解包](https://github.com/K0lb3/texture2ddecoder/blob/master/src/pylink.cpp)、[D3D11 功能规范 19.5 与 3.2.3.7](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm)、[HLSL Load](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-to-load)。
