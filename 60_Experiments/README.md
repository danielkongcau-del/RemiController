# 60_Experiments — 实验层

## 流程

```
想法 → 在此建实验目录（含 README/Findings/Prototype）
  成功 → 40_Integration 验证 → 合并主工程 → 本目录记载结论后归档
  失败 → 结论写入 Findings → 移入 99_Archive
```

主工程永远不因试验而腐烂：不出现 Test.cs / Test2.cs / NewMovementFinal2.cs 堆积。

## 实验卡模板

每个实验目录必含：
- `README.md`：目标、假设、验证口径（数值 + 真实 Unity 运行，不接受纯静态判断）
- `Findings.md`：过程结论、勘误记录
- `Prototype/`：原型代码/资产

## 在办实验

- `Movement/WalkToFly` — walk→fly 移动切换（首个实验，2026-09-09 立项）

## 分区

Movement / Combat / Animation / VFX / Camera / Rendering / Controllers（控制器候选行为验证，与 40_Integration 配合：这里验行为，40 楼做架构适配）。
