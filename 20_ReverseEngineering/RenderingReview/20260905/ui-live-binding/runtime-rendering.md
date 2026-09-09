# 当前模型的原生实时绘制、光照与最终颜色

2026-09-06：原生展示／商店新场景和 NativePlayer 已通过 100 帧编辑器相机检查、8,636 帧实际 Player 动画推进（30 个完整动作片段、30 次渐变切换），零颜色传递差异，私有目标释放完整。后台测试在正常 Animation / LateUpdate 后调度 Camera.Render，普通交互使用启用相机；不冒称已代操作 GUI 或测量交互性能。已从旧捕获核实反射消费者排除 UI 角色，并恢复 486 个节点、27 个 renderer 的源默认状态和根骨父链。附件事件与离体小环归属仍待推进；原始采样器／CPU 灯光边界保留，旧模型基线 81/82，未接控制器。

本轮复用了已有逆向结果，已经将展示／商店两套各 24 次 G-buffer 绘制、角色 Deferred／LUT、后续 Body_1 第二材质槽、13 道原生 Bloom 和完整 UI UberPost 接到当前 Unity 模型。最新 TAA 图集在 [连续采样最终颜色](temporal-preview/index.html)，[TAA 与新增 Body_1 工序](temporal-rendering.md) 说明当前状态；此前无 TAA 图集在 [原生最终颜色](live-post/index.html)，保留 [HDR 中间图](live-lighting/index.html) 便于分阶段检查。新原生可见场景和 NativePlayer 现已通过 8,636 帧连续检查，详见 [可见入口与方法](native-player/README.md) 和 [实际 Player 图库](native-player/index.html)。附件事件继续推进，目标仍停在接控制器之前。

## 已验证的范围

- 两套页面、六个模型／相机状态，共 144 次有顺序的实际 Unity 绘制。每一道都与独立 D3D11 的同输入结果比较，其中 138 道使用原始 DXBC，6 道发丝阴影使用已与捕获输出对证的汇编恢复程序，四路原生颜色、深度、模板均逐位相同，共比较 522,547,200 个 32 位数据字，零差异。这个数量包含未覆盖像素和每道工序后重复的缓冲区状态，不是独立的可见像素数。
- 检查镜头包含正、侧、背面，动画包括当前原生动作，上一帧顶点和矩阵在完整绘制链结束后提交。取景由实际 BakeMesh 顶点计算，避免移动动作离开固定相机，也避免同帧 SMR bounds 未刷新的误判。
- 当前原生 G-buffer 已接角色 Deferred 与来源明确的角色 LUT，六张 1920×1080 HDR 图的 49,766,400 个通道值均有限，完整模型覆盖范围处于图像内部。新增六组默认／Idle_Loop／Walk_Start、正／侧／背面、不同尺寸和近裁剪面输入；与原始 Deferred DXBC 比较的 2,320,085 个角色 HDR／辅助值逐位一致，模板范围外保持全零。
- 展示、商店的完整 13 道 Bloom 链，最终 821,006 个 R11G11B10 数据字与捕获输出相同。完整 UberPost 的原始 VS／PS、原始四顶点网格和索引顺序均有来源；独立 D3D11 与 Unity 每后端各比较 26,790,912 个最终 sRGB 字节，零差异。
- 当前模型的六张 1920×1080 最终成图，包含当前原生几何 → Deferred／角色 LUT → Bloom → 完整 UberPost。最终原始 VS／PS 对同一实时输入独立执行，49,766,400 个输出字节逐位一致。没有用捕获截图替代当前人物，也没有将离线颜色输出当成 TAA／动态光照已恢复。
- 原始模型、动画、骨骼、HoyoToon 材质、正式 prefab 和旧基线未在本轮重新生成。旧模型基线的 81/82 事件仍然保留，不用新哈希掩盖它。

查看 [最终成图](live-post/index.html)、[实时颜色验证](live-post/verification.json)、[动态 Deferred](live-deferred-gpu/verification.json)、[捕获 Bloom／最终后处理](native-post/verification.json)、[动态三角形验证](live-raster/verification.json)。

## 资产与代码入口

以下工程路径相对于 `E:\ZZZ\local-only\RemielleHoyoToon`。

| 路径 | 用途 |
|---|---|
| `Assets/RenderingReview/NativeUILive/display.asset`、`store.asset` | 两个独立运行时数据包，包含 24 道工序、常量模板、贴图／结构化资源和来源身份 |
| 同目录 `Meshes` | 原始顺序索引和消费过的静态颜色／UV；运行时只在私有副本写入当前蒙皮位置、法线、切线及前一帧位置 |
| 同目录 `Textures` | 原本只有 DDS 的数据图和 LUT 的 Unity 资源；已有正式来源贴图仍通过引用复用 |
| `Assets/RenderingReview/Runtime/RemielleNativeUIProfile.cs` | 可序列化的来源、材质输入和绘制定义 |
| `Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs` | 原生 24 道绘制和 GPU 资源生命周期，依赖同目录已验证的 MeshBinding／Constants |
| `Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs` | UI 角色 Deferred 连接；接收当前原生 MRT、深度、捕获常量与角色 LUT |
| `Assets/RenderingReview/NativeUILive/Post/display.asset`、`store.asset` | 两套最终后处理配置；只包含原始静态参数、网格、噪声／扭曲／污渍资源，不存游戏画面 |
| `Assets/RenderingReview/Runtime/RemielleNativeUIPost.cs`、`RemielleNativeUIPostProfile.cs` | 13 道原生 Bloom 和完整 UberPost；管理私有 R11／sRGB 目标和状态恢复 |
| `Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIFinalPost.shader` | 完整原始 UI UberPost 的 Unity 适配入口 |
| `Assets/Editor/RemielleUILiveProfileBuild.cs` | 从已有来源清单构建两个数据包；构建前检查原始资源 SHA-256 |
| `Assets/Editor/RemielleUILiveRasterAudit.cs`、`Assets/Editor/RemielleUILiveLightingAudit.cs` | 后台模型实例、动态输入导出、真实 GPU 读回；不保存模型或场景 |
| `Assets/Editor/RemielleUINativePostBuild.cs`、`RemielleUINativePostAudit.cs` | 后处理配置构建及原始两页完整链的 GPU 对照 |
| `Assets/Editor/RemielleUILiveDeferredGpuAudit.cs`、`RemielleUILivePostAudit.cs` | 当前输入导出、六组动态光照与六张高清最终图的 GPU 对照 |

使用 `RemielleNativeUIRenderer(profile, nativeAnimation, camera)` 创建独立渲染实例，无 TAA 的分阶段检查可在每帧动画求值后调用 `Prepare(width,height)` 和 `Render()`，再调用 `RemielleNativeUILighting.Render(renderer,camera)`、`RemielleNativeUIPost.Render(lighting.Hdr)`，最终纹理是 `post.FinalColor`。这段旧链仅用于此前分阶段报告；当前完整颜色链还须插入 late.Prepare／late.Draw。各单元均需 Dispose。实际 TAA 顺序与历史管理见 [使用说明](temporal-rendering.md)，其中 Bloom 接原 HDR、UberPost 接 TAA 颜色。最后一张纹理本身已采用 sRGB 存储，不能再次手动 Gamma。独立查看器接入时，还需正确设置相机输出与资源释放；目前不要将这些类误当成已替换正式 Player。

构建数据包入口是 `RemielleUILiveProfileBuild.Run`。重复检查入口为 `E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-UILiveRasterChecks.ps1`；需关闭该 Unity 工程，脚本以 D3D11 后台执行，不启动游戏或控制桌面。该脚本使用已经存在的数据包；修改捕获输入或准备规则后，应先独立核验再重新构建数据包。

新后处理源准备：`prepare_ui_final_post.py`；配置构建：`RemielleUINativePostBuild.Run`。新增顺序验收入口是同目录 `Run-UILiveResolveChecks.ps1`，执行动态 Deferred、两页捕获 Bloom／UberPost、HDR 和实时最终颜色检查。`NativeFullscreenReplay.cpp`／`bin/NativeFullscreenReplay.exe` 是离线独立 D3D11 原始字节码执行器，完全不注入游戏。采集来源、原始格式、输入 SHA-256 和报告都在 `ui-live-binding/native-post`、`live-deferred-gpu`、`live-post`。

## 本轮处理的方法

7 个网格保留 source block + CAB + pathID；运行时绑定还要求实际导入 Mesh 引用匹配。原生 shader 的对象坐标来自各自原始 `m_RootBone`，不能用组装时统一的 Pelvis rootBone 代替。

四个 MRT 格式沿用原生布局：RGBA16F、RGBA8 sRGB、两个 R10G10B10A2，加 D32/S8。采样、描边、面部模板修正、眼睛、发丝阴影和后续透明发丝按已验证的 24 道原顺序执行。原始捕获资源不改写；私有 GPU 资源在实例销毁时释放。

除已恢复的相机、对象、头部矩阵等命名常量外，发丝阴影的汇编专用 VS 按明确的寄存器读数更新相机和对象矩阵。头部球形法线中心先从捕获头骨矩阵还原局部偏移，再随当前头骨移动，半径保留原值。

Deferred 的 `unity_WorldToCamera` 与几何阶段 `unity_MatrixV` 的第三行符号不同；捕获中的两个矩阵及 shader reflection 可直接对证。新连接使用 `diag(1,1,-1) * view`，并按列写入。早期 `RemielleNativeDeferredProbe` 仍是旧的离屏近似入口，不能据此宣称它的任意视角与新原生链已经等价。

新增独立 GPU 对照查出：同一深度纹理同时绑定为模板测试目标与像素采样输入，会造成采样冲突，部分 HDR 像素偏暗。现在保留原深度／模板目标，另外将深度复制到 R32 采样纹理；每个像素的复制位型都经过校验，修正后六组光照输出完全一致。[D3D11 文档](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-pssetshaderresources) 说明了重叠输出资源对 SRV 绑定的影响。

Bloom 必须保留原生 R11G11B10 过滤采样。在本机，即使展开后的 RGBA32F 逐纹素数值相同，过滤也会产生不同的末位结果；两页实验分别引起 14,945／10,902 个最终字节相差 1。将捕获夹具恢复到原生 R11 目标并验证全部上传位型后，差异归零。运行时所有 Bloom 中间目标也使用 R11，最后合成不走旧预览用的部分公式。

完整 UI UberPost 使用原始 PS `6443f79de027d780` 与 VS `ce43e302fadd1328`，并保留原始四顶点／六索引。最初用全屏大三角形时，两页分别有 31／28 字节末位差异；恢复原始网格顺序后归零。最后输出显式恢复 [Unity GL.sRGBWrite](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GL-sRGBWrite.html)，避免 Bloom 浮点目标留下的禁用状态使最终画面偏暗。这里采样的是角色 Deferred 已处理的颜色；该 UI UberPost 不读取独立 4096×64 LUT，因此不能额外叠加旧后处理 LUT。

PNG 导出遵循 [Unity SetPixels32 的数组行顺序](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Texture2D.SetPixels32.html)，避免额外翻转。原生 RenderTexture 投影保留游戏中已有的 Y 约定；不能通过倒置模型修复图像文件的方向。通过 [CommandBuffer.DrawMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.CommandBuffer.DrawMesh.html) 手动绘制时，光照输入由已恢复的数据显式绑定。

## 还没有完成的部分

1. 动态灯光、角色环境渐变、阴影等生产逻辑；当前使用捕获预设，不能把通道逐位一致解释成任意游戏场景光照已经恢复。
2. 半分辨率深度／法线／范围生产单元已通过固定采集和六组当前输入检查，详见 [辅助层说明](depth-hierarchy/README.md)；还需连接辅助消费者、产出正式可见场景和 Player。后续 Body_1 的 338 个三角形已通过 Unity 固定捕获及 30 个当前动作姿态检查。TAA／历史帧已经接入并通过 48 帧同输入原始字节码对照；游戏首帧缓冲初始化与样本循环长度仍无直接来源。
3. 对完整 15 个动作的实时绘制、连续帧、尺寸变化及资源生命周期做最终验证；G-buffer 门禁为六个具体状态，后续 Body_1 另覆盖 15 动作各一个姿态；仍需可见 Player 的连续帧验证。
4. 核实另外 20 个组件与附件状态。当前 7 个网格对应两个 UI 捕获的 24 道 G-buffer 绘制；后续 Body_1 仍使用这 7 个网格之一；捕获中未出现的原始部件没有被删除。
5. 四个原始 sampler 描述符、红色小纹理的两级 mip、未使用数组第 7 层仍有来源边界。汇编恢复的发丝阴影尚无原始 DXBC；深度透明渐隐的 Bayer 常量精确位型与活跃分支仍待专门核验。
6. Bloom 中间尺寸目前保留原始采集值，只有首道源纹素偏移随当前分辨率更新。应在最终查看器明确分辨率策略。Bloom 最后合成 VS `7ce3d6948d7a44f8` 当前依赖捕获汇编的 UV 恢复，完整链已有采集结果对证，不能声称它也已有原始 DXBC。

下一步继续完成上述渲染连接与验证，停在接角色控制器之前。

## 2026-09-06 原生灯组

可选的 RemielleNativeUILightRig 已提供角色代理点、环境光渐变、主光方向／颜色与环境强度的本帧更新。先执行 geometry.Prepare 和 late.Prepare，再 Apply 灯组，最后绘制；Deferred.Render 的第三参数必须传入同一个灯组。原始捕获模板与其他实体保持不变。详见 [动态灯光来源与用法](light-rig/README.md)、[1080p 对照](light-rig/preview/index.html)。此刚性展示策略没有冒称原游戏 CPU 灯光选择／混合代码；正式可见 Player 和辅助消费者仍待连接。
