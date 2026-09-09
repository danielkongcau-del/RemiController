# 资产清单核验指南（VERIFICATION）

目的：在不传输资产本体的前提下，任何人可用本目录的 SHA-256 清单**复验任一份本地资产拷贝**是否与本工作区逐字节一致。

## 清单构成

- `<组名>.sha256.csv`：每组一份，格式 `sha256,size,path`；path 相对各组根目录、按字节序排序；UTF-8 无 BOM、LF。
- `summary.json`：组级摘要——文件数、总字节、`manifestSha256`（清单文件自身的哈希）、`merkleRoot`。

组与根（相对 `E:\ZZZ\ZCode`）：AssetVault-{assets,recovered,metadata,tools,skinning-verification,config}、RuntimeRepair、DataAcquisition、RenderingReview、Evidence、Remielle_Main（主工程副本）。

## 复验方法（在任一份拷贝所在的机器上）

```powershell
# 1) 核对清单本身未被篡改：重算 CSV 文件哈希，与 summary.json 的 manifestSha256 比对
Get-FileHash -Algorithm SHA256 .\AssetVault-recovered.sha256.csv

# 2) 抽验/全验单个文件：重算资产哈希，与 CSV 对应行比对
Get-FileHash -Algorithm SHA256 <资产文件>

# 3) 全组复验：python - <<'PY' 脚本按 CSV 逐行重算并比对（算法：流式 SHA-256，1MB 分块）
PY
```

Merkle 根算法（供独立实现）：对排序后每行构造 `sha256 <path>\n`，按序拼接后整体 SHA-256。任一资产文件或路径变动都会导致组根改变。

## 生成与再生成

- 生成脚本：`70_Automation/AssetScripts/generate_manifests.py`（流式哈希，1MB 分块）。
- **排除项**：嵌套 `.git` 目录不入清单（Evidence 组内 vendored 克隆 `XXMI-Libs-Package` 的 .git 元数据 29 个文件，非资产数据）。复验时如自行遍历，请同样排除 `.git`。
- 清单为 2026-09-09 时点快照；此后每次有意变更资产副本后应重新生成并在 git 中提交新清单，保持云端可复验性。

## 边界

- 清单可证明"字节一致性"，不能还原资产内容（哈希单向）。
- 云端语义检索请用 `../Analysis/catalogs/`（B1 目录层）与 `../VAULT_GUIDE.md`。
