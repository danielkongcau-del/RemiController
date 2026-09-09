# 外部仓库登记（PROVENANCE）

> 新引入的每个仓库/压缩包在此登记后才能进入评估（40_Integration）或主工程。

## 登记格式

```
## <名称>
- 来源 URL：
- commit/tag：
- 协议：
- 引入日期：
- SHA-256（zip/tgz 整包）：
- 用途 / 评估状态：
```

## 未入库原始包（指针）

以下原始包已存在于 `E:\ZZZ\local-only\RemielleControllerDependencies\downloads`（只读指针，其哈希登记见该目录 `download-provenance.json`）：

- OpenKCC a1a30ed7（已内嵌主工程）
- UnityHFSM f8fdc2e（已内嵌主工程）
- com.unity.cinemachine 3.1.7 tgz（已内嵌主工程）
- StickWeaponTrailEffect f34f445（已内嵌主工程）
- Animancer Pro 8.4.0（**未导入**，备用）
- BBB-Nexus（未导入，用途待定）

## 待引入

（尚无。候选角色控制器仓库按 `../README.md` 流程引入。）
