# 原生 TAA、历史帧与新增后续绘制

展示／商店两套原生 TAA 已接到当前模型、相机与动画输入。当前图集：[六张 1920×1080 对照](temporal-preview/index.html)。这是独立离屏渲染链；正式场景和 Player 尚未替换。24 道已验收工序是 **G-buffer 阶段**，Deferred 之后的 Body_1 第二材质槽现已补上，并通过固定采集和当前 15 动作输入检查；动态光照／辅助层、附件和正式可见 Player 仍未完成。

## 已完成与证据

| 检查 | 结果 | 报告 |
|---|---|---|
| 原始展示、商店 TAA | 两页颜色 RGBA16F 和历史标记 R8 均与原始 VS／PS 独立执行及采集输出逐位一致；每后端 33,488,640 个值 | [捕获验证](native-taa/verification.json) |
| 当前模型连续帧 | 已包含后续 Body_1 绘制，两页各 24 帧，共 48/48 与原始字节码同输入结果精确一致，比较 62,784,000 个值 | [动态验证](live-temporal/verification.json) |
| 历史帧连接 | 42 次颜色／标记延续匹配；6 次首帧、改尺寸或跳转重置匹配明确的本地初始化策略 | 同上 |
| 相机抖动 | 静止镜头的未抖动当前／上一帧矩阵不变；实际光栅化投影按当前像素偏移变化 | 同上 |
| 生命周期 | 两套 24 帧检查结束后私有 RenderTexture 泄漏为 0 | 同上 |
| 高清集成 | 六张图，每张固定姿态累积 16 帧，49,766,400 个 HDR 通道值有限，覆盖范围处于图内 | [高清验证](temporal-preview/verification.json) |

静止镜头的后八对采样帧中，TAA 输出的平均帧差约为原始抖动 HDR 的 6.26%／6.19%，即下降约 94%。这只描述本次静止抖动测试，不代表所有动作的拖影、细节和遮挡都已验收。高清图使用固定姿态预热，连续动画由独立 48 帧检查覆盖；完整 15 动作 Player 验收仍待完成。

此前受修改影响的 96 组顶点、144 道三角形、6 组 Deferred、两页 Bloom／最终后处理以及六张无 TAA 最终颜色门禁已重新运行并通过。旧模型基线仍为 81/82，已有 prefab 再生成事件单独保留，本轮不重新生成正式模型或动画、不改写基线。

## 原始来源和输入含义

PS 为 `78d237bfcf946e6e`，VS 为 `c85e3fc3d2f75d34`。展示 draw 106、商店 draw 107 的原始字节码、HLSL、常量、纹理以及 SHA-256 在 [源清单](native-taa/manifest.json)；实际上下游纹理的逐字节关系在 [输入来源追踪](native-taa/lineage.json)。

| 槽 | 内容 | 当前生产者 |
|---|---|---|
| t0 | 当前深度 | 几何深度复制为可采样 R32，避免 DSV／SRV 重叠 |
| t1 | R10G10B10A2 运动、实体标记和 TAA 控制位 | **Deferred 的 Auxiliary**，不能使用更早的几何 o2 |
| t2 | TAA 之前的 HDR | 当前角色 Deferred／LUT，再叠加后续 Body_1 绘制 |
| t3 | 前一帧 TAA 颜色 RGBA16F | 私有颜色双缓冲 |
| t4 | 前一帧标记 R8 | 私有标记双缓冲；不是历史深度 |

原始 shader 从近邻深度选择运动来源，解码运动、比较当前和历史标记、限制历史颜色范围并按运动混合。两路输出同时生成当前颜色与下一帧所需标记。只绑定实际消费的 cb0 和 t0–t4；捕获中的额外槽是旧绑定残留。

角色几何使用抖动投影，但运动使用当前及上一帧未抖动投影，防止把采样偏移当作物体运动。捕获第 4／10 号样本的偏移与 `Halton(index+1, 2/3)-0.5` 一致。目前顺序推进样本；游戏循环长度未恢复，不擅自套用其他 Unity 版本的 8 帧周期。

首次使用、改尺寸和明确跳转时，运行时以当前 HDR 和当前标记初始化历史，再执行原始 TAA。**这是本地明确的重置策略，游戏历史缓冲最初如何清空尚无采集证据。** 原始 `_IsFirstFrame` 的含义按原 shader 保留，不将它误当作会自动清空历史的开关。

Bloom 从 TAA 之前的 HDR 计算，最终 UberPost 同时接 TAA 颜色与 Bloom。不能把 TAA 输出也用作 Bloom 源，否则会改变原始顺序。最终颜色目标保持 sRGB 写入，HDR／历史目标保持线性。

## 资产位置与使用

工程根目录：`E:\ZZZ\local-only\RemielleHoyoToon`。

- 配置：`Assets/RenderingReview/NativeUILive/Temporal/display.asset`、`store.asset`，只保存原始常量模板和 shader 引用，不保存游戏历史画面。
- 运行时代码：`Assets/RenderingReview/Runtime/RemielleNativeUITemporal.cs`、`RemielleNativeUITemporalProfile.cs`。
- 原始计算入口：`Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUITemporal.shader`。
- 后台验证：`Assets/Editor/RemielleUITemporalCaptureAudit.cs`、`RemielleUILiveTemporalAudit.cs`、`RemielleUITemporalPreviewBuild.cs`。

每帧在动画求值之后按以下顺序调用；同一个实例不能在完成 Render 前再次 BeginFrame：

```csharp
Vector2 jitter = temporal.BeginFrame(width, height, resetHistory);
geometry.Prepare(width, height, jitter);
late.Prepare(geometry, nativeAnimation);
geometry.Render();
lighting.Render(geometry, camera);
late.Draw(lighting.Hdr, geometry.Depth);
temporal.Render(lighting.SampledDepth, lighting.Auxiliary, lighting.Hdr);
post.Render(lighting.Hdr, temporal.Color);
RenderTexture finalColor = post.FinalColor;
```

geometry、lighting、late、temporal、post 均需 Dispose。late.Prepare 必须早于 geometry.Render，因为几何 Render 结束时会提交上一帧状态。切换配置／瞬移／任意时间跳转时，调用者应明确请求历史重置。分辨率变化会自动重建目标并重置历史。当前类是渲染单元，尚不是最终场景组件或角色控制器。

准备脚本：`E:\ZZZ\local-only\RemielleRenderingReview\20260905\prepare_ui_taa.py`。配置构建入口 `RemielleUITemporalCaptureAudit.Build`。后续 Body_1 先运行同目录 `Run-UILateBodyChecks.ps1` 和 `Run-UILateBodyUnityChecks.ps1` 完成来源核验、适配 shader／配置及动态 GPU 对照。关闭该工程后运行同目录 `Run-UITemporalChecks.ps1`，顺序执行 Unity D3D11 捕获、48 帧实时检查、高清图生成、独立原始字节码执行及验证。`Run-UIFullStageChecks.ps1` 已接入该门禁。所有执行均为本地离线，不启动或注入游戏。

## 新发现：Deferred 后的 Body_1 第二材质槽

展示 draw 92、商店 draw 93 使用 `NapAvatarUITransparent`：VS `1f6ab42231416fdb`、PS `345d8de4fe86facc`。原始字节码及捕获资源可用。它对应 Body_1 子网格 1，firstIndex 57612、indexCount 1014，即 338 个三角形。其 IB、VB0／1／3 与此前 Body_1 G-buffer 捕获逐字节相同，索引片段与以下源 Mesh 相同：

- block `261957958`，CAB `CAB-060271f061d155c881cf052414809d4d`，pathID `7841027147514059888`。
- renderer 所属 block `2906705493`，CAB `CAB-a54cb8ece006bf575b732cd3ab36fd55`，pathID `3586499500754002266`。
- 材质槽 1 的外部指针为 fileID 12、pathID `-7413843588448389930`；必须按该 renderer 的来源解析 external 表，不按同名材质猜测。

[来源准备报告](late-body/source-preparation.json) 的 pass 只表示以上来源和字节校验通过，`runtimeImplemented=false`、`gpuPassVerified=false`。随后完成的独立 [前向合成验证](late-body/verification.json) 已证明：两页各自完整 HDR 的原始／翻译结果都与采集逐位相同，每后端两页合计比较 26,790,912 个值。原材质、UI 源材质及 shader 的限定身份也已解析，详见 [后续绘制说明](late-body/README.md)。新增 [Unity 固定采集验证](late-body/unity-verification.json) 精确通过；[当前模型验证](live-late-body/verification.json) 的两页各 15 个主动作共 30 组输入、每后端 62,208,000 个半精度值精确一致，深度、alpha 和运动辅助目标不变。随后重跑的 48 帧 TAA 及六张高清图均包含该道绘制。来源准备报告的旧阶段字段不能代替新的验证报告。

在原始 TAA 当前颜色的追踪中，该道之前／之后在角色区域分别有 76,420／32,836 个 RGB 值变化，alpha 没变；这些数量是两页采集的通道值差，不是该子网格的像素数。具体到更早的全场景中间目标，不能仅凭内容相似认定是同一个渲染目标。

半分辨率深度／法线／范围生产单元也已完成固定采集与当前模型检查，详见 [来源与调用](depth-hierarchy/README.md)。它尚未接到最终场景和辅助消费者；不能替代本节 TAA 的全分辨率深度与运动输入。

其余待完成内容：动态灯光／辅助消费者、附件归属与显隐、所有主动作的可见连续帧、正式场景及 Player。未确认的小环和源资产继续保留；原始采样器描述符等缺失来源继续明确记录。持续目标仍进行中，停在接控制器之前。
