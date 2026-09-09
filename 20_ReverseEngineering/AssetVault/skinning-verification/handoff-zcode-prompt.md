# 交给 zcode 的检查与更新提示词(用户粘贴用)

---
## 背景(必读,这是本轮交接的上下文)

你的上一轮会话在"蒙皮数学是否收敛"处终止。现在该问题已由外部代理(DSH/DeepSeek)接替完成定案,你在本任务里的角色是:**核查这次交接的改动,然后执行后续更新**。

**蒙皮数学定案结论**(所有细节与证据在此文件):
`E:\ZZZ\local-only\RemielleAssetVault\skinning-verification\skinning-math-derivation.md`

核心事实:
1. `recover_meshes.py`(2026-09-04 00:56 版)的蒙皮数学**已收敛、正确**,无需再改:
   - JSON 键 `M{col}{row}` = 元素(row,col),列主序解读(你的两个修复都在位);
   - IBM = S·B·S、节点世界 = S·W·S,S=diag(1,1,-1,1),均为 glTF 列主序写入;
   - 绑定姿态下 W·IBM = I 误差 4.9e-8(float32 精度),蒙皮输出==作者顶点 2.4e-7;
   - 27/27 修复版 GLB 全量审计 PASS(`skinning-verification\batch_audit.json`);
   - Blender 5.2 第三方求值:修复版偏差 2.6e-7 m;Unity 里部署的旧版(00:13)偏差 1.42 m。
2. **V1 塌陷的真正原因**:`Assets\SourceAssets\Meshes\` 里的 GLB 是修复前旧版(IBM 转置),
   修复版早已生成在 `E:\ZZZ\local-only\RemielleHoyoToon\_fix_test_full\*.glb`(顶层 27 个,00:56:55),只是从未部署。
3. 你之前"验证反复失败"的根因是验证脚本自身的 `reshape(order="F")` 误用(整数组 F 序交织 ≠ 逐矩阵 F 序),
   不是数据问题。文档 §8 有正确写法,请不要再重复"转置/列主序"纸面辩论。

**本轮外部代理的改动(只动了一个文件)**:
`E:\ZZZ\local-only\RemielleHoyoToon\Assets\Editor\V1SingleBaseline.cs`
- 保留了原有 rootBone→"Bip001 Pelvis" 修复;
- 新增组装侧补偿:实例外层包一个 `V1_Wrapper_Y180`,整体绕 Y 轴转 180°。
  数学依据:写入侧 S=diag(1,1,-1,1) × UnityGLTF 导入侧 C=diag(-1,1,1) → 净效果
  D=C·S=diag(-1,1,-1)=绕 Y 轴 180° 刚性旋转;wrapper 转回后 Unity 场景朝向=游戏作者空间。
  整棵层级刚性旋转不改变骨骼与网格相对变换,蒙皮恒等不受影响。
  实证:旧 V1 日志 bounds center z=-0.88,游戏作者 AABB 中心 z=+0.877,恰好差一个 D。
- **没有**改 `recover_meshes.py`(保持 Z 反射写入),这是有意决定,不要擅自动它。

---
## 第一步:检查(未通过前不要执行第二步的更新)

1. 通读推导文档与 V1SingleBaseline.cs 的 diff,核对:
   - S=diag(1,1,-1,1)、C=diag(-1,1,1)、D=C·S=rotY(180°) 是否成立;
   - wrapper 旋转是否只作用于整棵实例(骨骼+网格同转,相对关系不变);
   - 是否有 C# 语法/作用域问题(等待 Unity 重编译或查 Editor.log)。
2. 交叉核对 `skinning-verification\report.json`、`batch_audit.json`、`renders\` 与上述结论一致
   (必要时重跑 `python E:\ZZZ\local-only\RemielleAssetVault\skinning-verification\verify_skinning_math.py`
   与 `batch_audit_glbs.py`,只读不改)。
3. 若发现本次改动有误:直接修正并在最终报告说明;若发现推导本身有误:停止并报告,不要推翻重做。

## 第二步:检查无误后,更新以下内容(逐项完成并报告)

1. **部署修复版 GLB**:把 `_fix_test_full\` 顶层 27 个 `.glb` 覆盖到
   `Assets\SourceAssets\Meshes\`(尤其 `Remielle_Origin_Body_1.glb`,当前是 00:13 旧版=V1 塌陷元凶)。
2. **V1 重跑验收**:等 Unity 重新导入后执行 Remielle → V1 Single Baseline,读取
   `Renders\v1_single_body.png` 与 Editor.log 的 bounds 日志,验收标准:
   - 完整平躺人形剪影(体轴沿 Z、头部在 +Z 端),无塌缩团;
   - 补偿生效后 bounds center z ≈ +0.88(旧版无补偿时为 -0.88);
   - 画面占比明显大于旧版 3.3%。
3. **V2/V3 组装脚本**(若开始写)统一采用以下模式并写进注释:
   - rootBone 显式 = "Bip001 Pelvis"(绕开 UnityGLTF FindCommonAncestor 包 bug);
   - 实例根包 Y180 wrapper(见 V1);
   - 取景用顶点级 bounds 而非 renderer.bounds;
   - 若要挂动画:节点必须嵌套重建,局部矩阵公式 L_i = W_g_parent(i)⁻¹ · W_g_i
     (W_g 来自 inv(IBM),父链按 unityPath 前缀匹配),平铺构造只保证绑定姿态、不承载动画。
4. **文档与记忆更新**:
   - 在 RemielleAssetVault 的 README/metadata 记录蒙皮约定与结论,链接 `skinning-verification\`;
   - 更新 zcode 项目记忆(zzz 项目):写入"蒙皮数学已定案 + 验证套件位置 + 一键重跑命令",
     并注明"若未来改恢复脚本的矩阵处理,必须先重跑 batch_audit_glbs.py";
   - 记录约定:Vault 写入用 Z 反射,AnimeStudio 官方导出用 X 反射,两者混用需 180° 对齐
     (在未获用户授权前不要改 recover_meshes.py 的反射轴)。

## 最终报告格式

- 检查结论清单(每条:通过/修正说明);
- 更新清单(每条:做了什么、验收数据);
- V1 验收截图与 bounds 日志摘录。
