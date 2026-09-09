# Remielle 渲染与光照：第六阶段对齐

2026-09-06 附件补全：离体小环已由 27 次隔离 GPU 编号绘制唯一确认为 Remielle_Floater_02（2,216 顶点、8 根骨、259 个像素）。八根原始骨骼 PPtr 顺序吻合；15 动作的 20,160 条相关缩放曲线、1,703,760 个键与封存 RANI 输入逐位一致。52 份旧逆向技能文件重新校验，恢复 113 条显示／材质／抖动淡出／缩放动作，所有具名 renderer 目标均已匹配。当前原生展示链本就不绘制 Floater_02，源资产继续保留。标签竞争、modifier 触发与生命周期语义尚需核实，未接控制器；旧模型基线 81/82 不变。 [归属、事件来源与使用方法](ui-live-binding/accessories/visibility-contract.md)。

2026-09-06：原生展示／商店新场景和 NativePlayer 已通过 100 帧编辑器相机检查、8,636 帧实际 Player 动画推进（30 个完整动作片段、30 次渐变切换），零颜色传递差异，私有目标释放完整。后台测试在正常 Animation / LateUpdate 后调度 Camera.Render，普通交互使用启用相机；不冒称已代操作 GUI 或测量交互性能。已从旧捕获核实反射消费者排除 UI 角色，并恢复 486 个节点、27 个 renderer 的源默认状态和根骨父链。附件事件与离体小环归属仍待推进；原始采样器／CPU 灯光边界保留，旧模型基线 81/82，未接控制器。

查看 E:\ZZZ\local-only\RemielleRenderingReview\20260905\ui-live-binding\native-player\README.md 与 index.html。

**2026-09-06 最新：原生展示／商店灯组已随角色更新。** 主光方向／颜色、两个角色灯光代理点、环境光渐变和 Deferred 同步更新，支持独立转灯与强度变化。240 次当前 G-buffer 绘制的 870,912,000 个打包值、10 组 Deferred 的 2,484,555 个输出值，以及 10 组后续 Body_1 每后端 20,736,000 个半精度值与原始 shader 相同输入逐位一致。灯组的刚性跟随是明确的展示实现，原游戏 CPU 天气／灯光混合逻辑尚未完整恢复。 [十张 1080p 灯光对照](ui-live-binding/light-rig/preview/index.html) · [来源、使用及验证边界](ui-live-binding/light-rig/README.md)。正式原生可见场景／Player、辅助消费者、附件及全动作连续帧仍待完成，尚未接控制器；旧模型基线仍为 81/82。

**前一阶段：后续 Body_1 已接入当前模型与 TAA 颜色链。** 两页固定采集在 Unity 中精确一致；15 个主动作 × 两页共 30 组当前输入、每后端 62,208,000 个半精度值与原始 shader 逐位相同，深度／模板、alpha 和运动辅助目标不变，私有目标泄漏为 0。修复了动态检查发现的整数布尔标志反编译为零的问题。48 帧 TAA、42 次历史延续、6 次初始化／重置与六张 1920×1080 高清图已重跑，均包含这道绘制。动态光照／辅助层、附件、全动作连续 Player 和正式可见场景仍在推进；旧模型基线 81/82，尚未接控制器。 [高清对照](ui-live-binding/temporal-preview/index.html) · [后续绘制来源与验证](ui-live-binding/late-body/README.md) · [运行顺序与边界](ui-live-binding/temporal-rendering.md)。

**新增：深度／法线辅助层可随当前模型生成。** 两页固定捕获的 10,046,592 个值逐位一致；六组当前模型、镜头、尺寸及裁剪距离检查通过，私有目标无泄漏。下一层 mip 只有原始汇编依据；动态倒数与 CPU 的浮点差异单独记录，不能称作原始字节码对照。后续辅助消费者与正式 Player 仍待接入。[来源、验证与使用](ui-live-binding/depth-hierarchy/README.md)。

**此前阶段：当前骨架与原生顶点阶段已接通。** 已按原 renderer 的根骨指针恢复 7 个网格的对象坐标，读取 15 个原生动作和绑定姿势；两种基础／面部 VS 在 96 组动态相机输入中的 28,040,256 个有效 float32 输出，与实际 Unity 顶点阶段逐位一致，上一帧投影零差异。638 个原始常量字段已建立命名合同。详见 [动态输入、方法与验证边界](ui-live-binding/README.md)。完整实时三角形绘制、像素光照、附件和可见 Player 仍在推进，尚未接控制器。

**此前阶段：展示／商店完整角色绘制。** 两页各 24 次绘制在独立 D3D11 和 Unity 中均与游戏原始抓帧逐位一致：四路颜色、深度、模板全部通过，每后端比较 1,125,218,304 个 32 位数据字。新恢复 UI 专用反射数组层，确认当前样本需要 A 双线性重复、B 8 倍各向异性重复，并修正最终发丝的透明混合。详见 [资产、实现、状态与边界](ui-full-sequence/README.md)。这仍是固定抓帧输入；实时骨架／相机绑定、附件核实和可见 Player 集成继续推进。四份原始 sampler 描述符、未使用数组层及红色小图低级 mip 保留未知记录。旧模型基线仍为 81/82。

此前战斗路径：**Unity 固定输入绑定已通过**。19 组捕获绘制、155,305 像素、2,484,880 个 float32 值与原始 DXBC 逐位一致；修正了捕获几何的正反面约定、比较采样器条件和短常量缓冲上传。见 [Unity 绑定报告](unity-native-material-binding.md)。三个原始游戏 sampler 描述符与每帧动态输入仍未补齐，可见 Player 未重建。

此前战斗路径：两套 VS 和六套 PS 的原始 DXBC 对照均已建立。VS 的 2,643,111 个有效 float32 输出、PS 在三组明确测试 sampler 下的 7,454,640 个输出均逐位一致；修复面部实体索引覆盖和皮肤分类整数标志被反编译成零的问题。Unity 六 Pass 与额外 s3 变体通过。详见 [像素翻译报告](native-pixel-port.md) 和 [顶点报告](native-vertex-port.md)。本轮未重建 Player 或模型；真实 sampler 描述符、实时动态集成仍待完成。

日期：2026-09-05。工程：`E:\ZZZ\local-only\RemielleHoyoToon`，Unity 6000.3.17f1，Built-in Forward / Linear / D3D11。

这是模型验收之后的渲染工作。旧模型基线仍在 `E:\ZZZ\local-only\RemielleHandoff`。当前包含独立光照场景、派生材质、展示/商店原生 Bloom、角色环境光顺序修正、战斗运行时发光、HighQualityBloom 与最终 UberPost；材质 ID 光滑度选择和鼻线方向阈值也已接入。战斗相邻绘制的角色归属已经闭环，第六阶段恢复了六个原生材质 PS 的四目标 G-buffer 编码和 draw 522–533 延迟序列；包含 Remielle 的 draw 523 角色/敌人分支与 draw 531 环境分支均完成同一抓帧输入下的 GPU 精确回放。实时蒙皮人物现已能离屏生成四个原生格式 MRT、D32S8、上一帧变形运动，以及四级联/角色专用 R16 阴影图；o1 已按 RimGlow RGB 与发光累加 alpha 的原生字段语义纠正，G-buffer Pass 也已从 o0 抑制 Forward RimGlow，数据经零误差 R10 展开进入原生 draw 523，并通过四姿态 Player 读回。原生 o0/o1 的完整数值数学、可见相机接入、关卡环境/反射/AO、粒子和 TAA 仍未完成，因此仍不能宣称整套原游戏渲染逐像素一致。

## 打开和使用

- Unity 场景：`Assets/RenderingReview/Remielle_LightingReview.unity`。打开后进入 Play。
- 独立程序：本目录 `Player/RemielleLighting.exe`；运行 `Start-Review.ps1` 以 1920×1080 窗口启动。可传入 `-Width 2560 -Height 1440`。
- 左侧切换 Character display、Costume store、Trial combat；环境色、环境光响应、自阴影、地面、菜单 Bloom 均有开关；战斗可分别切换 Captured combat HQ Bloom 和原生镜头色散。
- Camera 的 Capture 使用捕获相机的展示/商店距离、高度和视角；Full 看全身；Face 看面部。Orbit 转相机，Light rotation 转光照，Reset light 回到采集配置。
- Save 4K PNG 输出 3840×2160、不含操作面板的相机画面，文件位置会显示在面板底部。默认保存到 Unity `Application.persistentDataPath/LightingCaptures`。
- Pause/Play 和下方 15 个原生动作只用于检查光照与运动的配合。角色控制器、输入移动系统、完整状态机尚未接入。
- 跑步等动作保留原始位移，固定相机下可能走出画面。勾选 Follow body movement 可让检查相机跟随身体位移；它只移动查看相机，不修改动画、模型根节点或动作时间。
- 最终后处理效果以 Game/独立程序/导出图为准；Scene 窗口不会自动执行这台相机的 `OnRenderImage` 后处理。

Unity 的几何/地面阴影默认关闭：直接套用通用阴影图会给皮肤带来原展示图没有的硬边。这个开关是可见 Forward 画面的可选对照工具，调用 Unity `SHADOW_ATTENUATION` 适配路径。离屏原生布局探针另行每帧生成角色专用阴影图和四级联阴影图，并使用恢复的 9/11 点核；它目前不会替换可见相机。面部 SDF、材质内部 Toon 明暗和已恢复的头发阴影不依赖 Unity 几何阴影开关。

## 数据来自哪里

原始三次 F8 采集保持不变：

```
E:\ZZZ\FrameTools\XXMI Launcher\ZZMI\FrameAnalysis-2026-09-04-144810
E:\ZZZ\FrameTools\XXMI Launcher\ZZMI\FrameAnalysis-2026-09-04-144910
E:\ZZZ\FrameTools\XXMI Launcher\ZZMI\FrameAnalysis-2026-09-04-145107
```

`lighting-evidence.json` 记录光照方向、颜色、相机、环境色、六个 Post tint、draw call、CB 文件及 SHA-256。`character-shader-evidence.json` 记录源着色器与捕获哈希的精确匹配。游戏 Shader 从本地原始 `.blk` 提取，没有下载替代材质或改写原资产。

展示/商店采用 `miHoYo/Character/NapAvatarUI` 和 `NapAvatarUIFace`。已匹配的 PS 包括 `a9a150925def11b2`（身体）、`1c51e86c69e9189a`（头发）、`106a138c0e9aac07`（面部）。其全局常量证实了环境色与 Post tint 的排列。

关键修复是 UI 材质的 `_OverrideMainLight`。原游戏用整数开关，且身体和头发分别有方向；当前 HoyoToon 原版本只有声明，没有使用这些参数。新的派生 Shader 按顶点 COLOR.b 的 bit 5 选择身体/头发方向，面部使用身体方向；面部 SDF 的顶点准备和片元着色使用同一方向，避免两个阶段照明不一致。

| 场景 | 原始身体方向 | 原始头发方向 |
|---|---|---|
| 展示 | (0.608848, 0.361615, -0.706073) | (0.444982, 0.041988, -0.894555) |
| 商店 | (0.702644, 0.309816, -0.640551) | (0.703057, 0.061960, -0.708429) |

上表是采集世界空间。实现通过原始 View 矩阵转换，再映射到本项目的相机正面约定；不能把它当作所有游戏地图、任意角色朝向下通用的世界坐标。

原有中性材质的六个 Post tint 仍保持白色。采集环境色只写入新场景的派生材质和运行时实例。战斗预设使用战斗角色光颜色、角色 LUT，以及本轮完整移植的捕获 UberPost（内含最终后处理 LUT 和镜头色散）；战斗场景光颜色与角色光颜色分别记录，没有混用。

## 原生菜单 Bloom 的恢复方法

这部分使用游戏实际编译的 `Hidden/PostProcessing/Nap Bloom` 和 `NAPBloom/MultipleGaussPassFilter`，不是通用五点模糊的近似。

1. 由展示页 draw 93–105 建立 13 步依赖：降采样提亮、横纵模糊、多区域降采样、三组横纵模糊、四层合成。
2. 保留原有阈值 0.45、提亮增益 0.8、合成权重 (0.30, 0.30, 0.26, 0.15)、各组采样位置和权重、Atlas 区域与边界限制。
3. 最终加入展示/商店 UberPost 的 Bloom 合成曲线和强度 1.3。
4. 逐步向移植 Shader 输入游戏捕获纹理，对比同一 draw 的捕获输出；原始 Half 输入保留 Half 精度，R11/G11/B10 的读回先在 GPU 转成 RGBAFloat。
5. `bloom-gpu-verification.json` 是验收结果。阈值步骤逐值一致，其余步骤最大误差为一个原生输出格式量化单位。这里验证的是 13 个 Bloom 处理步骤，不是整张角色画面的完整游戏还原。

中间目标保留采集尺寸 344×192、153×161、857×479，原本就用于低频泛光。人物和最终画面仍按窗口/导出分辨率完整渲染；这些低分辨率 Bloom 缓冲不是之前窗口像素化的原因。第一步采样偏移会跟随当前源图尺寸变化。

战斗捕获有两段不同用途的 Bloom。HighQualityBloom（draw 536–558）和后面的特效 Bloom（draw 652–664）不能混用。前者本轮接入实时画面；后者的 13 步已用原始输入/输出逐值回放验证，运行场景还没有对应粒子层，因此保持停用。战斗最终 UberPost（draw 666）已实际接入，替代旧的仅 LUT 后处理，避免重复调色。离屏原生布局已有上一帧变形运动；可见输出的 TAA 与历史缓冲仍待实现。

## 第三轮：战斗 HighQualityBloom 与实时材质遮罩

### 对第二轮顺序描述的纠正

第二轮把 HighQualityBloom 称为“角色调色前 Bloom”，这是不准确的。原始反射数据的 `_SceneUserLut_Params`、`_SceneLutContribution` 和原始 Pass 状态证明它区分场景与角色：

- draw 556：stencil bit 7 不等于 128，执行场景颜色加 Bloom 后的场景 LUT。
- draw 557：stencil bit 7 等于 128，保留已调色的角色基础 RGB；仅用场景 LUT 计算亮度比例，补偿 Bloom，再加回角色颜色。
- draw 558：stencil 等于 0，处理天空。

角色 LUT、场景 LUT 和最终 UberPost LUT 是不同作用阶段，即使某次采集绑定相同 LUT 内容也不能合并步骤。新实现没有关闭、重复执行或移动现有角色 LUT。

### 恢复与逐值验证

`prepare_hq_bloom.py` 恢复原始阈值、七级降采样、六级横纵滤波与向上合成、三种最终分支。23 个 draw 的每个已写 RGB 值均与采集一致，误差为 0；最终三分支按原始 stencil 重组后，2544×1440 完整 RGB 也一致。`hq-bloom-gpu-verification.json` 和 `verification.json` 保存数值。

`hq-native-replay.png` / `hq-native-truth.png` 使用同一个仅供显示的 HDR 曲线。这组图片验证原始输入的 Shader 回放，不表示重建人物的全部着色已与游戏一致。

关键实现细节：

1. `e3e399da` 在不同纹理槽代表不同内容的缓冲；依赖图用 draw、槽位、SHA-256 校验，不能只按短哈希或尺寸连接。
2. 阈值 Pass 的分类缓冲使用 Point，颜色与泛光纹理使用 Linear；错误插值会改变角色边缘的阈值分类。
3. PPFilter 的 CB0[263].x 是整数位模式。先解码整数样本数，再传入 Shader；保留原双样本 MAD 累加顺序、边界外为黑的条件和 RGB 权重。
4. 适配时显式保留顶点至片元的 TEXCOORD 向量签名；不能只复制反编译伪代码。反编译的整数寄存器、符号位运算须以 DXBC 指令为准。
5. 捕获中的 D32_FLOAT_S8X24 深度文件提供真实 stencil 掩码，验证只检查对应 Pass 实际写入的区域，再验证完整重组画面。

### 实时输入及其边界

`RemielleHighQualityBloom` 从当前 SkinnedMeshRenderer 绘制一张全分辨率材质缓冲。使用当前动画姿态、原 UV0–UV3、双面贴图选择及透明裁剪；R/A 保存角色覆盖/分类，G 保存主要发光均值。头发阴影辅助几何不充当真实表面，可选地面参与遮挡。

这张缓冲通过专用无光照 Pass 生成。Unity `CommandBuffer.DrawRenderer` 不会自动设置完整灯光信息，因此没有用它重复调用依赖灯光的主体着色。投影由当前相机的 GPU 投影矩阵明确传入。缓冲随窗口/高清导出尺寸重建，禁用战斗预设或销毁组件时释放。

实时缓冲支持当前源材质的主要发光，并按战斗抓帧的运行时状态加入 Wings、Weapon_01、Body_2 的次级发光以及 Weapon_01 的签名武器发光。`RemielleRuntimeProfile` 只保存参数；抓帧纹理由渲染复核场景上的 `RemielleCapturedBattleResources` 提供，不再写入受保护角色 Prefab。开启/关闭对照分别得到 16,591/1,187 个发光像素，角色覆盖和位置校验和保持一致。ScreenImage 在当前材料状态未启用。实时缓冲只保存当前 Bloom 公式需要的数据，**并非完整原生 G-buffer 的替代品**。

实时使用六级滤波合成的固定采集尺寸，人物与材质遮罩仍使用完整输出分辨率。黑色检查背景与可选地面共用场景合成公式；实际关卡天空、其他角色/敌人和粒子层尚未接入。原生布局阴影已进入离屏生产/消费链，但可见 Forward 画面仍是适配路径。当前 Forward 表面亮度与原游戏完整 BRDF 仍有差异，头发/皮肤高光及其周围泛光仍偏强；已验证的 Bloom 核不能证明输入表面亮度也已正确。

当前顺序：角色 Forward 着色（含角色 LUT）→ 战斗 HighQualityBloom（实时材质遮罩、场景/角色分支）→ 战斗最终 UberPost。展示/商店继续使用各自菜单 Bloom。

关闭战斗 Bloom 只把泛光输入置零，场景和角色分支及其调色仍保留，因此开关对照不会顺带改变背景调色。独立遮罩 Shader 使用标准 Always Pass；自定义 LightMode 会被当前 Built-in Shader 预处理器当作 SRP Pass 剔除，不能直接嵌入主材质并依赖该标签打包。

## 第五阶段：材质常量、鼻线与原生阴影边界

### 材质常量闭环

`analyze_material_cbuffer.py` 把 19 个已确认 Remielle 战斗 draw 的 UnityPerMaterial 捕获字节、游戏反射偏移、源 JSON 与当前派生 Shader 逐项连接。当前共有 1,845 条逐 draw 观测、519 种唯一变量签名；385 种已被 Shader 消费，6 种只有声明，128 种在当前 Shader 中不存在。阴影色距离归一化接入后，非零且未消费的候选从上一版 25 个降到 24 个。

### 相邻绘制归属闭环

此前把 draw 368–370/372 留作“可能缺失的 Remielle 网格/材质”，原因是它们位于连续角色绘制区间内，372 还与 Hair 共用 PS 变体和物理常量缓冲。这个结论现已被原生绑定范围和逐绘制缓冲差分否定：

- draw 371 Hair 从常量缓冲 `358d62cb` 的第 0 个常量读取；draw 372 从同一物理缓冲的第 32 个常量读取。D3D11.1 `VSSetConstantBuffers1` 的 `first_constant` 单位为 16 字节，不能忽略。
- 用正确子区间执行原生 VS 第 62–69 行后，372 投影到 x=1186.2–1204.5、y=804.1–811.0；371→372 的 o1 G-buffer 实际仅改写 52 像素，包围盒正是 x=1186–1204、y=804–810。
- 372 的对象位移约为 (-2.7488, 2.2239, -2.1233)，与远处训练敌人同组；Hair 的对象位移约为 (-13.4423, 1.7645, -2.4172)，属于近处 Remielle。368–370 也分别由各自的第 0/32/64 常量投影到训练敌人。
- 四个 draw 都没有 Remielle 源网格的有序索引匹配；Hair 371 与 Wings 373 则分别精确连接到源网格，构成两侧锚点。

因此 368–370/372 已从 Remielle 完整性缺口中排除，当前检查区间没有未归属绘制。共享 Shader、共享物理 CB 和绘制相邻只表示引擎批次复用，不能单独证明角色归属。可重复分析、逐项哈希和修正前后的反证见 `analyze_adjacent_draw_attribution.py`、`adjacent-draw-attribution.json/.md` 与 `adjacent-draw-attribution/battle-adjacent-draw-attribution.png`。

`analyze_native_material_frontier.py` 再把这 31 个候选连接到六个实际 PS 变体的 cb4 寄存器/分量并人工复核分支：12 个被捕获开关关闭，7 个为中性等价，3 个在捕获变体中根本未读取，4 个 `_AlbedoSmoothness2..5` 已实现材质 ID 选择且当前值均为 0.05。5 个明确适配项是两个鼻线阈值，以及 `_ReceiveShadows`、`_ShadowNormalBias`、`_ShadowColorFadeByZ`。对当前捕获变体已无独立的“活动参数未接入”项；完整原生像素程序仍是更大的上游移植边界。

### 鼻线方向阈值

原生 `NapAvatarStandardFace ps_013` 用头部局部方向、`_NoseLineHoriDisp`、`_NoseLineLkDnDisp` 和面部贴图 alpha 共同决定鼻线。派生 Shader 现在恢复了经反编译数据流证实的方向门控，Face 使用 0.85/0.5，Eyebrow 使用 0.92/0.62；颜色仍沿用稳定的本地 HoyoToon alpha/颜色路径。完整原生中间颜色要等下游 G-buffer/BRDF 一并移植，不能截取一段表达式强塞进 Forward Shader。

`RemielleNoseLineAudit` 在 D3D11 上检查七个 yaw 角。六个角度有预期差异，总计 10,068 像素；变化包围盒最多占画面的 2.63%，局限在面部。错误的青色中间结果已回滚，最终七角输出没有颜色污染。结果在 `nose-line-isolation/nose-line-isolation.json`。

### 原生阴影系统

`analyze_native_shadow_system.py` 对 19 个已映射 draw 和六个真实 PS 变体做了资源/CB/反汇编连接：

- 主光阴影固定在 t0，资源哈希 `553d3b4a`，2048×2048、array=4、R16_TYPELESS；每个变体执行 11 次 `SampleCmpLevelZero`。
- 角色专用阴影资源哈希 `9e00a8ad`，2048×2048、R16_TYPELESS；随变体绑定 t5/t7/t8/t12/t13，每个变体执行 9 次比较采样。
- 19 个 draw 在该帧共享 `$Globals` cb0 `5fb77447`、`MainLightShadows` cb2 `11fdc8a4` 和 `UnityNapCB` cb3 `161a5c12`。报告保存四个级联球、五组原始矩阵、角色阴影 UV 矩阵、对象偏移、图集参数、相机与主光数据。
- 所有已映射材质均为 `_ReceiveShadows=1`、`_ShadowColorFadeByZ=1`、`_ShadowNormalBias=0.01`，可用的材质 ID 阴影强度槽全部为 1。

两张深度图和对应矩阵都由当帧姿态、相机和灯光决定。捕获 DDS 可以精确回放那一帧，不能绑定到实时动画，否则投影会冻结。当前 `RemielleNativeShadowProbe` 已实现每帧生产：四层 2048² `R16_UNorm + D32_SFloat` 主光图、一张 2048² `R16_UNorm + D32_SFloat` 角色图、四组当前相机分割/矩阵与一组当前角色包围盒矩阵。30 个有效子网格分别写五次，共 150 次 shadow draw；接收端使用捕获的 `normal*0.01` 偏置、11 点旋转 Poisson 级联核和 9 点角色核。Face 按捕获 `_PerObjectShadowOnFace=0` 只接收级联，身体/头发组合两路。

Player 的四个姿态直接读回两类 R16 图和接收结果。角色图写入像素为 151,230 / 168,074 / 174,809 / 44,731；级联层写入分别为 `[0,64509,11175,3102]`、`[0,63867,11038,3070]`、`[0,93507,16133,4492]`、`[0,110066,19040,5289]`。第 0 层为空是当前近裁分割只到约 2.04 m、人物中心约 2.7 m 的结果，人物由第 1 层覆盖。按原生累加权重与直接比较语义修正后，四姿态组合阴影接收像素为 5,966 / 6,860 / 3,694 / 0；孤立场景没有原关卡遮挡物，所以级联接收大多为亮区。第五张 `R8G8B8A8_UNorm` 诊断目标分别保存组合、角色、级联和覆盖率，前四个原生 MRT 格式未改。

该实现证明了动态资源、矩阵、过滤结构和接收路径能随动画工作。过滤器保留六个原生变体共有的 `0.111100003` / `0.0908999965` 累加权重、级联深度范围守卫与直接 SampleCmp 比较语义，没有额外添加 R16 epsilon。级联拟合目前由复核相机/灯光/角色边界重建，场景没有原关卡投影物，完整原生材质阴影合成也未宣称逐位一致；捕获 DDS 继续只作为证据。`native-shadow-system.json/.md` 与 `player-verification.json` 保存机器结果。

阴影颜色还有一项已修复的派生错误。六个原生材质 PS 与 cb3 证明 `_ShadowColorFadeByZ=1`、`_PackedParams0.w=2.384185791015625e-7`，并使用 `min(1, 0.437249988 * distance(camera, worldPosition))` 计算归一化权重。旧适配误用了固定 0.56275/0.43725 混合，还把无关插值临时量参与颜色均值；现已按原生数据流修正。`analyze_shadow_color_depth_fade.py` 会验证常量、六个反汇编与源码标记；前后隔离图在 `shadow-color-depth-fade-isolation/`。

## 第六阶段：原生 G-buffer 与 DeferredShading 闭环

### 材质 MRT 编码

战斗中 19 个已映射 Remielle draw 使用六个原生 PS 变体：`9dd6a2a0a6d11117`、`80c34aae4f69f1ff`、`8fc7e589644cb3c4`、`77bdd348772c62c8`、面部 `dff613cbed805284` 和 `0c8ddaae78cc096f`。反汇编、输出绑定与实际 DDS 一致证明它们同时写入：

- o0 `R16G16B16A16_FLOAT`：主材质/光照颜色；除头发覆盖率变体外 alpha 为 1；
- o1 `R8G8B8A8_UNORM_SRGB`：附加光照负载。RGB 写入 `min(1, 0.2*sqrt(max(0,x)))`，draw 523 采样 sRGB 后以 `(5*x)^2` 解码；alpha 是附加亮度累计的三分之一，面部分支写 0；
- o2 `R10G10B10A2_UNORM`：xy 是中心约 0.498 的带符号平方根运动编码；z 是除以 255 的标志字节，标准分支依次编码 Ghost、Ignis、全局位、VFX 和 `cb4[41].w`，面部分支使用自身四位；w 在采样材质 ID 命中 `_SkinMatId` 时写 0.34，落盘为 2 位 alpha 的 1/3，透射/发光变体可强制为 0；
- o3 `R10G10B10A2_UNORM`：`worldNormal * 0.5 + 0.5` 编码；
- oD `D32_FLOAT_S8X24_UINT`：深度与后续模板分区。

`analyze_native_gbuffer_payloads.py` 对六个生产 Shader、draw 523 消费 Shader 和 19 个逐 draw 原始差分建立了字段级机器门禁。运动开关 `cb1[28].y` 在全部映射 draw 中为 1，o2.xy 的两个 10 位通道实际覆盖 482–531 与 480–534，动态运动编码已有数值证据。Remielle 的 19 个基础绘制在本帧 o2.z 全为 0；同一帧的训练敌人则在 64–66/68 与 368–370/372 写出字节 4，且 `ps-cb3[40].x=1`，因此全局 bit 2 路径已经实证，但不能把敌人的标志归到 Remielle。编码世界法线的最大单位长度误差为 0.001686。它解决了“原生人物材质到底给下游传什么”的结构问题；当前 Forward 可见路径保持不变，独立离屏探针已实时写出这四张目标。

`analyze_native_gbuffer_feature_bits.py` 又扫描了本地全部 14 个抓帧中的六个目标 PS，共找到 304 个常量完整的绘制：功能字节逐 draw 直方图为 0×276、4×8、6×20。2026-08-26 的四帧在训练敌人 IB `9fa01202/868c2729/4a596474` 上令 `_MarkAsIgnisFatuusMask=1` 且 `cb3[40].x=1`，因此现有资源已经实证 bit 1 与 bit 2；28 个非零 draw 全部属于已排除的敌人。Ghost、VFX 和 `_DecolorizationContrast.w`/bit 4 尚无激活帧。旧帧 MRT 只保存为 JPEG，报告只据精确常量与原生 HLSL 证明字节 6 的生产条件，不伪造无损输出像素统计。

### 镜面反射实例、轮廓和面部/头发后续绘制

`analyze_native_gbuffer_instances.py` 把同帧的两个人物渲染实例和基础材质之后的步骤分开建账。1024×576 四目标序列已由资源血缘精确识别为镜面反射生产器：12 个确认的 Remielle 基础绘制形成 11,470 像素并集，12 个轮廓绘制形成 855 像素并集；163–165 的头发在该反射中完全裁掉，229–231 的透明/贴花阶段留下 124 像素。该低分辨率实例省略或裁掉 Weapon_05、Face/Eye/Eyebrow 和两次炮体绘制，这是反射可见集/LOD 选择，不是源模型缺件。

血缘不依赖画面猜测：draw 300 的 `4bb2fe34` 输出与 Gaussian Blur 8 的输入逐字节相同，Blur 9 写回后的结果与 draw 530 t6 逐字节相同；draw 530 的序列化变体明确含 `USE_MIRROR_REFLECTION`。draw 530 输出 `d72d6162` 又与 draw 531 t2 逐字节相同。因此镜面人物确实进入了主相机环境/反射 resolve。

2544×1440 主相机的 19 个基础材质绘制之后，还有 19 个确认的 Remielle 轮廓绘制，写入并集为 10,017 像素。draw 413 使用生成的头发轮廓几何给 2,902 像素写 stencil 132；414–416 在 stencil 132/148 下重放脸、眼和眉，最终分别有 0/32/21 个主颜色像素生效。draw 488 写 45 个眼部覆盖像素；489 先给 53 个发丝像素写深度/模板，490 用不同混合与深度状态重写这 53 个高质量头发覆盖像素，491 再补 75 个轮廓像素。严格实时复刻必须包含这些步骤，不能只实现基础 19 draw。

`analyze_native_shader_identities.py` 已把后续步骤的五个哈希还原到原始 Unity 记录：`6c0519a487c74676`=`NapAvatarStandard/CharacterOutlineDeferred`，`8e41e455f2e919f7`=`NapAvatarStandardFace/FaceOutlineDeferred`，`02549f5219c55aa4`=`NapAvatarStandard/CharDepthOnly`，`d528b74afe9888e4`=`NapAvatarStandardEye/CharacterOpaqueEye`，`0c8ddaae78cc096f`=`NapAvatarStandard/CharacterToonDeferred` 的高质量头发变体。报告保存每项的 record、关键词、Pass 状态以及 block+CAB+pathID。draw 413 的 `bcddceab76ee10ce` VS 与 `c07b54c6554b2591` PS 在运行时缓存和反汇编中存在，但当前提取的 17 个序列化 Shader 资产中没有同哈希记录，所以来源身份仍明确列为缺口，未用合成名称冒充原始资产。

### draw 522–533、角色分支与环境分支

`analyze_native_gbuffer_deferred.py` 已连接 draw 522–533 的 12 步顺序。逐 draw 输出差分纠正了最初仅凭模板参考值作出的过宽解释：draw 523 实际改写 stencil 128/132/144/148 的 128,977 个 bit-7 角色/敌人像素；draw 524 在本帧没有颜色改写；525 与 527 处理 stencil 32 环境；528 处理背景。529 复制前段 HDR，530 生成 1272×720 环境/反射缓冲，531 将模板值 32 的环境 resolve 写入最终目标，532 从前段 HDR 恢复角色等非零且非 32 分类，533 写模板值 0 的背景。

draw 523 的源身份为 `273563795.blk` / PathID `2543301894137999429` / record 26 / pass 3，关键字为 `ENVIRO_SIMPLE_FOG`。它读取 t0 编码法线、t1 深度、t2 主材质/光照颜色、t3 sRGB 编码的附加光照负载、t4 运动/特征负载和 t5 LUT，有效常量为 185 个 float4。`prepare_deferred_character.py` 从不可变 DDS/CB 生成回放；`RemielleDeferredCharacterAudit` 核对 128,977 个实际写入像素：HDR 的 386,931 个 RGB 值**逐位一致**，R10 辅助目标的 515,908 个值最大误差仅 0.007775 个原生量化单位，越界为 0。

采样器状态是这条回放的必要组成：s0 为线性钳制，深度邻域采样 s1 为点钳制。把 s1 误写成线性只影响 176 个 HDR 值，但最大会偏离 70 个 R11G11B10 量化单位；恢复点采样后 HDR 误差归零。验收没有通过放宽阈值掩盖它。

draw 523 同时处理敌人，不能只凭分支范围宣称全是 Remielle。为此分析器把 19 个已确认 Remielle draw 各自造成的 o0/o1/o2/o3/oD 原始字节变化分别取并集，再合并为完整写入范围，得到 104,502 个程序化确认像素，包围盒 x=1038–1549、y=247–800；它们 **104,502/104,502 全部落在 draw 523 改写范围内**，对应 stencil 128=96,003、132=2,805、144=5,597、148=97。只按 o1 变化统计得到 38,542 像素，这是因为 o1 量化后可能维持相同字节值，现保留为保守子集。这建立了 Remielle 材质生产端与角色 deferred 分支之间的直接像素连接。

draw 531 的源身份为 `273563795.blk` / PathID `2543301894137999429` / record 2384 / pass 10，启用 `ENVIRO_SIMPLE_FOG`、`USE_MIRROR_REFLECTION`、`_CAPSULE_AO_ON`。它是 stencil 32 的环境主 resolve，读取连续 t0–t11：法线、线性与 sRGB 底色、半分辨率环境、BRDF LUT、深度、两路 AO、场景 LUT、o0 材质负载、特征负载和前段 HDR；有效常量范围为 181 个 float4。世界位置由 t4 深度、cb0[134..137] 逆视投影矩阵和 cb0[30] 相机位置重建。

`prepare_deferred_shading.py` 从不可变 DDS/CB 生成本地审计夹具和派生 Shader；`RemielleDeferredShadingAudit` 在 RTX 5070 Ti / D3D11 上对模板值 32 的 3,471,327 个环境像素、10,413,981 个 RGB 值进行逐值核对。最大误差为 **1.0 个 R11G11B10 原生量化单位**，越界值为 0。`deferred-shading-native-replay.png` 与 `deferred-shading-native-truth.png` 是同一抓帧输入的结果。它证明环境 BRDF、环境/反射、AO、雾、LUT 和绑定可重复；人物侧的严格证据来自上面的 draw 523，不再把 draw 531 误称为人物 resolve。

`RemielleNativeMrtCapabilityAudit` 还在当前 RTX 5070 Ti / D3D11 上执行了独立传输层验证。本机报告 8 路同时渲染目标能力；四个原生格式 `R16G16B16A16_SFloat`、`R8G8B8A8_SRGB`、两张 `A2B10G10R10_UNormPack32` 均可作为 RenderTarget 按请求创建并正确量化，`D32_SFloat_S8_UInt` 深度/模板及 `R8_UInt` 模板视图也可写可采样。D3D11 的模板字节位于整数纹理 G 通道，不能按其他 API 的 R 通道读取。Unity 当前允许 R10/R11 作为渲染目标，却不支持在普通 Shader 中直接采样这两种打包格式；实时路径已在材质 MRT 与 resolve 之间加入浮点展开，并由 Player 对 o2/o3 全图逐通道确认误差为 0。离线回放夹具同样无损展开为 float。该审计与实时读回共同关闭了本机 MRT 写入、模板读取和打包目标转换边界。

`native-gbuffer-payloads.json/.md` 保存逐字段公式、六个变体源证据、逐 draw 范围和直接模板分类；`native-gbuffer-instances.json/.md` 保存镜面/主相机实例、反射资源血缘、轮廓与面部/头发后续步骤；`native-gbuffer-feature-bits.json/.md` 保存 14 帧功能位扫描；`native-shader-identities.json/.md` 保存后续 Pass 的序列化身份与剩余缺口；`native-gbuffer-deferred.json/.md` 保存完整顺序、格式、源身份、后续模板直方图、Remielle 像素连接与边界。`captured-deferred-character.json` / `deferred-character-gpu-verification.json` 和 `captured-deferred-shading.json` / `deferred-shading-gpu-verification.json` 分别保存角色、环境两条可执行回放。`deferred-character-fixtures/` 约 283 MiB，`deferred-shading-fixtures/` 约 316 MiB，只用于本地审计，不进入 Player；深度按无损单通道 RFloat 保存，避免四通道无意义膨胀。

实时探针现已每帧生产四目标 MRT、深度/模板、上一帧变形运动和两类动态阴影，并把 R10 零误差展开结果交给原生 draw 523。o1 已恢复字段分工：RGB 使用 RimGlow 平方根编码，alpha 是发光/附加亮度累加的一次三分之一；G-buffer Pass 已从 o0 剥离 Forward RimGlow，因此当前消费链不再重复叠加。闭环仍需把近似 o0/o1 数学替换为原生材质主体，生产环境/反射与胶囊 AO，并把整条路径接到可见动画相机。静态捕获输入不会绑定到实时人物，也不会被当作完成的动态实现。

## 第二轮的具体变化与验证范围

### 角色环境光

`NapAvatarUI` 身体 PS 第 623 行、头发 PS 第 561 行，以及 `NapAvatarUIFace` PS 第 571 行，均明确包含 `_CharacterAmbient × albedo`，之后才进入角色调色阶段。原适配在 LUT 后执行 `CalcLighting`，又加入 Unity SH 环境项，计算顺序不同。

派生 Shader 现在对脸、身体、头发等主分支在 LUT 前加入采集线性环境色，并把主光颜色乘法移到调色前；该分支不再重复叠加 LUT 后的 SH。这只修正了一个明确的照明项，不等于移植了完整原生 BRDF。眼睛等单独早退支路和附加灯光仍保留既有适配；离屏原生布局使用动态 9/11 点阴影路径。查看器可切换旧响应用于同姿态检查。

### 战斗最终画面

精确匹配的原程序为 `Hidden/Universal Render Pipeline/UberPost`，PS `5b043c5784c3b4ed`，source block `1076301774.blk`，PathID `4408032091271226619`，record 514。保留它的镜头色散 → Bloom 合成 → 线性/sRGB 转换 → 4096×64 最终 LUT 顺序。捕获中噪点、暗角和额外畸变均未启用；本轮只对该捕获配置负责。

战斗最终 Pass 使用所有原捕获输入纹理做回放，与原输出逐通道对比，最大差异为 1 个 8 位输出量化单位。`combat-post-native-truth.png` 和 `combat-post-native-replay.png` 是同一输入场景的原输出与重放，**这里才是严格对齐的画面对照**；它不是 Unity 重建人物与游戏人物的全画面一致证明。

战斗 FX Bloom 的 13 步（652–664）逐个输入原始 DDS，在 GPU 输出的每个已写 RGB 值上精确一致。验收保留了 RGBAHalf 采样精度；R11/G11/B10 值可以无损放入 Half，不能为方便而改为 Float 纹理插值，否则阈值附近会出现误差。原生 DXBC 布尔位掩码也按位语义恢复，没有把 `0x3f800000` 错当普通整数数值。

`combat-post-gpu-verification.json` 记录全部数值；`combat-post-fixtures.json` 记录纹理格式、尺寸、原 DDS 和 SHA-256。捕获纹理仅用于本地验收，不打进 Player，不会作为截图背景或假模型使用。

## 代码与资产分工

所有下列相对路径均相对于正式 Unity 工程。

| 路径 | 用途 |
|---|---|
| `Assets/RenderingReview/Remielle_LightingReview.unity` | 可使用的独立渲染场景 |
| `Assets/RenderingReview/Materials/` | 13 个角色派生材质和可选地面材质 |
| `Assets/RenderingReview/Shader/` | 从当前已修复 HoyoToon 派生的角色 Shader，UI 光向与 LUT 前的角色环境光接入 |
| `Assets/RenderingReview/captured-lighting.json` | 三套照明/环境数据 |
| `Assets/RenderingReview/captured-bloom.json` | 菜单 Bloom 原始参数与步骤 |
| `Assets/Scripts/RemielleLightingReview.cs` | 相机、光照预设、材质实例、查看面板与高清导出 |
| `Assets/Scripts/RemielleReviewBloom.cs` | 原生菜单 Bloom 的运行时调度与资源释放 |
| `Assets/Shaders/CapturedMenuBloom.shader` | 从本地原始程序适配的 Bloom 各 Pass |
| `Assets/Editor/RemielleRenderReviewBuild.cs` | 生成新场景并构建独立程序 |
| `Assets/Editor/RemielleBloomAudit.cs` | 菜单 Bloom 捕获输入/输出的 GPU 数值对照 |
| `Assets/Scripts/RemielleCapturedCombatPost.cs` | 战斗最终 UberPost 的实际运行入口，含 LUT、色散及未来 FX Bloom 输入 |
| `Assets/Shaders/CapturedCombatPost.shader` | 战斗 FX Bloom 13 步所用的 6 类 Pass，加最终 UberPost Pass |
| `Assets/RenderingReview/captured-combat-post.json` | 战斗捕获常量、步骤、来源哈希 |
| `Assets/Editor/RemielleCombatPostAudit.cs` | 战斗 FX Bloom 和最终 UberPost 的捕获回放验收 |
| `Assets/Scripts/RemielleHighQualityBloom.cs` | 战斗 HQ Bloom 与实时角色材质缓冲 |
| `Assets/Shaders/CapturedHighQualityBloom.shader` | 原生 HQ 滤波/合成 Pass 和地面遮挡 Pass |
| `Assets/RenderingReview/Shader/ReviewBloomMask.shader` 与 `Include/review-bloom-mask.hlsl` | 当前蒙皮/UV/主要发光数据的专用绘制 Pass |
| `Assets/RenderingReview/captured-hq-bloom.json` | 23 步参数和按内容校验的输入依赖 |
| `Assets/RenderingReview/RuntimeSceneLut.asset` | 原采集场景 LUT，仅包含调色数据 |
| `Assets/Editor/RemielleHighQualityBloomAudit.cs` | 23 步原始输入的 GPU 验证与 stencil 区域检查 |
| `Assets/Editor/RemielleRuntimeMaterialAudit.cs` | 战斗运行时次级/签名武器发光状态校验 |
| `Assets/Editor/RemielleNoseLineAudit.cs` | 七角鼻线方向隔离与五槽光滑度检查 |
| `Assets/Shaders/CapturedDeferredShading531.shader` | 由原生 draw 531 HLSL 机械适配的精确抓帧回放 Shader |
| `Assets/Editor/RemielleDeferredShadingAudit.cs` | 环境分支 12 路输入、181 个 float4 常量和模板 32 分区的 GPU 逐值核验 |
| `Assets/RenderingReview/captured-deferred-shading.json` | 环境 draw 531 输入、常量、源哈希和本地夹具索引 |
| `Assets/Shaders/CapturedDeferredCharacter523.shader` | 由原生 draw 523 HLSL 机械适配的角色/敌人分支回放 Shader |
| `Assets/Editor/RemielleDeferredCharacterAudit.cs` | 角色分支 6 路输入、185 个 float4、HDR 与辅助目标的 GPU 逐值核验 |
| `Assets/RenderingReview/captured-deferred-character.json` | draw 523 输入、常量、写入掩码、源哈希和本地夹具索引 |
| `Assets/Shaders/RemielleNativeMrtCapability.shader` | 四路原生格式 MRT、D32S8 写入与模板读取探针 |
| `Assets/Editor/RemielleNativeMrtCapabilityAudit.cs` | 本机 D3D11 多目标与可采样模板能力验收 |
| `Assets/Scripts/RemielleLightingAudit.cs` | 独立程序的照明、截图、实例数量、动作回归 |
| `Assets/Scripts/RemielleNativeGBufferProbe.cs` | 当前蒙皮姿态的离屏四目标 MRT、D32S8 调度与实时材质分类 |
| `Assets/Scripts/RemielleNativeShadowProbe.cs` | 动态四级联/角色专用 R16 阴影图、当前矩阵和 150 次子网格阴影调度 |
| `Assets/RenderingReview/Shader/NativeShadowDepth.shader` | 动态阴影图的蒙皮深度生产 Pass |
| `Assets/RenderingReview/Shader/Include/remielle-native-shadow.hlsl` | 角色 9 点、级联 11 点比较过滤及法线偏置 |
| `Assets/RenderingReview/Shader/Include/remielle-native-gbuffer-probe.hlsl` | o0–o3 字段写入、UV3 背面路径、皮肤类与世界法线编码 |
| `Assets/RenderingReview/Shader/Include/remielle-native-depth-stencil-readback.hlsl` | D32S8 深度和 D3D11 模板 G 通道读回 |

派生角色 Shader 的来源哈希和差异范围在 `shader-derivation.json`；原 HoyoToon GPL-3.0 许可证仍适用，原包内 LICENSE 保留。游戏源文件与派生着色器保持本地使用。

`prepare_rendering.py`、`prepare_shader.py`、`prepare_bloom.py`、`prepare_combat_post.py`、`prepare_hq_bloom.py`、`prepare_deferred_character.py`、`prepare_deferred_shading.py` 是本目录的数据/Shader 准备脚本；`analyze_adjacent_draw_attribution.py`、`analyze_material_cbuffer.py`、`analyze_native_material_frontier.py`、`analyze_native_shadow_system.py`、`analyze_shadow_color_depth_fade.py`、`analyze_native_gbuffer_deferred.py`、`analyze_native_gbuffer_payloads.py`、`analyze_native_gbuffer_instances.py` 生成角色归属、材料、阴影、阴影色距离归一化、字段、实例与延迟管线证据；`review_bloom_mask.hlsl` 是派生 Shader 的遮罩源代码。`Run-Checks.ps1` 是总验收入口，`collect_review.py` 同时封存本轮代码、生成脚本、场景与 Player 的 `implementation-manifest.json`。五个 `*-fixtures/` 目录仅是本地验收数据，不打进 Player。

## 与模型基线的关系

本轮最终验收：18 张静态高清图（含 3840×2160）、415 帧运行检查、10 组实时遮罩检查、30 次预设切换，材质数量保持稳定。遮罩缓存材质会复用并在退出时释放。新增运行时发光、鼻线方向、光滑度槽、阴影输入审计、动态 R16 阴影生产/接收和原生 G-buffer/DeferredShading 抓帧闭环；保留战斗 Bloom、背面、地面、同姿态环境光与镜头色散对照。独立 GPU 灯光探针、4× MSAA、菜单 Bloom 13 步、战斗 HQ Bloom 23 步、完整 stencil 合成、draw 523 角色分支和 draw 531 环境分支均通过；HQ Bloom 与 draw 523 HDR 回放 RGB 精确一致，draw 531 在一个原生量化单位内。实时 MRT 四姿态随蒙皮变化，三个运动姿态有真实上一帧变形运动；两类阴影图在四姿态均有非空生产证据。完整数值在 `verification.json` 与 `player-verification.json`。

原始 GLB、完整精度动画输入、15 个主动画、旧模型场景及包内正式角色 Shader 没有改变，也没有为打包降低动画精度或删减动作。受保护基线目前是 **81/82**：唯一不一致项是 `Assets/V3/Remielle_V3_Animated.prefab`。一次误调用 `NativeAnimationBuild.Run` 重新生成了该 YAML；清除临时渲染纹理引用后，语义/运行检查全部通过，但旧源字节尚未找回。旧 `baseline.json` 没有改写，总验收仍因此保持红色。事件、搜索范围、旧/现哈希在 `prefab-regeneration-evidence.json/.md`；禁止再次运行 `NativeAnimationBuild.Run` 或用新哈希覆盖旧基线。

构建时曾发生大体积动画 YAML 集中读取导致的内存不足。当前流程先依次读取动画，再清理临时场景和未使用资产，之后构建。构建入口还检查 Shader 编译错误；仅有 BuildPlayer 返回成功不足以证明画面有效。

最终构建没有代码或 Shader 编译错误，仍有两条依赖包告警：工程未关联 Unity Services 云项目，以及 HoyoToon 包内未使用的 `AvatarLight VRC` 示例缺少 VRChat 脚本。新场景没有实例化该示例。它们与角色的蒙皮、材质绑定和新渲染代码无关。

## 已知边界与下一步

- 光照方向、环境常量、LUT 前环境光、材质 ID 光滑度、鼻线方向阈值、运行时发光、菜单/HQ Bloom、战斗最终 UberPost 和抓帧级原生 Deferred resolve 有原始采集依据。当前 HoyoToon Forward 展示仍未实时替换成原生 G-buffer/Deferred 管线。
- 光照预设中的展示/商店相机有来源；战斗使用便于检查的固定相机，没有冒称恢复当时完整镜头轨迹。
- 当前播放姿态不是自动对齐原采集的同一时间点。对照图片用于视觉检查，不能据此声称逐像素误差已归零。
- 既有翅膀透射贴图和着色路径继续使用；实时 MRT、深度/模板、上一帧变形运动和动态阴影探针已经接入。原生 BRDF、完整辅助缓冲、精确级联拟合/关卡投影物、屏幕空间项、环境和附件显隐仍需动态对照。
- 已通过的模型检查不能扩大解释为完整游戏控制器已完成。

后续优先顺序：把 o0/o1 的 HoyoToon 数学近似替换为原生材质主体；当前字段分工和不重复叠加已保证，但数值尚非逐指令原生。随后补环境/反射、AO、精确级联拟合与原关卡投影物，把已验证的离屏链路接到可见相机。环境 draw 531 可在完整场景接入时复用已验证路径。再核对翅膀逆光、粒子层/FX Bloom、TAA、相同姿态时间和附件显隐，之后才能做整张模型的严格像素比较。渲染基准稳定后再接控制器和动画状态逻辑。

## 查阅的官方资料

- [Unity CommandBuffer.DrawRenderer](https://docs.unity.cn/2023.2/Documentation/ScriptReference/Rendering.CommandBuffer.DrawRenderer.html)：自定义绘制不会自动设置完整灯光数据，本轮据此使用专用无光照材质缓冲 Pass。
- [Unity CommandBuffer.SetRenderTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetRenderTarget.html)：确认多颜色目标可与同一深度/模板目标绑定。
- [Unity RenderTextureSubElement.Stencil](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.RenderTextureSubElement.Stencil.html)：确认 `R8_UInt` 模板视图的 Load 读取方式，以及 D3D11 使用 G 通道的例外。
- [Unity SystemInfo.supportedRenderTargetCount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SystemInfo-supportedRenderTargetCount.html)：运行时检查同时 MRT 数量。
- [Unity GL.GetGPUProjectionMatrix](https://docs.unity.cn/6000.2/Documentation/ScriptReference/GL.GetGPUProjectionMatrix.html)：显式矩阵写入 RenderTexture 时的投影转换。
- [Unity SkinnedMeshRenderer.BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html)：保存当前与上一帧 CPU 蒙皮顶点，恢复真实变形运动和阴影投射姿态。
- [Unity RenderTexture dimension](https://docs.unity3d.com/cn/6000.0/ScriptReference/RenderTexture-dimension.html) / [volumeDepth](https://docs.unity3d.com/cn/6000.0/ScriptReference/RenderTexture-volumeDepth.html)：建立四层主光阴影数组。
- [Unity 纹理数组 Shader 用法](https://docs.unity3d.com/cn/6000.0/Manual/class-Texture2DArray-use-in-shader.html)：确认二维数组声明与层索引采样结构。

- [HoyoToon 官方说明](https://github.com/Hoyotoon/HoyoToon/blob/main/README.md)：确认当前 Built-in 路线。
- [HoyoToon 官方 ZZZ Shader](https://github.com/Hoyotoon/HoyoToon/blob/main/Shaders/ZenlessZoneZero/HoyoToonZenlessZoneZero.shader) 与 [ZZZ common include](https://github.com/Hoyotoon/HoyoToon/blob/main/Shaders/ZenlessZoneZero/Include/zzz-common.hlsl)：核对鼻线属性和官方当前实现边界。
- [Unity OnRenderImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.OnRenderImage.html)：相机后处理入口与顺序。
- [Unity RenderTexture 抗锯齿](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-antiAliasing.html)：高清输出采样配置。
- [Unity Camera.Render](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.Render.html)：独立相机截图。
- [Unity 灯光线性强度](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.GraphicsSettings-lightsUseLinearIntensity.html)：颜色输入与 Shader 端线性值的区别。
- [Unity HLSL 采样器](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html)：原生采样器的适配。
- [Unity ReadPixels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.ReadPixels.html)：GPU 到 CPU 的同格式读回；本轮据此修正了验证时的低值转换问题。
- [Unity 官方 Bloom 参考实现](https://github.com/Unity-Technologies/PostProcessing/blob/v2/PostProcessing/Shaders/Builtins/Bloom.shader)：作为常规处理流程参考，最终菜单 Bloom 算法来自本地原游戏程序。

## 2026-09-05：角色展示页与时装商店页原生链补充

两个 UI 抓帧现已从角色 draw 一直追到 DeferredShading 输出。展示页使用 call 39–62，商店页使用 call 40–63；去掉一个 call 的整体偏移后，24 次 draw 的顺序、索引数量和起点、StencilRef、VS/PS 哈希全部一致。顺序包含 Wings、Hair、Body_2、Body_1、Face、Eyebrow 的基础绘制，八组常规轮廓，一组运行时生成发丝轮廓，以及脸、眉、眼和头发的校正/深度/覆盖步骤。每个 draw 对 `o0/o1/o2/o3/oD` 的实际改写像素数都记录在 `native-ui-character-pipeline.json`，不再只用最终截图推断。

三个 UI Shader 的原始序列化身份已经由 AnimeStudio 对原游戏 block 直接枚举并与解析 JSON 交叉检查：

| Shader | block | CAB | pathID |
|---|---|---|---:|
| `miHoYo/Character/NapAvatarUI` | `2083248778.blk` | `CAB-71c2b9f7f898cf35b459c692a301a19b` | `-1264760374214708068` |
| `miHoYo/Character/NapAvatarUIEye` | `2507100060.blk` | `CAB-94df647bf5ba440b9265117744cc5e53` | `497480590774261793` |
| `miHoYo/Character/NapAvatarUIFace` | `2507100060.blk` | `CAB-5557432d335cd4adeb6dd4253321ca7d` | `2497034685900247571` |

`extract_ui_shader_evidence.ps1` 对 17 个相关 Shader pathID、12 个 block 执行完整 DXBC FNV-1 哈希检索。基础、matcap、脸、眼、头发、轮廓和深度路径共 14 个 VS/PS 哈希均精确命中上述序列化资产。发丝生成轮廓 `bcddceab76ee10ce/c07b54c6554b2591` 以及半分辨率两级处理 `88dbc503fb7bb29a/52892a9ee3f22e47`、`f6629881b97d4608/4cffd0d19d95e5e8` 在该扫描范围内无命中，按运行时生成边界保留，不伪造 block/CAB/pathID。

角色 G-buffer 完成后，第一步半分辨率处理读取全分辨率深度与法线，在 2×2 中选择最小深度，按 `1/(cb0[62].x*depth+cb0[62].y)` 线性化，并输出所选法线、样本编号与最大/最小深度。第二步对深度范围做 2×2 max/min 归并。抓帧只导出了第二步当前基级视图，因此仅断言可见基级与上一步范围纹理逐字节相同；未导出的 mip 不作推断。

最终 `o0/o1/o2/o3/oD` 的 payload 分别精确进入共享角色 DeferredShading 的 `t2/t3/t4/t0/t1`。初始化 pass 在角色 stencil 区域把 HDR 置零，把辅助 R10 写为固定中性字 `521725`，即整数通道 `509/509/0/0`。随后 `fbb07bad65276f2d` 恰好改写所有 bit 7 像素：展示页 604,914，商店页 358,402；后续 StencilRef 16/32/2/32 分支对这两份隔离目标没有改写。该 PS 与战斗 draw 523 是同一个序列化程序：`Hidden/Universal Render Pipeline/DeferredShading`，`273563795.blk`，pathID `2543301894137999429`，record 26，pass 3，关键字 `ENVIRO_SIMPLE_FOG`。

`prepare_deferred_character_ui.py` 从两次原始抓帧建立独立夹具；`RemielleDeferredCharacterAudit.cs` 现在一次核验战斗、展示、商店三种 profile。RTX 5070 Ti Laptop GPU / D3D11 的结果为：战斗 128,977 像素，展示 604,914，商店 358,402；两份 UI 的 RGBA16F HDR 四通道逐位一致，辅助 R10 均为 0 个越界值。对应预览是 `deferred-character-ui-display-native-*.png` 与 `deferred-character-ui-store-native-*.png`。

新增文件分工：

| 文件 | 用途 |
|---|---|
| `prepare_deferred_character_ui.py` / `captured-deferred-character-ui.json` | 生成并索引两份 UI Deferred 精确回放夹具 |
| `analyze_ui_shader_source_identities.ps1` / `ui-shader-source-identities.json` | 直接从原 block 确认 Shader 名、CAB 与 pathID |
| `extract_ui_shader_evidence.ps1` / `ui-shader-evidence.json` | 对 20 个 UI/运行时哈希做 17 pathID、12 block 的序列化检索 |
| `analyze_ui_character_pipeline.py` | 复算 48 个角色 draw 的五目标差分、半分辨率与 Deferred 接力 |
| `native-ui-character-pipeline.json/.md` | 完整机器证据与可读结论 |
| `Assets/Editor/RemielleDeferredCharacterAudit.cs` | 在 Unity D3D11 上核验战斗 R11 与两份 UI RGBA16F 输出 |

`Run-Checks.ps1` 会先重建 UI 夹具，再执行 Unity GPU 审计、CAB 身份核对、序列化哈希提取、UI 全链分析和最终汇总。其末尾仍会因为受保护模型基线 81/82 主动报红；这是已记录 prefab 字节事件，不代表本节 UI 渲染审计失败，也不允许通过改写旧基线消除。

这次补充关闭了 UI 抓帧真值、序列化 Shader 身份和共享 Deferred 分支的证据缺口。Unity 可见人物目前仍由 HoyoToon Forward 绘制；离屏实时四目标 G-buffer、深度模板、角色 Deferred、真实变形运动和动态级联/角色阴影已在下一节完成。下一步补完整 o0/o1 原生数学、环境与镜面反射、AO、翅膀逆光/透射、粒子与 FX Bloom、TAA，最后接入可见相机并做同姿态同相机逐像素视觉对照。

## 2026-09-05：实时原生布局 MRT 与 D32S8 探针

`RemielleNativeGBufferProbe` 已挂到 `Remielle_LightingReview.unity` 的相机上。它在 `LateUpdate` 后读取当前 29 个 SMR 中启用的 30 个有效子网格绘制，复用派生材质的主纹理、数据纹理、面部类型、透明开关、法线贴图及双面 UV 设置，离屏生成：

| 目标 | 实际格式 | 当前实时内容 |
|---|---|---|
| o0 | `R16G16B16A16_SFloat` | 已审查的 HoyoToon 前向材质结果，包含现有脸 SDF、matcap、透射、环境色与运行时发光 |
| o1 | `R8G8B8A8_SRGB` | RimGlow 使用原生平方根 RGB 编码；A 保存基础/次级/签名发光累加的一次三分之一 |
| o2 | `A2B10G10R10_UNormPack32` | XY 由上一帧变形后位置写原生带符号平方根运动码，首次/静止帧为中性码；Z 为 0，A 写皮肤类 `0.34` 并量化为 `1/3` |
| o3 | `A2B10G10R10_UNormPack32` | 主体法线贴图或顶点法线转换后的世界法线 `n*0.5+0.5` |
| oD | `D32_SFloat_S8_UInt` | 深度；普通表面模板 128，Eye/Eyebrow 模板 144，模板视图为 `R8_UInt` |

o1 的语义在本轮重新核对后已纠正。原生标准材质在写出前对独立 RimGlow 颜色执行 `min(1, 0.2*sqrt(max(0,x)))`，而 alpha 写发光/附加亮度累加器的 `0.333299994` 倍；draw 523 再以 `(5*x)^2` 解码 RGB。旧实时适配把 emission 误填到 RGB，并把已经求均值的 alpha 又除以 3。捕获中 draw 365/366/367/371/373/375/387 的 `_Emission=0` 但 o1.rgb 非零，直接否定了旧解释。当前 RGB 改用 RimGlow 数据流，alpha 只执行一次三分之一；`analyze_native_gbuffer_payloads.py` 已把这两条写入门禁。Player 四姿态中 RGB 分别覆盖 8,554 / 8,469 / 7,686 / 9,393 像素，alpha 分别覆盖 3,034 / 3,348 / 1,566 / 2,656 像素，两路分别具有非零校验和，确认它们没有再次混成同一发光字段。

Body_1/Body_2 的背面路径按 `_DoubleSided=1`、`_DoubleUV=3` 使用 UV3；HairShadow 辅助几何不作为表面写入。Face 使用皮肤类，Eye 与 Eyebrow 使用模板 144；身体皮肤类由 `_OtherDataTex.r` 与各材质 `_SkinMatId` 计算。唯一 `_UseAlpha=1` 的 Body_1_T 仍走同一透明裁剪规则。

Player 在 RTX 5070 Ti Laptop GPU / D3D11 上对 `combat-static`、Idle 第 45 帧、Walk 第 135 帧、Run 第 225 帧做 640×360 全目标读回。四次分别覆盖 30,312、31,479、24,000、27,242 个角色像素；模板覆盖与 o3 覆盖每次完全相等。普通模板分别为 30,278、31,447、23,983、27,210 像素，眼眉模板分别为 34、32、17、32；皮肤类分别为 1,732、1,761、2,084、1,702。世界法线最大单位长度误差不超过 0.001782；首次静止帧全部保持中性运动码，R10 量化误差为 0.000444；Z 标志严格为 0。四个姿态的位置校验和均不同，证明 MRT 来自实时蒙皮而非静态图。

运动历史按 60 Hz 的每个动画帧推进。探针使用 Unity 的 [`SkinnedMeshRenderer.BakeMesh`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) 获取当前 CPU 蒙皮快照，为 27 个可见 SMR 各保留上一帧局部顶点和对象矩阵，并同时保留上一帧相机 VP。Shader 使用游戏生产程序中已提取的公式：先计算当前与上一帧 NDC 差值，再对 `(dx,-dy)` 做带符号平方根压缩并偏置到 `0.498039216`。Idle、Walk、Run 三个采样分别有 31,479、24,000、27,242 个角色像素偏离中性码；历史帧序号为 46、136、226，证明读回前没有在同一帧重复渲染并把速度归零。对应 R10 X/Y 范围分别为 `0.501466–0.502444 / 0.501466–0.502444`、`0.466276–0.529814 / 0.519062–0.566960`、`0.448680–0.556207 / 0.445748–0.553275`。

机器报告为 `live-native-gbuffer-probe.json`，调试预览为 `native-live-*-primary/auxiliary/motion-class/normal/depth-stencil.png`。模板读回已处理 D3D11 的 `uint2.g` 约定；本机没有发生 Y 翻转。查看器面板可用 `Live native MRT probe` 开关禁用离屏更新，探针不会改变可见相机输出。

当前边界保持明确：o0 是现有 HoyoToon 前向材质主体，不是游戏材质 G-buffer 的逐位复刻；G-buffer Pass 已抑制它的 Forward RimGlow，避免与 o1 重复相加。o1 已按捕获纠正为 RimGlow RGB 与发光累加 alpha，但 o0/o1 的材质数学仍是结构适配。o2.xy 已实时生成变形运动，o2.z 仍为 0，因为现有 Remielle 抓帧没有需要启用的功能位。R10 目标已经无损展开并进入已验证的角色 Deferred；动态阴影也进入此离屏生产链。下一步移植 o0/o1 原生主体并接入可见相机。

`analyze_native_live_material_port.py` 进一步检查了完整原生材质主体的输入。结果为 19 个 Remielle draw、6 个 PS、2 个 VS 的接口 19/19 完整；每个 draw 都保存了 cb0–cb4 与全部已绑定纹理槽的无损 mip0 `.buf/.dds`，合计 215 个纹理绑定、39 份唯一捕获载荷、24 份唯一 PS 常量缓冲载荷。19 个实时网格/子网格也全部解析。旧严格联接未纳入的 Hair 项只在 UV1 有差异，而普通和面部两套捕获 VS 均只消费 UV0/2/3，故不阻断原生 PS。原始 F8 DDS 的边界为 2 份完整 Texture2D、33 份 Texture2D 只有 mip0、1 份八层数组只有第 0 层 mip0；这是采集文件的边界，不是游戏 GPU 资源本身缺 mip。详见 `native-live-material-port-readiness.json/.md`。

`generate_native_material_binding_manifest.py` 已把 215 个槽展开为逐 draw 运行时契约，并按 SHA-256 归并 39 份唯一 payload（122.86 MiB）：36 份捕获静态纹理、1 份四层 R16 主阴影、1 份 R16 角色阴影、1 份 64×128 字节的结构化场景/胶囊缓冲。格式分布、尺寸、mip、数组层、Shader 访问操作、精确源贴图身份和每个槽的动态替换策略都在 `native-material-binding-manifest.json/.md`。旧 API 日志恢复了三个真实 D3D11 sampler handle 和七种 PS/材质组合；`77bdd348772c62c8` 的 s3 在 draw 374 与 draw 375–383 使用不同状态对象，证明实际移植不能只给每个 PS 写死一组 sampler。旧日志没有 `D3D11_SAMPLER_DESC` 字段；实验 DLL 支持记录这些完整字段，但被 XXMI 官方签名校验拒绝；已恢复原版，这部分新数据尚未采集。

`AssetGraphProbe` 的源纹理检索现已扩展到全量本地元数据，并支持 BC1 和 Crunch 原流。原有 25 条二维纹理 mip 链之外，本轮补回 3 条直接源流与 5 条 Crunch 内完整 BC1 链；连同 8 层 × 5 mip 的 matcap 数组及两份原本完整的捕获，现为 **36/36 完整**，暂存总量 149,987,032 字节。原有 28 份完整数据不变。新恢复 91 级 mip、17,467 个抽样点已在 Unity 与独立同 GPU D3D11 上传间逐位核对。完整来源、方法、重跑步骤见 [源纹理恢复报告](../../RemielleDataAcquisition/20260904/source-completion-plan/README.md)。新资产已进入正式 Unity 工程，本轮未重新打包可见 Player。

`RemielleNativeTextureImportAudit` 依据每份资源实际暂存的 mip/层数构造 Unity 原生 `.asset`，避开 Unity DDS 导入器对 RGBA8 的拒绝以及对 BC6H/BC7 的翻转和 mip 丢失。D3D11 编辑器审计为 36/36 通过：35 个 Texture2D、1 个 Texture2DArray；BC1、BC6H、BC7、RGBA32 与 RGBAHalf 的 GraphicsFormat 和 sRGB/linear 均与清单一致，matcap 的 8×5 布局也已核对。详见 `source-texture-data/manifest.ndjson`、`native-material-texture-assets.json` 与 `native-material-texture-import.json`。

旧 F8 丢失子资源的原因已回到同版本源码确认：3DMigoto `FrameAnalysis.cpp` 先对整个资源执行 `CopyResource`，随后调用 DirectXTK `SaveDDSTextureToFile`；原 `ScreenGrab.cpp` 把 `mipMapCount` 和 `arraySize` 固定为 1，并只映射 subresource 0。已从官方 `SpectrumQT/XXMI-Libs-Package` commit `48c24f8d19b48909a2a4405c64394f42d35e9796` 构建本地 1.3.16 补丁，按 `array item -> mip level` 写出所有子资源。WARP D3D11 回归逐字节验证单层 legacy DDS 和 2 层 × 4 mip DX10 DDS 后，补丁 DLL 曾安装到 ZZMI，但实机被官方签名校验拒绝，现已用 SHA256 锁定备份恢复官方原版。离线导出通过不代表启动兼容。源码、补丁、DLL、还原脚本和 `build-report.json` 位于 `../../RemielleDataAcquisition/20260904/full-mip-frame-analysis/`。依据：[3DMigoto FrameAnalysis](https://github.com/bo3b/3Dmigoto/blob/master/DirectX11/FrameAnalysis.cpp)、[Microsoft DirectXTK ScreenGrab](https://github.com/microsoft/DirectXTK/blob/main/Src/ScreenGrab.cpp)、[DirectXTex DDS I/O](https://github.com/microsoft/DirectXTex/wiki/DDS-I-O-Functions)、[`ID3D11SamplerState::GetDesc`](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11samplerstate-getdesc)、[`D3D11_SAMPLER_DESC`](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_sampler_desc)。

`Verify-And-Import-RemielleFullMipCapture.py` 已把下一次 F8 的识别、校验和回填自动化。8 个目标均保存旧 draw/槽、3DMigoto 资源哈希、格式/尺寸和旧 mip0 SHA-256；新抓帧即使 draw 序号变化，也会用“描述符相等 + mip0 逐字节相同”重新定位。只有声明 mip 数、实际完整负载字节数和完整文件 SHA-256 全部通过，且目标 draw 的 PS sampler 事件取得至少三种稳定的完整描述符时，才会复制到 `recovered-runtime-textures/`。旧抓帧负向对照为找到 8/8、完整 0/8、实际 mip 均为 1且无描述符；完整链正向回归为纹理 8/8、sampler handle 3/3。待兼容启动器的采集流程验证并获得真实新数据后，才可运行 `Verify-And-Import-NewCapture.ps1` 回填；当前原版 F8 无法补齐这些数据。签名恢复证据见采集包 `launcher-signature-recovery.json`。

`generate_native_material_shader.py` 已把六份不可变反编译 PS 机械改名并生成到 `Assets/RenderingReview/Shader/GeneratedNative/`，组成 6 Pass、四 MRT 的编译候选。Unity 6 / D3D11 的 ShaderUtil 门禁结果为 supported、6/6 Pass、0 error；28 条 warning 全部是反编译 `cmp` 真值以 `-1` 表示后转无符号数的同一种保真提示，已逐条限制，其他警告不接受。当前 Vertex Stub 只用于验证 TEXCOORD0–8 链接，Shader 不进入可见渲染。

捕获的 cb0–cb4 已实际进入 Player 绑定门禁。生成器把六个变体统一为 `209/29/27/41/170` 个 float4 的相同布局，`RemielleNativeMaterialConstantBufferProbe` 用 `GraphicsBuffer.Target.Constant` 和 `Material.SetConstantBuffer` 给六 Pass 逐项绑定 draw 374 的五份原始字节。RTX 5070 Ti / D3D11 报告 `supportsSetConstantBuffer=true`、偏移对齐 256 字节；六个原生 Pass 均成功 `SetPass`。独立的同布局诊断 Shader 又分别把五个捕获缓冲的首个 float4 写入 RGBA32F，五次 GPU 读回均与原始二进制一致，因此这里验证的是实际上传/绑定/着色器读取链，而不只是 API 能力标志。`Material.HasConstantBuffer` 对这组 HLSL 缓冲返回 0/5，符合它只查询 ShaderLab 属性的文档语义，验收不再把这个反射结果误当成绑定成败。19 个 draw 的捕获缓冲全部不短于相应声明。参见 [Unity 6 `Material.SetConstantBuffer`](https://docs.unity3d.com/cn/6000.0/ScriptReference/Material.SetConstantBuffer.html)、[`GraphicsBuffer.Target.Constant`](https://docs.unity3d.com/kr/current/ScriptReference/GraphicsBuffer.Target.Constant.html)、[`Material.HasConstantBuffer`](https://docs.unity3d.com/cn/2023.1/ScriptReference/Material.HasConstantBuffer.html) 与 [ShaderLab sampler states](https://docs.unity3d.com/kr/2018.3/Manual/SL-SamplerStates.html)。

RenderDoc 替代工具的离线验证与游戏启动退出日志继续保留，但八份纹理已由本地源数据补全，不再依赖新 RDC。下一步确认 3 个运行时 sampler 的实际描述符，接入 t1 StructuredBuffer 和真实 TEXCOORD0–8 顶点桥，再将原生 PS 接到可见相机。源 TextureSettings 已保存，不能冒称等于实际 GPU 创建状态。官方 XXMI 文件保持原样。

`RemielleNativeDeferredProbe` 直接使用已经对战斗、展示和商店三组抓帧逐值验证的 `CapturedDeferredCharacter523.shader`。离线审计默认 `Stencil Comp Always`；实时材质把同一 Pass 切为 `Ref=128 / ReadMask=128 / Comp=Equal`，因此只处理 bit 7 角色区域。`T0/T4` 来自 R10 零误差展开，`T1` 直接绑定 D32S8 depth 子资源，`T2/T3` 绑定实时 o0/o1，`T5` 是由原捕获字节生成的 1024×32 RGBAHalf 场景 LUT。相机位置、视图基向量、逆 VP、分辨率、深度线性化和主光方向每帧更新；其余场景/雾/调色常量仍使用已确认的战斗捕获值。

`analyze_live_deferred_constants.py` 进一步逐寄存器连接原生 HLSL、Shader 反射和 C# 写入。draw 523 的 185 个 float4 中实际读取 36 个；其中 12 个由当前相机、灯光和 LUT 尺寸每帧重算，20 个是明确选择的战斗雾/FXCC profile，3 个是 Rim、Motion Mask、角色 LUT 的捕获功能开关，1 个只读取 `_MainLightColor.w=1`。当前没有“已读取但未分类”的隐式常量。完整表在 `live-deferred-constant-audit.json/.md`；雾和 FXCC 仍是选定 profile，不代表场景管理器已经复刻。

四个 Player 样本的 resolve 写入像素与各自 G-buffer 覆盖完全相等，分别为 30,312、31,479、24,000、27,242；每个写入像素的 R11 HDR 均非零，背景非模板泄漏为 0。o2/o3 的 R10 展开在四份全图上逐通道误差为 0，四份输出校验和互不相同。预览为 `native-live-*-resolved.png` 与 `native-live-*-resolved-auxiliary.png`。这证明实时蒙皮字段、变形运动、格式转换、模板分类、深度和原生消费者已经连通；由于上游 o0/o1 与部分常量仍是适配/捕获值，预览不能作为最终游戏画面对齐结果。

本次实现还复核了 Unity 官方的 [平台渲染差异](https://docs.unity3d.com/cn/current/Manual/SL-PlatformDifferences.html) 与 [ShaderLab ZTest](https://docs.unity3d.com/cn/current/Manual/SL-ZTest.html)：D3D11 使用反转 Z，`GL.GetGPUProjectionMatrix` 会返回平台矩阵；几何深度仍使用 ShaderLab 的 `LEqual` 前后关系语义。实际 Player 深度范围和模板覆盖均由 GPU 读回验证。
