# 40_Integration — 架构移植实验室

## 定位

候选架构（新角色控制器仓库、动画/运镜/特效系统）**先在此验证，再进主工程**。避免"拆掉现有 → 换新 → 不合适 → 项目爆炸"。

## 流程

```
30_ExternalRepos（上游只读）
        │  选定候选
        ▼
40_Integration/Controller/Candidates/<候选名>/
   ├─ OriginalArchitecture.md   ← 摸清候选自身的架构
   ├─ UsefulModules.md          ← 哪些模块值得保留
   ├─ RemielleMapping.md        ← 如何映射到 Remielle 的资产与行为
   ├─ RequiredChanges.md        ← 双方需要哪些改动
   └─ Prototype/                ← 适配原型（可跑最小验证）
        │  验证通过
        ▼
10_Unity/Remielle_Main（合并）
```

## 评估必答题（以 walk→fly 为试金石）

- 候选能否表达 Remielle 的二段移动（走路 → 慢速飞行）？
- 与现行 Native 状态图（97 状态）、动画求值链、输入缓冲的兼容成本？
- 迁移是替换、包裹还是并行分支？回滚方案？

## 现行系统档案

`Controller/Current.md` —— 评估任何候选前先读它，明确"现框架做不到"的具体清单（TODO-C2）。

## Compatibility

跨候选/跨版本的兼容垫片与对照测试放 `Controller/Compatibility`、`Animation/`、`VFX/`、`Camera/`、`Rendering/`。
