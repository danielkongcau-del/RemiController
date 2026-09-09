# 实时 UI 输入：来源、骨骼坐标与 GPU 验证

**最新：原生 TAA 与历史帧已接入。** 两页采集的颜色／历史标记逐位匹配；当前模型 48 个连续帧的 62,784,000 个值与原始字节码同输入结果一致，42 次历史延续、6 次初始化／重置及资源释放通过。已生成六张累积 16 帧的 1920×1080 图。追踪输入又确认 Deferred 后还有 Body_1 第二材质槽的 338 个三角形需要补绘；原始几何和 VS／PS 均在本地，独立前向合成现已与两页捕获逐位一致，Unity 集成待完成。此前 24 道工序只代表 G-buffer 阶段。动态光照／辅助层、附件、全动作最终验收和正式 Player 仍在推进，旧模型基线保留 81/82，尚未接控制器。 [高清对照](temporal-preview/index.html) · [资产、方法与边界](temporal-rendering.md)。

**最新：原生实时绘制与 HDR 连接已完成第一轮检查。** 当前模型的展示／商店 24 道工序已接入动态骨架和相机，144 次实际三角形绘制与原始 DXBC／捕获汇编参考执行的同输入输出逐位一致，比较 522,547,200 个 32 位数据字、零差异；另生成 6 张 1920×1080 角色 Deferred／LUT 中间图。变化中的灯光／阴影、Bloom／最终后处理、附件和可见 Player 仍待完成；尚未接控制器。 [详见本轮方法与剩余范围](runtime-rendering.md)。

两页完整角色绘制的 [固定输入 GPU 对照](../ui-full-sequence/README.md) 已通过。本目录开始把这些绘制连接到当前模型，尚未把捕获姿态冒充成实时动画。

[mesh-bindings.json](mesh-bindings.json) 对 48 次 draw 的实际索引范围逐项匹配，全部唯一对应到正式来源选择中的 7 个网格。所消费的静态顶点颜色与 UV 在原生格式量化后零差异；动态位置、法线、切线和上一帧位置留给当前骨架生产。

| 网格 | 来源 block | CAB | pathID |
|---|---|---|---|
| Remielle_Wings | 3400898960 | CAB-de1e7bf1f931a82616ab5af638131c54 | 5848914197253234693 |
| Remielle_Hair | 2583518027 | CAB-9fc8bde953e0cc5bedb5d13203291759 | 5315158174618627359 |
| Remielle_Origin_Body_2 | 261957958 | CAB-f2d1bd42ab339e59b64c2b631135a868 | -7179018126605340138 |
| Remielle_Origin_Body_1 | 261957958 | CAB-060271f061d155c881cf052414809d4d | 7841027147514059888 |
| Remielle_Face | 2583518027 | CAB-37f6f5c28b11ac3772f027ddc6c1e1d1 | 2656821902131795392 |
| Remielle_Eyebrow | 2583518027 | CAB-52bab030ec299bba1e5d7703911863f3 | 1489112290284175769 |
| Remielle_HairShadow | 261957958 | CAB-0548582bffe6187b999c9acb5b2dc614 | 8845268117021823249 |

原始目录探针在 `source-catalogs/`。身份由 source block+CAB+pathID 组成；不能只按名称、单独 CAB 或 pathID 匹配。表中是源 Mesh 身份，原游戏运行时的 Mesh 指针仍未捕获。

`Remielle_HairShadow` 对应此前“生成发丝深度”draw 的 1,974 个顶点和 11,277 个索引，已有 91 根骨骼的源蒙皮和当前 Unity GLB。缺失的是该运行时 shader 的原始字节码，网格本身已有可复用来源。这里保留旧来源表中 `capturedGeometryVerified=false` 的历史状态，只新增本次独立证据，不改写旧基线。

还有一项实时桥接必须保留的约定：翅膀和头发的原生输入布局把不存在的 TEXCOORD3 指向 TEXCOORD2 的同一段数据。共 18 次绘制实际消费这一映射，报告将其记为 `inputLayoutAlias=m_UV2`。不能在实时 shader 输入中简单补零。Body_1/Body_2 的真实第 4 组 UV 保持各自原始来源；头发第 2 组使用已有捕获来源的派生版本。

当前展示样本只涉及这 7 个网格及相应子网格，不代表可以从完整模型中删除其他组件。其余部件应根据展示、战斗和技能的显隐依据处理。

复现入口：`E:\ZZZ\local-only\RemielleRenderingReview\20260905\prepare_ui_live_mesh_bindings.py`。它读取当前来源、原始目录、捕获缓冲及已有头发派生记录，只输出绑定清单。

当前已经实现以下动态输入与顶点阶段门禁。完整 24 次实时绘制、像素光照及最终可见链仍在下一步；控制器不在本阶段接入。


## 已完成的动态输入

[原始根骨绑定](native-root-bindings.json) 从原 renderer 的 `m_RootBone` 指针回到同一个 source block/CAB 的原始 Transform。正式组装模型的公共 `SMR.rootBone` 均为 Pelvis，这是有效的蒙皮／边界设置；它不等于原生 shader 的对象坐标。原生的翅膀用 Spine，头发与 HairShadow 用 Spine2，Body_1/2 用 Pelvis，Face/Eyebrow 用 Head。运行时桥使用已验证原生动画驱动中的对应 source Transform，不改写正式模型的蒙皮根。

`Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs` 读取当前 `BakeMesh(false)` 的位置、法线和切线，并转换到上述原始根骨空间。令 `R` 为 SMR 的局部到世界矩阵，`N` 为原始根骨到世界矩阵，则位置使用 `N⁻¹R`，法线使用其逆转置，切线使用其线性部分并保留手性。每次完整绘制结束后才提交上一帧位置与 `N`，不会在每个 Pass 之间提前滚动历史。静态颜色和 UV 来自已验证的原生输入模板。

[模型输入核验](pose-input-verification.json) 覆盖 7 个网格、绑定姿势与 15 个现有原生动作，共 112 个网格姿态。顶点编号与源位置零差异；11 个导入子网格均为保持绕序的循环角点排列 `[2,0,1]`。独立 float64 检查的最大世界位置差约 1.77 微米，法线与切线分量差低于 2.3×10⁻⁷；706,800 个上一帧顶点记录保持精确对应。没有重建网格、动画或 prefab。

## 常量及实际 GPU 门禁

[原生常量表](uniform-contract.json) 从 14 份准确 shader 记录恢复 638 个命名字段，逐项覆盖 2,145 个字面分量读取。旧反射辅助工具的 `sizeBytes` 漏计向量数组；本表根据原始 `w4` 数组长度单独补正 9 处范围，包括 16 个 float4 的 `_AmbientLights` 和 4 组 5 项 matcap 参数。旧原始记录不被覆盖。

`Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs` 按命名偏移写入当前／上一帧对象与相机矩阵、分辨率、世界相机位置、运动开关和相关头部字段。材料参数与暂未接动态生产器的灯光参数保留采集字节。后续像素、描边和眼睛需要另验各自实际使用的字段，不能由本轮两个基础 VS 的成功推断全部 638 项已动态实现。

[实际顶点阶段验证](vertex-gpu-verification.json) 使用当前骨骼和表情输出，覆盖 96 组输入、6 个模型网格、两个基础／面部 VS、绑定姿势与 15 个动作。相机角度为 −70°、0°、70°、180°，分辨率为 960×540／640×480，视野为 30°／38°／46°。独立 D3D11 的原始游戏 DXBC、翻译 VS 与实际 Unity 顶点阶段的 **28,040,256 个有效 float32 输出逐位一致**，当前／上一帧投影逐位一致。两处缺失字节码的运行时生成 shader 不在这个新动态门禁内。

Unity 顶点结果通过禁用插值的点与像素 UAV 原样读回；这检查真正的 vertex stage。可选的 compute 诊断曾在 Walk_Start 的面部角度上出现 1 ULP 差异，不能拿它代替顶点阶段验收。探针仅按原始 DXBC 输出签名收窄未定义 `.w` 分量，不更改已验收 VS 指令，也不把未定义分量计入对照。

## 重复执行

关闭正式 Unity 工程后运行 `E:\ZZZ\local-only\RemielleRenderingReview\20260905\Run-UILiveInputChecks.ps1`。它顺序重建本目录合同、在隐藏 D3D11 Unity 中读取已保存模型、执行当前姿态和实际顶点阶段检查，再用原始 DXBC 独立复核。不会启动游戏或重建模型、动画、可见 Player。完整原游戏抓帧 48 次绘制门禁仍由 `Run-UIFullStageChecks.ps1` 管理。

API 约定来自 [Unity BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) 和 [GPU 投影矩阵](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GL.GetGPUProjectionMatrix.html)；本项目的骨骼身份、绕序与数值结论以以上本地证据为准。

## 后续工作边界

还需把这套动态输入用于每页完整 24 次实际三角形绘制，补齐其余四种 VS 和像素阶段的头部／灯光／阴影字段，再连半分辨率辅助层、Deferred、LUT、Bloom 和最终可见相机。当前可见 Player 仍是此前 HoyoToon 适配版本。本轮顶点阶段通过不代表已经交付完整实时原生渲染；附件显隐与正式 Player 验收也仍待推进。旧模型基线继续保留 81/82。

`unity-live-vertex-stage/` 与 `unity-live-vertex-stage.json` 是本轮正式顶点阶段结果；`unity-live-vertex/` 与同名 JSON 保留此前 compute 诊断，仅作研究记录，不是当前渲染验收。
