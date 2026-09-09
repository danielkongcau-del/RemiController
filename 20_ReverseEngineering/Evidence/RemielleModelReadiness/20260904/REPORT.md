# Remielle 模型交接验收

当前使用入口：[完整交接文档](../../RemielleHandoff/README.md)。此目录是当前模型验收与唯一正式 Player 输出位置。

本次清理后重新构建并运行：**[verification.json](verification.json) 全部通过**。

| 检查 | 结果 |
|---|---|
| 网格、材质、组件 | 28 个导入 GLB、27 个可见网格、29 个 SMR、13 个材质、115 张源贴图 |
| 已序列化引用 | 8,001 项检查 |
| 姿态形变 | 75 个姿态、4,801,725 次顶点检查 |
| 动作组合 | 225 个有序组合与独立目标采样一致 |
| 真实 GPU 面部方向 | 9 个带符号方向，最大误差约 1.20e-7 |
| 动态裁剪 | 75 个真实姿态，最大越界数值误差约 3.81e-6 |
| 最新独立程序 | 560 帧渲染检查 + 240 帧基础运行检查 |
| 构建 | 0 错误、3 警告；具体内容见交接文档的构建警告说明 |
| 面部正/侧/背光 | 约 0.700668 / 0.624351 / 0.584135，正光与修复基线一致 |

- [14 张实际运行截图](review.html)：正反侧面、面部光向、发带内外、翅膀和 LUT 配置。
- [Player](Player)：运行 RemielleAudit.exe；整个目录为完整运行包。
- [构建结果](Player/build-result.json)、[渲染检查](player-render-verification.json)、[运行检查](player-smoke.json)。
- [源与导入设置](source-and-import-settings.json)、[模型结构检查](model-integrity.json)。
- [最终文件哈希](final-artifacts.json)：封存代码、设置、Player 和当前结果。

执行 `RemielleHandoff/Run-Checks.ps1` 可顺序复验，`-FullMotion` 追加完整 15 动作播放对照。
本轮重新进行了全 Vault 校验及 673 个原生 ACL 解码比较；之前 4,180 帧完整播放和 229 项采集/工具测试保留原先时间，未冒称本次重跑。

面部修复使用真实 GPU 世界方向；同帧 editor bounds 只作诊断，最终裁剪门槛使用真实帧与渲染后的数据。
仍需完整渲染光照对照、六网格证据补充、离体附件的原生显隐规则、控制器和事件，详见统一文档。
