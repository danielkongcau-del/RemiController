# 展示／商店完整角色绘制：资产、状态与 GPU 验证

2026-09-05。本阶段已经完成两页各 24 次角色绘制的顺序重放。独立 D3D11 和 Unity 的四路颜色、深度、模板均与游戏原始抓帧逐位一致。**输入仍是捕获时的几何、常量和资源；当前可见 Player 尚未替换为实时原生材质。**

## 验证结果与范围

| 检查 | 当前结果 |
|---|---|
| 原游戏样本 | 角色展示 `144810`、时装商店 `144910`，分辨率均为 2448×1368 |
| 顺序绘制 | 展示 draw 39–62；商店 draw 40–63，各 24 次 |
| Shader | 6 个 VS、10 个 PS，VS 与 PS 使用独立常量、纹理绑定 |
| 单阶段原生对照 | 46 次具有原始 DXBC；55,651,302 个有效 float32 分量与 Unity 逐位一致 |
| 运行时生成的发丝 | 2 次只有原始汇编；跨宿主执行通过，其实际捕获姿态的深度／模板另由完整顺序对照验证 |
| 独立 D3D11 顺序对照 | 48/48 通过；每次绘制后的四路颜色、深度、模板全部逐位相同 |
| Unity 顺序对照 | 48/48 通过；读取实际原生格式的 GPU 原始数据，不用截图容差 |
| 每后端比较规模 | 1,125,218,304 个 32 位数据字，包含未覆盖区域；不是独占角色像素数 |
| 源资源完整性 | 27 份唯一纹理中 25 份完整；另有一份部分来源待证的数组及一份缺少低级 mip 的红色小图 |
| 模型保护 | 模型、动画、场景、可见 Player 未由本阶段重建；旧模型字节基线仍为 81/82 |

机器证据：[单阶段验证](stage-verification.json)、[D3D11 对游戏](sequence-comparison.json)、[Unity 对游戏](unity-sequence-comparison.json)。前者验证程序翻译与绑定；后两者直接比较原始游戏的逐 draw 输出，避免只证明两个错误配置彼此相同。

## 从旧资料中实际补全了什么

旧逆向的 45 个引用及当前游戏二进制版本已复核，入口是 [旧资料复用清单](../prior-reverse-reuse.md)。本阶段继续从这些资料指向的原始 block、CAB、pathID 取数，没有重新注入游戏或修改原包。

展示翅膀 `_LightTex` 的原始 11 级 mip 已在 [基础材质报告](../ui-native-material/README.md) 登记。这里新增恢复 `Eff_Matcap_112`，它是 UI 身体第二材质 `_MatCapTex3` 的源引用，映射到 GPU 数组第 4 层。不能因为两份数组捕获到的第 0 层相同，就把战斗数组的其他层直接用到 UI。

新纹理来源：

- block：`984820015.blk`。
- CAB：`CAB-274c2475c7afc2d0721bb3e5433ca1bc`。
- pathID：`1614834203788729912`；名称 `Eff_Matcap_112`。
- BC7、256×256、9 级原始 mip，87,408 字节；源流 SHA-256 为 `7109a1e81a9cbb1427c342cd3f561f07481b3052ad338d0074517870174a0c3f`。
- GPU 数组为 8 层、5 级 mip；第 4 层直接使用对应前 5 级原始压缩数据。完整 9 级原流保留在 `matcap-source-data/`。

完整身份连接和逐材质 CB 索引见 [matcap-array-completion.json](matcap-array-completion.json)。第 0 层有原始捕获字节依据，当前采样的第 1–6 层有 UI 材质 PPtr、external CAB 和捕获 CB 索引依据，并通过两页完整颜色输出核验。第 7 层未被当前绘制使用，保留明确标注的战斗派生候选，不登记为原 UI 来源已确认。

## 如何找出并修正剩余颜色差异

先用诊断 shader 输出实际 matcap 坐标、层号及 LOD，确认身体差异只发生在纹理数组采样分支。随后分别核对资源内容、过滤与寻址方式：

1. 普通表面采样 A 使用双线性过滤、重复寻址；这使翅膀和头发的基础输出与原始抓帧一致。
2. 数组采样 B 使用 **8 倍各向异性过滤、重复寻址**。三线性、2/4/16 倍各向异性，以及边缘钳制／镜像寻址均有独立诊断结果；恢复正确 UI 数组后，8 倍重复寻址使两页身体反射全部逐位吻合。
3. 最后一层头发不是预乘颜色：RGB 和 alpha 都采用 `SrcAlpha / OneMinusSrcAlpha`。抓帧边缘的 alpha 符合 `a*a + (1-a)`，最低约为 0.75；使用 `One` 会把这部分错误地变成 1。修正后完整 48 次绘制零差异。

这证明当前样本所需的采样行为可以重建，**不等于取回原始 `D3D11_SAMPLER_DESC`**。日志中实际存在 A/B/C/D 四个句柄，D 是眼睛 LUT 的额外句柄。C/D、未触发的 LOD／边界条件以及展示 s2 原句柄仍缺少独立证据。低级红色 mip 同样不伪造为原始回收数据。

## 渲染状态与 Unity 实现

目标格式保持为 `RGBAHalf`、`RGBA8 sRGB`、两份 `R10G10B10A2 UNORM` 和 `D32_FLOAT_S8X24_UINT`。每页仅在第一 draw 清空，之后保留深度、模板和颜色。GPU 原始读取不做颜色转换；深度 float32 与 8 位模板经过无损读取后与原始数据比较。

状态生成器保留来源 shader 的动态属性名称，并结合捕获句柄与独立输出检查恢复实际状态。具体包括正文／描边剔除、眼眉模板掩码、生成发丝模板、面部修正的深度偏移和最终头发混合。状态明细见 [sequence-state-candidates.json](sequence-state-candidates.json)；文件名保留候选来源含义，当前所用组合的验收结果以两个 sequence comparison 报告为准。

捕获序列的原生正面约定为 `FrontCounterClockwise=true`；Unity 完整序列不启用反转剔除。旧单阶段夹具采用另一组两端一致的剔除约定，因此仍保留 `SetInvertCulling(true)`；两类测试的目的不同，不能把夹具状态当作游戏原状态。

Unity 的 B 采样器从实际数组纹理取得；检测时对纹理做内存副本并设为 Trilinear、Repeat、anisoLevel=8。这样不会改写导入资产的设置。检查结束恢复全局各向异性设置并释放临时网格、材质、纹理、常量缓冲和渲染目标。

连续审计还修正了检查器的读回内存累积：单个 Editor 调用中反复使用内部存储的 `AsyncGPUReadback.Request`，释放会等到 Editor 返回后续帧，大批量检查曾因此耗尽内存。现在改为 `RequestIntoNativeArray`，完成等待后立即释放调用方持有的数组；基础、单阶段和完整顺序检查已经在同一 Editor 进程连续通过。此问题位于离线检查工具，不是模型或动画资产损坏。

## 文件位置与使用

| 文件／目录 | 用途 |
|---|---|
| [manifest.json](manifest.json) | 48 次绘制的身份、顶点布局、常量、纹理和采样器来源 |
| [full-textures/UI_MatcapArray.dds](full-textures/UI_MatcapArray.dds) | UI 专用派生数组，保留原始 BC7 mip 数据 |
| `E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\CapturedNativeUI\Textures\UI_MatcapArray.asset` | Unity Texture2DArray，独立于战斗数组 |
| `translated/` | 原始汇编／字节码支持的 16 份 HLSL |
| `source-state-evidence.json` | 原序列化 shader 状态及其材质属性名称 |
| `sequence-gpu/`、`unity-sequence-gpu/` | 两个后端的逐 draw 7 数据字原始输出和覆盖索引 |
| `gpu/`、`unity-gpu/` | 单阶段 float32 夹具输出；不能作为完整可见画面使用 |
| `E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Shader\GeneratedNativeUIReplay` | 真正按 24 次顺序使用的 Unity shader 与 include |
| `E:\ZZZ\local-only\RemielleHoyoToon\Assets\Editor\RemielleNativeUISequenceGpuAudit.cs` | 内存绑定、逐 draw 执行和原始 GPU 读回 |
| [../Run-UIFullStageChecks.ps1](../Run-UIFullStageChecks.ps1) | 从派生资源准备到 Unity、D3D11、原游戏比较的完整复现入口 |

关闭 Unity 后，在 PowerShell 中运行：

```powershell
& 'E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-UIFullStageChecks.ps1'
```

脚本使用隐藏的 D3D11 批处理，不代操作游戏，不修改保护／启动器设置，不重建模型或动画，也不重置旧基线。失败即停止；未通过的结果不会登记为验收通过。

## 下一步：把已验证的输入换成实时输入

先映射当前骨架的蒙皮与上一帧变形、相机矩阵、分辨率及角色方向，再由当前模型实际生产这 24 次绘制。然后接上已有的深度／法线辅助层、展示／商店 Deferred、角色 LUT、Bloom 与最终后处理，验证动态姿态和真正可见的输出。

其中来源连接已补上：[实时绑定准备](../ui-live-binding/README.md)。48 次绘制全部对应到当前模型的 7 个网格，使用的静态顶点颜色和 UV 零差异；HairShadow 的既有 91 骨蒙皮可以复用，并明确记录翅膀／头发的原生 UV 输入别名。

附件归属与显隐仍要按原资料核实，未确认的离体小环继续保留。最后生成可用场景和 Player，检查面部光向、动画切换、裁剪与资源释放，**停在接控制器之前**。固定抓帧全部吻合不替代这些动态验收。

Unity API 依据：[独立采样状态](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html)、[模板状态](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-Stencil.html)、[深度偏移](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-Offset.html)。原始游戏行为的判断依据仍是本地原始抓帧和 GPU 对照。
