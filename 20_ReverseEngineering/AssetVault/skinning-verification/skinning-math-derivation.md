# 蒙皮数学推导定案（Remielle GLB 恢复管线）

> 结论先行：**当前 `recover_meshes.py`（2026-09-04 00:56 版）的蒙皮数学已经收敛、正确**。
> 三套独立验证（推导链数值复核、字节级独立重建比对、Blender 第三方求值）全部通过。
> Unity V1 渲染塌陷的原因不是"数学没推对"，而是 **Unity 工程里部署的 GLB 是修复前的旧版**（00:13 生成，IBM 被转置写入）。
> 修复版 GLB 已存在于 `_fix_test_full\*.glb`（00:56:55 生成），27/27 通过全量审计，只差覆盖部署后重跑 V1。

---

## 0. 问题背景与现状盘点

| 物件 | 时间 | 状态 |
|---|---|---|
| `recover_meshes.py`（Vault 恢复脚本） | 2026-09-04 00:56:20 | 数学收敛（平铺 bind-exact 构造）✅ |
| `_fix_test_full\*.glb`（27 个，顶层） | 2026-09-04 00:56:54~55 | 全量审计 27/27 PASS ✅ |
| `_fix_test_full\<blk>\...\*.glb`（嵌套版，00:36） | 2026-09-04 00:36 | **勘误（2026-09-04 02:20 复测）：并非"同样 PASS"。** 00:36 嵌套代是修复完成**前**的产物，实测 17/27 PASS、10 FAIL（失败集 = Body_1/Body_2/Face/Hair/Wings/Ramiel_Mask/Sticker_01/Weapon_01/Weapon_02_L/Weapon_02_R，max\|W·IBM−I\|≈2.0，转置特征，见 `batch_audit_nested.json`）。初版本文此处声明有误——当时用的还是带 reshape 陷阱的旧校验脚本（§8）。**任何部署只能用 `_fix_test_full\` 顶层 27 个文件。** |
| `Assets\SourceAssets\Meshes\`（01:25 部署） | 2026-09-04 01:25 | **部署事故**：27 个文件经 SHA-256 核对全部来自上述 00:36 嵌套旧代（而非顶层修复版），故 17 PASS/10 FAIL。02:10 已用顶层修复版覆盖重部署（哈希逐一核对 27/27 一致），重审计 27/27 PASS（`batch_audit_deployed_after_fix.json`）。❌→✅ |
| `Assets\SourceAssets\Meshes\`（02:10 重部署后） | 2026-09-04 02:14 | **V1 重跑验收通过**：SMRs=1，verts=15845，bones=118，rootBone=Bip001 Pelvis；vertex bounds center=(0.00,0.16,0.88) size=(0.98,0.61,1.30)，z 跨度 [0.23,1.53] 与骨骼 FK 真值（Pelvis 0.932/Head 1.417）吻合；剪影覆盖率 4.88%（紧凑，旧塌陷版为 27.4% 散片），最大连通域占剪影 91.5%。✅ |

所有旧文件均未改动；本目录（`skinning-verification\`）是全新产物。

---

## 1. 约定与记号

**列向量约定**（Unity 与 glTF 都是）：点写作 4×1 列向量，变换为左乘 `M·p`；
平移分量位于 M 的第 4 列（索引 0..2 行、3 列）。

**坐标手性**：Unity = 左手系（Y-up）；glTF = 右手系（Y-up）。
本管线用 **Z 轴反射共轭** 在两者间转换：

```
S = diag(1, 1, -1, 1),    S⁻¹ = S
```

对矩阵做共轭 `S·M·S`（= 相似变换），对向量做镜像 `S·v`（z 取反）。
因为 det(S) = -1 且左右各乘一次，**det(S·M·S) = det(M)**：
旋转块仍是真旋转（det +1），不会把骨骼变成镜像。

**游戏数据的坐标系事实**（与推导无关但影响取景）：bind 空间里角色沿 Z 轴平躺
（Pelvis 在 z≈+0.932，Head 在 z≈+1.417，单位米；站立姿态由游戏 Animator 的预制体层级完成，不在 mesh 数据里）。
所以修复后的 GLB 渲染出来是"平躺的人形"——这是作者空间的真实形态，不是 bug。

---

## 2. 环节一：JSON `m_BindPose` → 真实 Unity bindpose 矩阵 B

AnimeStudio 导出的每个 bindpose 是 16 个键，**插入序**为
`M00, M10, M20, M30, M01, M11, ...`（列主序存储顺序），**键名语义为 `M{col}{row}`**，
即：键 `M{col}{row}` 存的是数学矩阵的**元素 (row, col)**。

```
B[row][col] = value["M{col}{row}"]          （正确读法，现代码）
B[row][col] = value["M{row}{col}"]          （旧代码读法 = Bᵀ）
```

**证据链**（对 543 个 mesh JSON 全部 118/543 个矩阵成立，见 `verify_skinning_math.py` 阶段 A）：

1. 正确读法下，仿射底行 `(B[3][0..3]) = (M03,M13,M23,M33) = (0,0,0,1)`（偏差 0.0）；
   平移落在 `(M30,M31,M32)`，模长 0.618 ~ 1.417（米级，非零）。
2. 旧读法下，末列 = (0,0,0,1)，**全部骨骼平移恒为 (0,0,0)**——543 个矩阵无一例外。
   骨骼全部退化到原点在物理上不可能，直接证伪旧读法。
3. 正确读法下旋转块正交误差 1.3e-5、det ∈ [0.99999, 1.00000]：真刚性变换。
4. 交叉真值：`inv(B)`（= 绑定世界矩阵）给出 Pelvis 高 0.932 m、Head 高 1.417 m，
   与 `zcode_bone_hierarchy.json` 的 FK 层级段长比值 100.0 精确吻合（层级单位是 cm，Bone_Root scale=0.01）。

因此每个关节的**绑定世界矩阵**（Unity 左手系）为

```
W_i = B_i⁻¹
```

---

## 3. 环节二：坐标共轭（Unity LH → glTF RH）

```
IBM_i = S · B_i · S                      （逆绑定矩阵，glTF 空间）
W_g_i = S · W_i · S = inv(S·B_i·S)       （绑定世界矩阵，glTF 空间）
```

配套的向量规则（现代码已全部正确实现）：

| 数据 | 规则 | 原因 |
|---|---|---|
| 顶点 POSITION | `z *= -1` | 逆变向量镜像 |
| 法线 NORMAL | `z *= -1` | 共变向量（反射后仍垂直于面） |
| 切线 TANGENT | `z *= -1, w *= -1` | xyz 逆变镜像；w 因手性翻转取反 |
| 三角形绕序 | 每三角形交换 index[1]↔index[2] | 反射翻转面朝向，反转绕序保持正面 |

---

## 4. 环节三：glTF 编码（字节序）

glTF 规范（3.6.2.2）：`MAT4` accessor 与 `node.matrix` 都是**列主序** 16 浮点，
即字节流顺序 = 矩阵第 0 列 4 个元素、第 1 列 4 个元素、……

numpy 对应关系（这是历次"验证自我矛盾"的高发区，见 §8）：

```
gltf 列主序字节流 = 逐矩阵 m.transpose().astype(f4).tobytes(order="C")
                  = 逐矩阵 m.flatten(order="F") 再整体 C 序写出
```

现代码的两处写入都正确：

- `node.matrix`：`gltf_matrix(global_bind[i])` = `flatten(order="F")` ✓
- IBM accessor：`inverse_bind.transpose(0,2,1).reshape(N,16).astype(f4)` 后 `tobytes("C")` ✓
  （转置 + C 序 = 列主序，语义与 `flatten("F")` 完全一致）

**节点构造（平铺 bind-exact）**：每个关节直接挂在场景根下，`node.matrix = W_g_i`。
于是关节世界矩阵 = `W_g_i`（无层级乘积）。这是"绑定精确"构造：

- 优点：`W · IBM = I` 由构造保证，静态/基线渲染 100% 还原作者形状；
- 局限：**不承载动画**（子关节不随父关节运动）。V3/V4 挂动画时必须改为嵌套构造：

```
嵌套构造:  L_i = W_g_p(i)⁻¹ · W_g_i      （p(i) = 按 unityPath 前缀求出的父关节）
           node_i.children 挂入父节点，node_i.matrix = L_i（列主序）
```

现代码已计算 `parent_joint`（按 unityPath 前缀匹配），只是未使用（死代码，无害），
为嵌套重构留好了接口。

---

## 5. 环节四：核心恒等式（蒙皮数学的"收敛点"）

glTF 蒙皮公式（bind 姿态、网格节点为单位阵）：

```
v' = Σ_j w_j · (W_g_j · IBM_j) · v
```

代入本管线：

```
W_g_j · IBM_j = (S·W_j·S) · (S·B_j·S)
              = S · W_j · (S·S) · B_j · S
              = S · W_j · B_j · S
              = S · I · S          （W_j = B_j⁻¹）
              = I                  ∎
```

**一行化简，不涉及任何近似**。数值复核（float32 产物）：`max|W·IBM − I| = 4.88e-8`
（= float32 往返精度），蒙皮输出顶点与原始顶点逐分量偏差 ≤ 2.4e-7。
这正是"绑定姿态下蒙皮渲染 = 作者几何"的严格含义。

---

## 6. 环节五：UnityGLTF 导入侧（为什么 Unity 里一定正确）

UnityGLTF（org.khronos.unitygltf@74d7064fa339）的关键事实（源码实读）：

1. `SchemaExtensions.CoordinateSpaceConversionScale = (-1, 1, 1)`，记 `C = diag(-1,1,1,1)`；
2. 所有矩阵经 `ToUnityMatrix4x4Convert` 进入 Unity：`M → C·M·C`（node.matrix 与 IBM 同样处理）；
3. `ImporterSkinning.cs`：`bindposes[i] = IBM[i].ToUnityMatrix4x4Convert() = C·(S·B_i·S)·C`；
   关节 Transform 由 `node.matrix.ToUnityMatrix4x4Convert()` 解出，世界矩阵 = `C·(S·W_i·S)·C`；
4. Unity 蒙皮：`v' = Σ w_j · (bone_j.localToWorldMatrix × bindpose_j) · v`。

乘积：

```
(C·S·W_i·S·C) · (C·S·B_i·S·C) = C·S·W_i·(S·C·C·S)·B_i·S·C
                               = C·S·W_i·I·B_i·S·C        （C²=S²=I）
                               = C·S·I·S·C = C·C = I      ∎
```

顶点也被 `ToUnityVector3Convert` 镜像（x 取反），蒙皮乘积恒等对其相容。
**无论 C 是什么对角 ±1 矩阵，恒等性都不受影响**——因为我们对矩阵用共轭、对向量用镜像，
UnityGLTF 的反向转换把两者同时还原。

净效果：导入 Unity 的场景 = 游戏作者空间几何经

```
D = C·S = diag(-1, 1, -1, 1)
```

的刚性变换（= 绕 Y 轴旋转 180°，det +1，保形保距）。**形状完好**，只是整体朝向转了 180°，
V1 取景按实际 AABB 处理即可。

附带结论：`rootBone` 只影响 renderer.bounds 与取景，不进入顶点公式；
UnityGLTF 的 `GLTFHelpers.FindCommonAncestor` 死代码导致 rootBone 恒取 joints[0]
（所以出现过 "Bip001 L Clavicle"），与形状无关——此前把 rootBone 改成 Pelvis 只是修 bounds，方向正确。

---

## 7. 历史两个 bug 的精确刻画（为什么 V1 塌陷）

| # | bug | 数学后果 |
|---|---|---|
| 1 | `matrix_from_unity` 用 `M{row}{column}` 读（旧版） | B 变成 Bᵀ，全部骨骼平移归零，骨架退化到原点 |
| 2 | IBM accessor 直接 `tobytes("C")`（旧版） | 写入字节 = 正确矩阵的**行主序平铺** = 转置（glTF 按列主序解出 `(S·B·S)ᵀ`） |

两个转置在旧版里部分互相抵消，掩盖了 bug 1 的部分症状；最终 00:13 部署 GLB 的实测：

```
部署 GLB:  node world = S·W·S （正确）;  IBM = (S·B·S)ᵀ （转置）
          max|W·IBM − I| = 2.41 → 蒙皮顶点偏移均值 0.45 m / 峰值 2.85 m → 塌缩团
修复 GLB:  node world = S·W·S ;  IBM = S·B·S
          max|W·IBM − I| = 4.88e-8 → 蒙皮偏移峰值 2.3e-7 → 精确还原
```

Blender 第三方复核（用 Blender 自己的骨架求值管线，不经过任何本项目代码）：

```
修复版: maxEvalDeviation = 2.63e-7 m（求值顶点 == 作者顶点）
部署旧版: maxEvalDeviation = 1.42 m（求值 AABB 从 1.0×1.3×0.6 爆散到 2.7×2.9×1.7）
```

渲染剪影佐证（`renders\*.png`）：修复版覆盖 4.7%/8.8%、紧凑人形；旧版覆盖 27.4%、碎片散开。

---

## 8. 为什么之前的"数值验证"反复自我矛盾（方法论教训）

之前会话里验证脚本对同一文件先后得出"IBM = SBS"与"IBM 奇怪值"两种结论，根因是三个叠加陷阱：

1. **`reshape(order="F")` 语义**：`flat.reshape(N,4,4,order="F")` 是"整个 (N,4,4) 数组按 F 序交织"，
   **不是**逐矩阵 `m.reshape(4,4,order="F")`。两者相差一个跨矩阵的排列（本审计脚本初版就踩中，已修正）。
   正确写法只此一种：

   ```python
   ibm = np.array([f.reshape(4, 4, order="F") for f in flat.reshape(-1, 16)])
   ```

2. **node.matrix 是 JSON 里的 16 元列表（列主序）**，而 IBM 是 BIN 里的字节流（列主序）——
   两种载体都要"逐矩阵列主序解码"，混用行主序 reshape 就会各错各的。

3. **三层叠加（转置 × Z 反射 × 列主序）不靠心算**：任何一层错号都会给出"看起来合理"的中间值。
   本定案的验证方法论 = 每层独立单元验证（§2 证据链）→ 参考链对照 → 字节级重建比对 → 第三方求值器（Blender）裁决。
   这样每一层都有独立证据，不存在"纸面推导超过可靠性"的问题。

---

## 9. 收敛判定与剩余事项

**判定：蒙皮数学已收敛。** 依据：

| 验证 | 结果 |
|---|---|
| 全量 27/27 修复版 GLB 审计（`batch_audit_glbs.py`） | 全部 PASS，max|W·IBM−I| ≤ 5.4e-8，蒙皮偏移 ≤ 2.4e-7 |
| 字节级独立重建比对（`verify_skinning_math.py` 阶段 D） | IBM accessor 与 POSITION accessor 逐字节一致，node 矩阵误差 0.0 |
| Blender 第三方求值 | 2.63e-7 vs 1.42 m（修复版 vs 旧版） |
| UnityGLTF 源码级推演 | 导入侧共轭 C 与 S 相容，乘积恒等 |

**待办（按依赖顺序，2026-09-04 02:20 更新）**：

1. ~~**部署**~~ ✅ 已完成（02:10）：顶层修复版 27 个 GLB 已覆盖 `Assets\SourceAssets\Meshes\`，
   哈希逐一核对一致，重审计 27/27 PASS。
2. ~~**V1 取景**~~ ✅ 已完成（02:14）：批处理重跑 `Remielle → V1 Single Baseline`，
   bounds 与骨骼真值吻合、剪影紧凑单一（数据见 §0 表格末行）。
   注意：作者姿态是沿 Z 平躺（不是站立）；wrapper Y-180 只补偿朝向，不改姿态。
   把实例立起来看需再包一层 rotX(-90)，属于 V2 组装/取景决策。
3. **V3/V4 动画**：平铺构造不承载动画。嵌套重构公式见 §4：`L_i = W_g_p(i)⁻¹ · W_g_i`，
   父链用 `parent_joint` 的 unityPath 前缀匹配（CRC32 路径映射与恢复脚本一致）。
4. **备注**：UnityGLTF `FindCommonAncestor` 的 rootBone 恒取 joints[0] 是包 bug，
   取景正确性不依赖它；如需精确 bounds 可在组装脚本里自行指定 rootBone = Pelvis 节点。

**V2/V3 组装脚本统一模式（后续所有组装代码必须遵循）**：

- rootBone 显式设为 `"Bip001 Pelvis"`（绕 UnityGLTF FindCommonAncestor 死代码 bug）；
- 实例外层包 `_Wrapper_Y180`，`rotation = Euler(0,180,0)`，`SetParent(..., false)`
  （补偿 D = C·S = rotY(180°)，整棵刚性旋转，蒙皮恒等不受影响；销毁时 DestroyImmediate(wrapper) 连带子树）；
- bounds 一律用顶点级遍历（`mesh.vertices × localToWorldMatrix`），不信 renderer.bounds；
- 嵌套重建（仅动画阶段需要）：`L_i = W_g_p(i)⁻¹ · W_g_i`，节点矩阵列主序写入；
- 验收一律程序化：AABB 对照骨骼 FK 真值、剪影覆盖率、连通域占比；不做目测判断。

---

## 附录：本目录产物清单

| 文件 | 说明 |
|---|---|
| `skinning-math-derivation.md` | 本文档 |
| `verify_skinning_math.py` | 单网格全链验证器（约定证据 / 参考链 / GLB 审计 / 字节级重建比对） |
| `batch_audit_glbs.py` | 全量批量审计器（默认 `_fix_test_full\*.glb`，27/27 PASS） |
| `blender_skin_check.py` | Blender 5.x 第三方求值 + 双视角渲染脚本 |
| `report.json` / `batch_audit.json` | 验证报告 |
| `renders\fixed_*_*.png` / `deployed_*_*.png` | 修复版 / 旧版渲染对照（side/top 双视角） |
| `renders\fixed_result.json` / `deployed_result.json` | Blender 求值偏差记录 |
| `Remielle_Origin_Body_1.verified.glb` | 已审计通过的 Body_1 修复版副本（可直接覆盖部署） |
