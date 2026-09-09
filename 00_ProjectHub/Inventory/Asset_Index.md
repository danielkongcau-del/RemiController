# 资产总索引（Asset Index）

本目录是工作区的资产知识层入口。**权威资产库不在本工作区内**，先查 `../../WORKSPACE_MAP.md` 指针表。

## 索引分工

| 文件 | 范围 | 状态 |
|---|---|---|
| `Animation_Index.md` | 动画（Vault 全量库 → 工程 .anim 映射） | 骨架，待全量归纳（TODO-B1） |
| `VFX_Index.md` | 粒子/特效（31 项清单） | 骨架，8 项已登记 |
| `Camera_Index.md` | 运镜曲线与事件 | 骨架 |
| `Audio_Index.md` | 音频 | 未启动 |
| `Missing_Content.md` | 已知缺口与错误清单 | 已按用户口径初始化 |

## 每条资产的登记格式

```
名称/源身份
Status: Identified / InPipeline / Integrated / Rejected
Source: 逆向库中的位置（Vault 路径 + source block/CAB/pathID 身份）
Unity:  主工程内的位置
Meaning: 用途说明 / 触发条件 / 已知问题
```

## 当前最大欠账

- ~~逆向资产库未做完整查询归纳~~ → **B1 v1 已完成**（2026-09-09）：机器目录层 `../../20_ReverseEngineering/Analysis/catalogs/`（动画/网格/材质/控制器/特效/时间线 CSV + 摘要，829 身份已机器验证）；SHA-256 全量清单 `../../20_ReverseEngineering/Manifests/`（11 组 CSV + summary.json，云端可复验字节一致性，方法见其 VERIFICATION.md）。
- B1 v2 待办：名称口径归一（827 vs 810）、locomotion/特效/运镜的语义分类、Inventory 人工索引的逐项充实。
