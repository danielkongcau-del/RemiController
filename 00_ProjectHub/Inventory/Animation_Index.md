# 动画索引（Animation Index）

状态：骨架；全量归纳未开始（TODO-B1/D3）。

## 权威来源

`E:\ZZZ\local-only\RemielleAssetVault\recovered\animations`：
- `highest-quality`：664 个完整分层 ACL
- `standalone-native`：9 个独立 ACL + 156 个未压缩动画
- 合计 829 个源身份 / 810 个名称
- `scalar-recovered/full-source`：必要的正确原始 JSON/raw/resS/ACL 输入

## 工程侧

- `10_Unity/Remielle_Main/Assets/V3/Animations`：15 个 .anim——**渲染/光照验收示例集，非全集**。
- 已接动作（经验证）：闪避、Dash_Start/Loop/Evade/Loop_02/End、冲刺攻击、四段普攻、特殊技/强化特殊技、移动族。

## 归纳任务

- [ ] 全量库 → 工程 .anim 映射表（库内真名 / 源身份 / 导入状态 / 用途）。
- [ ] locomotion 族候选清单：idle 家族 / 走路 / **慢速飞行（fly/hover）** / 跑 / 冲刺 / 闪避 Evade·Dodge·Dash·Roll / 转向——优先服务 walk→fly（`../Design/Remielle_Movement.md`）。
- [ ] 每条登记：名称、源身份（source block+CAB+pathID）、时长、循环性、已接/未接。
