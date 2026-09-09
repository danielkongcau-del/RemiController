# Remielle 文件与实机数据采集结果 · 2026-09-04

本轮已取得静态缺口的源数据，以及用户提供的三种场景的无损 GPU 抓帧。去重存档 **4,376 个文件、8,325,208,221 字节**，全部文件尺寸与完整 SHA256 校验通过，失败 0。完整抓帧目录仍保留在 `E:\ZZZ\FrameTools\XXMI Launcher\ZZMI`，本目录归档角色及相关渲染阶段的必要资源，不重复打包全部 UI/场景资源。

**这份文档记录采集当时的发现。后续正式材质、GLB、prefab、LUT/透射、头发 UV 和驱动均已回填并通过当前模型验收；使用状态以 [交接文档](../../RemielleHandoff/README.md) 为准。下文的“当时预览”和待办描述仅表示采集时点，不表示当前仍有同样错误。**

| 用户采集顺序 | 原始抓帧目录 | 已确认内容 |
|---|---|---|
| 1 · 角色展示页 | FrameAnalysis-2026-09-04-144810 | 原皮角色、展示用 shader、原生角色 LUT |
| 2 · 时装商店页 | FrameAnalysis-2026-09-04-144910 | 原皮全身、展示用 shader、原生角色 LUT |
| 3 · 试玩实战 | FrameAnalysis-2026-09-04-145107 | 原皮角色与翅膀、战斗材质/灯光、角色 LUT、最终后处理 LUT |

三帧均有完整 API 日志、VB/IB/常量缓冲原字节、纹理 DDS 和资源描述。连同两份历史对照，本目录归档 469 次相关绘制，完整性清单为 `artifact-manifest.json`，场景身份为 `captured-scenes.json`，最终校验为 `verification.json`。

| 数据 | 获取与核验结果 | 入口 |
|---|---|---|
| 原皮静态材质槽 | 正确源模型 27 个 SMR、31 个槽、13 个 Material；逐项核对源 block/CAB/fileID/pathID/类型 | `base-model-material-bindings.json`、`base-model-materials/` |
| 材质贴图 | 59 个非空槽对应 40 个 Texture2D，全部从正确目标 CAB 导出；PNG 解码、尺寸、SHA256 通过。13 个材质中有 12 个包含非空纹理引用 | `base-model-texture-bindings.json`、`base-model-textures/` |
| 此前 24 个纹理缺口 | 18 张精确来源纹理及 24 条引用关系已收集，包含目标目录、身份与导出清单 | `source-evidence.json`、`blobs/`；原始整理在 `E:\ZZZ\local-only\workspace-repair-20260904\resolved-gap-textures` |
| 此前两根占位骨 | 已取得 Skn_Ori_B_Lingzi_01/02 的身份、CRC32 路径及从自身到根节点的 9 个不同 Transform 原始 JSON | `bone-source-transforms/`、`source-evidence.json` |
| 正确原皮源模型的完整骨架数据 | 486 个 GameObject、486 个 Transform、1 个 Avatar、1 个 Animator 均已精确导出，共 974 项，全部成功 | `base-model-rig/manifest.ndjson` |
| 角色 LUT | 展示/商店共用一份、试玩另一份；1024×32，RGBA16F，采样与曝光参数已核对 | `lut-parameters.json`、`lut-payloads.json` |
| 最终后处理 LUT | 试玩的 4096×64 RGBA8 UNORM 原数据已取得，原生精度本来就是 8 位；历史 JPEG 不再充当真值 | `lut-payloads/`，原 DDS 保留在 `blobs/` |
| Wings 透射 | 实战 shader `8fc7e589644cb3c4`、PS t13 贴图、全部声明输入与常量已取得；源 PNG 经上下方向对应后与原生 DDS 每个 RGBA 像素完全一致 | `wings-transmission-evidence.json`、`exact-shader-variants.json` |
| 着色器 | 68 个原始 DXBC 哈希已核验。另一个 `54ac269badc5b38c` 已有实际运行时反汇编，尚无可独立核验的原始 DXBC，不将它计入 68 个 | `shader-evidence.json`、`native-shader-disassembly.json` |
| 网格与实机绘制的对应 | 298 次绘制的索引顺序与全部源 UV 数组唯一匹配，涉及 20 个命名网格；26 个候选源 JSON 与现有恢复 GLB 已归档 | `runtime-mesh-fingerprint-joins.json`、`runtime-assembly-candidates.json` |
| 材质候选与绘制的对应 | 新帧 116 次绘制包含逐像素吻合的源纹理；44 次同时收敛到唯一源材质候选和唯一网格指纹，共 14 种网格/子网格/材质组合 | `runtime-texture-material-joins.json`、`runtime-draw-binding-evidence.json` |

LUT 已另外提供原样 texel 数据（`.rgba16f` / `.rgba8`）与 `.npy`，逐字节往返一致，共 3 份不同像素内容。没有翻转 LUT、套用 sRGB、曝光或转成普通 PNG。角色 LUT 与最终后处理 LUT 属于两个阶段，不能互换或重复套用；展示与战斗的灯光/曝光也应分别消费。

Wings 原生纹理格式为 **BC7_UNORM_SRGB**。纹理匹配时的上下翻转只是坐标对应证明，没有改动原 PNG 或 DDS；它不能证明现有 HoyoToon `_T` 导入及透射算法已经正确。源 shader 同一份 DXBC 对应 LOW 和 MIDDLE 两个关键词变体，不能凭相同字节码区分当时使用了哪一个质量标签。

必须修正的旧结论：

- `build_visual_evidence_boundary.py` 过去仅按 CAB 名索引，不同源块的同名 CAB 会覆盖彼此的 external 表。63 个 CAB 存在不同表，旧报告的 57 条非空指针受影响（27 个 Mesh、30 个 Material）。索引已改为源 block + CAB，并加入目标类型和多来源歧义检查；真实碰撞回归通过，3,538 条 renderer 记录已重新生成到 `source-scoped-renderer-bindings.json`。
- 原 `1436584839/CAB-f56...` 的 fileID 13 正确目标是 `CAB-a54cb8ece006bf575b732cd3ab36fd55`，不是旧报告中的 c6f。读取错误目标只找到 distortion 材质，不能证明原来的身体/脸 Mesh 不存在。
- 正确 a54 模型位于 `2906705493.blk`，其 27 个 SMR 的 m_Mesh 全为空；已核对文件声明对象表与解析对象表，均无 Mesh。这份 prefab 证明静态材质槽和骨架，不直接证明实机 Mesh 引用。
- **采集当时的旧预览与实机身体是同名的不同来源版本**：预览来自 `1436584839/CAB-1d137...`，Body_1 为 15,845 顶点、1 子网格，Body_2 为 13,663 顶点；实机精确匹配 `261957958` 中的 Body_1（15,705 顶点、2 子网格）与 Body_2（14,065 顶点、1 子网格）。名称都包含 Origin，不能靠名称判断版本。已归档两份正确候选 GLB，其恢复报告没有未解析骨哈希。
- 两根已定位的旧占位骨属于 15,845 顶点的旧版本。它们的源身份数据有效，但不能按骨索引直接搬到另一个版本；更换身体版本后应按该版本骨架重新绑定。

采集当时记录、后来已完成正式回填的事项：Hair 的源 UV1 与实机缓冲存在差异，不能把拓扑一致说成全部顶点数据一致；部分附件本次未达到唯一材质候选；GPU 记录不包含 Unity 对象实例 ID。已取得这些实机原缓冲，不需要通过猜测补值。此后已依据正确版本和原皮骨架生成绑定表，回填辅助纹理缺口，适配 LUT/透射并完成模型验收。采集快照中的 `allRequiredDataAcquired=false` 保留的是这些尚未完全闭合的严格证据范围，不代表两套 LUT 或本次三帧仍然缺失。

采集故障已处理：XXMI 启动时会把 d3dx.ini 的 hunting 设置覆盖为 0，最初 F8 无反应由此造成。采集期间修正了启动器对应开关并用 F10 重载；三帧完成后，启动器开关及本次改动的 hunting/analyse_options/export_shaders 均已恢复。备份和恢复状态在 `capture-before/`、`capture-config-state.json`。未代操作 GUI、未修改游戏源数据、未上传私有资产。

复用入口：`tools/collect_render_evidence.py --frame <目录名>`、`tools/analyze_acquired_evidence.py`、`tools/join_runtime_meshes.py`、`tools/join_runtime_textures.py`、`tools/prepare_lut_payloads.py`、`tools/finalize_acquisition.py`。先核对新采集的场景身份与输入范围，再运行分析；不要将历史捕获参数当作其他场景的固定值。
