# 当前状态（CURRENT_STATUS）

更新时间：2026-09-09（工作区初始化时点）
工程：`10_Unity/Remielle_Main`（Unity 6000.3.17f1，Built-in，Linear）
能力基线来源：冻结原件的阶段文档（`local-only\RemielleControllerDependencies\implementation\README.md`，2026-09-08~09）

## 已实现（均经真实 Player 帧验证）

- **移动**：原始状态图驱动；闪避；长按冲刺链（Dash_Start/Loop/Evade/Loop_02/End）与冲刺攻击；OpenKCC 位移/碰撞；Cinemachine 相机（含身体高度跟随）。
- **普攻**：四段连击输入链（465 帧验证）；左/右与 B 组武器拖尾、显隐、材质发光、渐隐/描边。
- **特殊技**：E 键普通/强化分支，原始能量规则（启动 60，强化第 14 帧扣费，不足自动走普通分支）；输入缓冲（默认 0.2s，过期清除、窗口内消费）。
- **命中**：34 条原始命中事件接入训练靶；角色局部停帧；33 组命中镜头震动（Cinemachine Impulse）；两种命中粒子效果（预热实例、无临时创建）。
- **冲刺镜头**：原始曲线库 366 条记录接入；Dash_Start 第 8/38 帧事件、65°/半径 3。
- **特效**：31 项粒子效果中 **8 项**已接入（普通特殊技双侧闪光、爆发、烟雾、拖尾等）；49 条特殊技特效事件已恢复。

## 已知问题与缺口（详见 `00_ProjectHub/Inventory/Missing_Content.md`）

- **逻辑**：移动状态机缺 walk→fly（Remielle 有两套 walk——走路与慢速飞行，按住移动一段时间后自动切换）；当前未实现。
- **资产**：大量特效/动作/运镜未接入（31 项特效仅 8 项；特殊技运镜、原生退出仰角映射、全翼构图、遮挡处理未做）；**普通特殊技闪光颜色不对**（HDR 诊断已定位：当前控制器相机未挂最终 LUT，显示路径尚待 HDR 校准）；逆向资产库（829 动画源身份等）尚未做完整查询归纳。
- **拓展性**：部分想加的内容在现存框架下暂无法实现，可能需要引入新的开源角色控制器仓库（评估流程见 `40_Integration`）。
- Floater 显隐与挂点按用户指示跳过，保留源资产。

- 云端可复验层已建立（2026-09-09）：SHA-256 全量清单（11 组 + Merkle 根，`20_ReverseEngineering/Manifests/`）+ B1 v1 机器目录（`Analysis/catalogs/`，动画 829 身份已机器验证、主工程动作库 335 motions/456 slots）。资产本体保持本地。
- 审查建议已落实一批（2026-09-09）：D1 路径审计（100 命中/64 输出写回，三类路径政策+护栏方案）、工作集校验 PASS（22 项 legacy 机器登记+依赖断链零命中）、`Gap_Register`/`Feature_Coverage`/三份绑定级索引骨架、验收双口径固化；**自研代码开始入本仓库**（用户决策，不设私有代码仓）。
- **D1-b 已完成（2026-09-09）**：61 处纯输出迁移 `90_Builds`，OUTPUT→local-only=0；`RemiellePathPolicy` 落地；GAP-013/014 闭环；残留 D1-c（23 处混合根待拆分）。Unity 首次导入+编译验证后台执行中。

## 工作区状态

- 主工程工作副本已建立（2026-09-09 复制）；冻结原件与全部 local-only 基线保持只读。
- 逆向资产已全部物理入库（2026-09-09）：Vault 清理副本（214,611 文件）+ RuntimeRepair/DataAcquisition/RenderingReview/Evidence 四组扩充（约 31G），按域导航见 `20_ReverseEngineering/README.md` 与 `VAULT_GUIDE.md`；原件全部留 local-only 只读。
- 副本内编辑器工具的绝对路径审计未做（见 TODO-Workspace-1）。
