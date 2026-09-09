# Remielle 技能设计（Skills Design）

状态：初始化骨架。

## 已知事实

- 特殊技/强化特殊技已可操作（能量规则见 `Remielle_Combat.md`）。
- 原始 49 条特殊技特效事件已恢复；31 项粒子效果中 8 项接入。
- 命中停帧、独立时钟（不受时间缩放影响的原始设置）已按源实现。

## 待办

- [ ] 技能时间线归纳（起帧/判定帧/特效帧/运镜帧）落 `../../20_ReverseEngineering/Analysis/Skill_Timeline/`。
- [ ] 强化特殊技专属特效与运镜。
- [ ] 闪光颜色修正方案（HDR 路径，见 `../Architecture/VFX_Architecture.md`）。
