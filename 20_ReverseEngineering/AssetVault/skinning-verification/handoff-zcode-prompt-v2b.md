# 交给 zcode 的提示词:V2b 材质绑定(用户粘贴用)

---
## 背景与当前状态(必读)

V2a 几何组装已全部验收通过:产物 `Assets\V2a\Remielle_V2a_Assembled.prefab`
(488 骨嵌套共享骨架 + 27 网格,双朝向断言,27/27 PASS)。**本轮任务 = V2b 材质绑定,
只碰材质,不动几何/骨架/组装逻辑。**

必读文档(按顺序):
1. `RemielleHoyoToon\PROGRESS.md`(进度与 V2a 验收数据);
2. 工作区 `AGENTS.md`(朝向坑:站立旋转=Euler(90,180,0),组装不得改表现层);
3. `RemielleAssetVault\skinning-verification\skinning-math-derivation.md`(数学定案,勿重开辩论);
4. zcode 项目记忆里 RemiModer 时期的材质复原结论(分层 ramp/脸 SDF/matcap/描边/丝袜偏红事故等,全部直接适用);
5. 游戏材质真值:`RemielleAssetVault\recovered\materials\material_recovery_report.json`
   与各材质 JSON(30 个游戏材质,10 个 Remielle 槽:MAT_Remielle_*)。

## 任务清单(按顺序)

1. **HoyoToon 包导入(先授权)**:`Packages\manifest.json` 目前无 HoyoToon 依赖。
   **导入前先向用户报告来源与版本并取得授权**(下载红线,即停即清)。
   若用户不批准联网下载,改为评估用工程内/本地已有的 shader 资产(如 UnityGenshinToonShader 对照基准)作为临时方案。
2. **材质绑定脚本(新 Editor 菜单,独立于 V2aAssembly.cs)**:加载已验收的
   `Remielle_V2a_Assembled.prefab`,按 10 个材质槽 JSON 逐个 SMR 绑定 HoyoToon 材质:
   - 参数**严格取游戏材质 JSON 真值**(分层 ramp 色 _ShadowColor1-5/_ShallowColor1-5、
     matcap 槽与 tier、_FresnelColor、_OutlineColor/width、_AOParameters 等),不臆造;
   - 贴图引用 `Assets\SourceAssets\Textures\` 已有 115 张(_D/_M/_N/MatCap/脸 SDF/Eye_E 等);
   - 材质创建走 HoyoToon 官方菜单/管线,**不手写 .mat 绕过**(历史教训:Unity6 batch 下
     EnableKeyword 不持久化 → 分支一律用浮点参数驱动,不要关键字)。
3. **历史坑执行清单(逐条自查)**:
   - 数据图(_M/_A/_N/脸 SDF/Eye_E)导入设置:**不压缩 RGBA32、_N 用 Default 导入**(不是
     NormalMap,DXT5nm 会清掉 B 通道=diffuse bias 真值);
   - 亮面=纯 albedo、只暗端乘影色;shadow→shallow→lit 三段式(丝袜偏红事故:引擎侧
     Post tint 不是材质真值,取中性值,别用 HoyoToon 默认偏粉值);
   - 脸分支用 _MaterialType 浮点判定走 SDF 光照(_FaceShadowTex= Female_Face_Lightmap_02);
   - 眼睛 eyeshadow LUT:顶点色 R×255 索引 Eye_E 16×16;
   - 描边:游戏原生网格**没有**平滑法线 UV3 烘焙——先回退渲染法线,并把"是否在原生
     网格上烘焙 UV3 平滑法线"列为问题项向用户确认(不擅自动几何)。
4. **验收(程序化 + 人眼)**:
   - 程序化:每 SMR 材质槽数正确、贴图引用非空、无 shader 编译错误、抽样核对材质参数
     == 游戏 JSON 真值;材质改动**不得触碰 mesh/bones/bindpose**(可用 V2a 的 CPU 蒙皮
     恒等复跑确认零几何回归);
   - 渲染:四视角 + 脸/身体/武器/翅膀特写;与游戏本体参考帧 A/B(视觉权威=游戏本体);
   - **用户在编辑器实看确认**后本轮才算验收通过。
5. **收尾**:更新 `PROGRESS.md`(V2b 结果、参数对照表、遗留项);更新 zcode 项目记忆
   (HoyoToon 绑定模式、真值来源、新坑)。
   顺手可修的两个 V2a 遗留小项(可选):mask SMR rootBone=null(小骨架无 Pelvis,可给
   mask 单独指定 rootBone);prefab 内 mask 实例名 "(Clone)" 外观瑕疵。

## 约束

- 只碰材质与贴图导入设置;**不改** V2aAssembly.cs 组装逻辑、manifest、GLB、
  recover_meshes.py、表现层旋转;
- 下载 HoyoToon 包前必须先向用户授权;
- 不重新推导蒙皮数学(已定案);材质公式有疑问时以游戏材质 JSON 真值 + 已复原的
  RemiModer 结论为准,不臆造参数;
- 任何"游戏侧没有真值"的公式项(如引擎后期色)按已复原的中性/社区对照结论处理,并在报告里标注来源等级(游戏真值/社区复原/中性近似)。

## 报告格式

- 材质绑定清单(每槽:材质/贴图集/关键参数来源等级);
- 验收数据(材质槽数、贴图引用、shader 报错、参数抽样对照、几何零回归确认);
- 渲染图与 A/B 图清单;
- 遗留项与下一步建议(V2b 打磨项、V3 动画)。
