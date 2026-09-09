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

逆向资产库（829 动画源身份、31 项粒子、运镜曲线、材质）**尚未做一次完整查询归纳**——这是所有后续接入工作的前置（TODO-B1）。归纳产出直接写进上述各索引。
