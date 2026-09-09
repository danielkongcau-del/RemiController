# Remielle Asset Vault

本库保存来源明确的原始导出、机械恢复产物和验证证据。当前模型的使用请先读 [统一交接文档](../RemielleHandoff/README.md)。

2026-09-04 清理后的注册范围为 **96 个来源、257,275 个文件**。已重新逐文件核对源、目标、大小及 SHA-256；详见 [verification.json](metadata/verification.json)。
该数字不代表全部工作区文件数。恢复范围见 [RECOVERY_STATUS.md](metadata/RECOVERY_STATUS.md)，证据边界见 [GAPS.md](metadata/GAPS.md)。

| 目录 | 用途 |
|---|---|
| `assets/` | 已注册来源的独立副本或 NTFS 硬链接，按源身份保留 |
| `recovered/meshes/by-source` | 178 个原始 Mesh 的蒙皮 GLB；当前头发另有可追溯的 UV 派生版本 |
| `recovered/animations/highest-quality` | 664 个完整分层 ACL 动画包 |
| `recovered/animations/standalone-native` | 9 个独立 ACL、156 个原生未压缩动画 |
| `recovered/animations/scalar-recovered/full-source` | 当前完整解码依赖的 JSON、raw、resS、ACL 和流式证据，不是废弃产物 |
| `recovered/skeletons` | 原皮、异装及相关原生层级 |
| `recovered/materials` | 95 个材质包、410 个已解析纹理引用 |
| `recovered/ledger` | 原始 JSON/JSONL/NDJSON 的结构化账本 |
| `metadata/manifest.json` | source/destination、身份、大小、SHA-256、存储方式 |
| `metadata/coverage.json` | 来源范围、文件数量、逻辑字节与同步状态 |
| `config/` | 主来源、追加来源与排除来源的注册说明 |
| `tools/` | 恢复、同步、独立验证和目录生成工具 |

同名不能代替身份：动画 829 个源身份对应 810 个名称；Mesh、Material、PPtr 必须按源 block/CAB/pathID 解析。
原始来源视为只读。NTFS 硬链接共享底层内容，改其中一个路径会影响另一个；可重建来源使用独立副本和原子替换。
历史 `assets/legacy` 中的真实源数据继续保留；不完整 `json-only-recovered`、试验网格、旧低精度生成器及其注册副本已删除。

## 日常验证

```powershell
$pythonExe = 'D:\Anaconda\python.exe'
$vaultDir = 'E:\ZZZ\local-only\RemielleAssetVault'
& $pythonExe -X utf8 "$vaultDir\tools\verify_vault.py"
& $pythonExe -X utf8 "$vaultDir\tools\verify_recovered_assets.py"
& $pythonExe -X utf8 "$vaultDir\tools\build_recovery_status.py"
```

确有源内容变化时再运行 `sync_vault.py --update-changed`；需要完整计算同步哈希时用 `--full-hash`。
原有替换内容会进入 `_history/<时间戳>`，不同于本次已明确删除的错误派生物。使用 `--source-id` 可以限制同步来源。
PowerShell 包装器 `tools/sync_vault.ps1` 对应参数为 `-UpdateChanged`、`-FullHash`、`-SourceIds`，可通过 `-Python` 指定解释器。
路径检查须解析 junction 并统一 Windows 长路径前缀，不得绕过包含检查。

完整动画使用 `recover_full_animations.py` / `recover_standalone_animations.py`；共享 ABI 文件 `recover_failed_animations.py` 的历史名称保留，旧不完整 YAML 导出逻辑已移除。
新增 JSON 来源后须更新账本并再同步验证。不能手改 manifest 哈希去掩盖来源不一致。

当前 Unity、源资产与动画逐项位置见 [模型目录](../RemielleHandoff/model-assets.csv)、[材质目录](../RemielleHandoff/material-assets.csv)、[动画目录](../RemielleHandoff/animation-assets.csv)。
