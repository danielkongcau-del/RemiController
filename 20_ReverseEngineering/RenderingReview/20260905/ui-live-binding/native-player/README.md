# Remielle 原生展示场景与独立程序

本目录记录当前可见相机集成与 Player 验收。以 [verification.json](verification.json) 中的 pass 和构建文件哈希为准；没有通过该报告前，不把构建成功当作运行验收完成。

## 入口

| 用途 | 完整位置 |
|---|---|
| Unity 工程 | E:\ZZZ\local-only\RemielleHoyoToon |
| 原生可见场景 | E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Remielle_NativeUIReview.unity |
| 原生独立程序 | E:\ZZZ\local-only\RemielleRenderingReview\20260905\NativePlayer\RemielleNativeUI.exe |
| 手动启动入口 | E:\ZZZ\local-only\RemielleRenderingReview\20260905\Start-NativeReview.ps1 |
| 顺序重建和验收 | E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-NativeUIPresentationChecks.ps1 |
| Player 图库 | [index.html](index.html) |
| 原始模型 prefab | E:\ZZZ\local-only\RemielleHoyoToon\Assets\V3\Remielle_V3_Animated.prefab |

独立程序需保留整个 NativePlayer 文件夹，不能只复制 exe。所有数据均打包在本地程序中，运行不读取原始抓帧目录，也不调用 AssetDatabase。

Unity 版本为 6000.3.17f1，Built-in、Linear、D3D11。原生相机不重复绘制 HoyoToon 材质；它使用本项目从游戏 shader 恢复并验证的角色链。源模型和旧材质保留，供结构检查和之前的适配路线使用。

## 怎样查看

打开上述新场景进入 Play，或主动运行 Start-NativeReview.ps1。默认使用时装商店配置，1920 × 1080，可调整窗口大小。最终输出、G-buffer、深度、TAA 使用相机实际像素尺寸，未设置低分辨率再放大；Bloom 保留捕获配置的内部多级尺寸，这与最终图像分辨率不同。

预览面板提供角色展示／时装商店切换、15 个原始动作、暂停、默认姿态、全身／面部镜头、镜头方位、主光方向和主光／环境光强度。H 显示或隐藏面板。该面板仅用于外观与动画检查，没有实现移动、攻击、技能或游戏状态机。

Scene 视图的图像效果开启且使用透视相机时，可通过 Unity 的 ImageEffectAllowedInSceneView 机制使用相同效果。关闭图像效果或使用正交编辑视图时，看到的是常规编辑器模型绘制。已验证独立 Scene 类型相机的效果和历史隔离；没有代操作真实编辑器 GUI，不能将这项测试描述成实际窗口操作验收。

## 实现顺序与资源

1. RemielleNativeAnimation 使用原始曲线更新源骨架和模型蒙皮，保留源采样精度和播放速度。
2. 每个相机拥有独立 RemielleNativeUIFrameGraph。它收集当前相机、骨架和上一帧输入，准备 24 次 G-buffer 绘制与后续 Body_1。
3. 展示灯组以当前骨盆位置更新代理点、主光、环境渐变和 Deferred；灯光旋转与颜色强度可独立调整。
4. 绘制四路 G-buffer、深度／模板，再生成半分辨率深度／法线／范围层。
5. 原生角色 Deferred + 1024 × 32 角色 LUT → Body_1 第二材质槽前向混合 → TAA → 13 步 Bloom → 原生最终后处理。
6. OnRenderImage 将最终 sRGB 目标写入可见相机输出，并保留 Unity 要求的目标纹理状态。

运行代码目录：E:\ZZZ\local-only\RemielleHoyoToon\Assets\RenderingReview\Runtime。

新增主要类型为 RemielleNativeUIPresentationProfile、RemielleNativeUIFrameGraph、RemielleNativeUIPresentation 和 RemielleNativeUIReviewControls。两套打包配置位于 Assets/RenderingReview/NativeUILive/Presentation，引用此前已验收的 Geometry、LateBody、Temporal、Post、shader 和 LUT 资产。

每个相机分别拥有历史帧和渲染目标，切换配置、尺寸或指定重置时重新初始化 TAA。关闭／销毁组件会释放私有目标。Game 相机按当前蒙皮顶点自动取景，不靠扩大 Mesh.bounds 或关闭裁剪修正截图。

## 验证方法

编辑器测试覆盖 100 次实际 Camera.Render：1920 × 1080、1280 × 720、1377 × 823，以及第二个 960 × 540 Scene 类型相机。逐字节比较相机实际输出与原生最终颜色，允许最多一个 8 位量化单位；报告记录实际差值。检查独立历史、尺寸、深度辅助层和资源释放。

独立 Player 按正常 Animation / LateUpdate 连续推进所有 15 个动作，每个展示配置各完整播放一次，另做共 30 次渐变切换。只有选定新动作的初始不连续定位使用 Sample，后续没有逐帧手工采样、修改原始曲线或改变播放速度。后台测试设置 1/60 秒的离线帧步进，保留动作原始时长和时间关系。

本机 Unity 无窗口批处理不会自动调度已启用相机，因此验收在 LateUpdate 后调用 Camera.Render 触发真实 OnRenderImage；普通交互使用启用相机的正常渲染调度。该测试不能称为手动操作过窗口。每帧读回最终颜色和深度，检查有限值、像素尺寸、完整取景、原生颜色传递，以及源骨架姿态确实变化。

GPU 全图读回会显著增加验收耗时，不能以该运行速度推断正常交互 FPS。实际交互性能仍需单独测量。

Unity 自身会缓存名为 TempBuffer 的相机中间纹理。本机记录的内部 flags 为 125；本实现分配的私有目标统一为 HideAndDontSave（61）。释放统计按后者归属，前后完整资源清单另保存在 editor/targets-before.json 和 targets-after.json，未删除引擎缓存来通过测试。

## 尚有边界

- 这条展示／商店链使用捕获证据选定的 7 个源网格和 25 道角色绘制，不能等同于战斗全附件、所有游戏场景与特效系统。源 prefab 中其余部件仍保留。
- [辅助消费者复核](../auxiliary-consumers/README.md) 说明原始反射阶段如何排除当前 UI 角色，不代表环境探针或 AO 已全部实现。
- 灯组刚性跟随是本工程明确实现的展示策略，原游戏 CPU 天气／灯光混合程序尚未完整恢复。
- 原始采样器创建描述符和少量未使用 mip／数组层的来源边界，仍保留在上游验证报告中。
- 旧模型受保护字节基线仍为 81/82；历史 prefab 再生成差异没有通过刷新基线掩盖。此次新场景和渲染验收不替代旧基线。
- 构建可能报告已有 shader 翻译的位掩码／未使用输出警告，以及未关联 Unity 云项目的提示；以 build.json 和日志为准，不冒称零警告。

## 技术依据

Unity 官方 [OnRenderImage](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html)、[图像效果目标约定](https://docs.unity3d.com/ru/530/Manual/WritingImageEffects.html)、[SceneView 图像效果](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ImageEffectAllowedInSceneView.html)、[Input System API 对应关系](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/Migration.html)。游戏行为的依据来自本地捕获和原始 shader，官方 Unity 文档仅用于引擎集成约定。
