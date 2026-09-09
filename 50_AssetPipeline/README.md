# 50_AssetPipeline — 资产中转管线

## 阶段定义

```
Incoming   新到/待识别的原始资产（来自 20_ReverseEngineering 或外部）
   ↓
Converted  格式转换完成（如 GLB/ACL → Unity 可读）
   ↓
Cleaned    命名规范、去冗余、身份核验（source block+CAB+pathID 完整）
   ↓
Retargeted 需要重定向/重绑定的资产（换骨架、UV 修正等）
   ↓
UnityReady 通过验证、等待合入主工程的最终形态
   ↓
10_Unity/Remielle_Main/Assets/...
```

任何一步不合格 → `Rejected/`（保留原件与拒绝原因，不删除）。

## 规则

- 管线内流转的是**副本**；原始文件永远留在 `20_ReverseEngineering/Raw` 或权威库，不改写。
- 每次流转在文件旁留 manifest（来源身份、转换参数、校验哈希）。
- 进主工程前必须在 `../00_ProjectHub/Inventory` 对应索引登记。
- 记住既有约定：glTF TEXCOORD 的 V=1-V 与 UnityGLTF 导入翻转成对；不能翻 PNG 修高 UV 打包数据；贴图 sRGB/linear 按槽判断（详见 `E:\ZZZ\AGENTS.md`）。
