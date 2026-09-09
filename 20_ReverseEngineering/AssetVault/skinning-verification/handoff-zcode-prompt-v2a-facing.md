# 交给 zcode 的提示词:V2a 正反翻转修复确认 + 正反断言(用户粘贴用)

---
## 背景(必读,详见工作区 AGENTS.md「坐标朝向坑」)

倒立修复后(stand=+90°)角色已正立,但**正反面是反的**:DSH 用游戏数据判定了作者空间脸朝向——
眼/嘴/胸骨在 **−Y**(Skn_L/R_Eye y=0.022、Skn_M_Mouth y=−0.040、Skn_L/R_Chest_Cj_01 y=−0.042,
均低于头中心 y=+0.041),背裙骨 Skn_B_Skirt_01 在 **+Y**(y=+0.104),Face 网格鼻尖在 y=−0.059 极值。
即**作者空间角色俯卧、脸朝 −Y、背朝 +Y**。导入链 D=rotY180 保持 Y 不变,`Rx(+90°)` 把 −Y 转到
**−Z** → 当前 posZ 渲染看到的是后脑勺/背部(你说得对)。

**DSH 已修复**:`Assets\SourceAssets\Skeleton\v2a_assembly_manifest.json` 的
`"standRotationEuler"` 已从 `[90.0, 0.0, 0.0]` 改为 `[90.0, 180.0, 0.0]`(站立后加 yaw 180,
脸朝 +Z,与"相机在 +Z 拍正面"的项目约定一致)。总变换仍是纯旋转(det=+1),左右手没有镜像。
工作区 `AGENTS.md` 已同步更新此坑(站立旋转必须 Euler(90,180,0))。**不要改回。**

## 任务清单(按顺序)

1. **核对**:读 manifest 确认 `standRotationEuler=[90,180,0]`;对照 AGENTS.md 的推导复核
   (−Y 脸朝向证据 + D·Rx90 链);grep 确认无其他地方硬编码站姿旋转。
2. **重跑组装**:Unity 执行 **Remielle → V2a Assembly Geometry**,重新生成 prefab、四视角渲染、报告。
3. **加正反断言(防复发)**:在 `V2aAssembly.cs` 现有 Head.y > Pelvis.y 断言旁再加:
   `Skn_M_Mouth` 世界 z > `Bip001 Head` 世界 z(不满足 LogError 并中止);报告/日志输出两骨世界坐标。
4. **重新验收(程序化)**:
   - 27/27 maxDev/aabbErr 仍 ≤1e-6(表现层旋转不影响蒙皮恒等);
   - upright 断言 + 新 facing 断言双 PASS;总 bounds size 不变,center 预期 (0, 0.556, **−0.383**)
     (x/z 随 yaw 180 换号);
   - 剪影覆盖率/连通域与上次同量级。
5. **视角确认(人眼核对,关键)**:posZ 渲染应为**面部**(五官、无翅膀遮挡),negZ 为背面
   (翅膀/炮管在后);左右手位置正确(未镜像)。
6. **更新文档与记忆**:
   - `PROGRESS.md`:记录正反事故根因(作者俯卧脸朝 −Y + Rx90 把 −Y→−Z)与修复(Euler(90,180,0));
   - zcode 项目记忆:在倒立坑条目旁补正反坑一句话(站立旋转= Euler(90,180,0),脸朝 +Z)。

## 约束

- 不要改回 `standRotationEuler`;不要改 `recover_meshes.py`;不要动材质(V2b 待用户确认);
- 不要重新推导蒙皮数学(已定案)。

## 报告格式

- 核对结论(每条:通过/修正说明);
- 重跑验收数字(maxDev/aabbErr、双朝向断言、总 bounds、剪影);
- 四视角图的朝向确认(posZ=正脸等);
- 文档与记忆更新清单。
