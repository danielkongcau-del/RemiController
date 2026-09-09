# 资产扩充囊括报告（2026-09-09）

## 背景

应用户指令，按"散落资产盘点结论"物理囊括 Vault 之外的资产性数据。线索来源：codex 会话日志（2026-09-06，读取/解算路径挖掘）+ 全工作区体量盘点。**所有原件留在 local-only 只读，未做任何改动。**

## 复制清单与核验（16 组 robocopy，全部逐一文件数吻合）

| 副本位置 | 源（local-only） | 文件数 | 体量 |
|---|---|---:|---:|
| `RuntimeRepair/` | RemielleRuntimeRepair | 1,316 | 1.9 G |
| `DataAcquisition/` | RemielleDataAcquisition | 13,528 | 12 G |
| `RenderingReview/` | RemielleRenderingReview | 16,667 | 15 G |
| `Evidence/`（13 目录组） | RemielleModelReadiness、CharacterShaderEvidence、Frame{Bloom,Lighting,LightingPass,Pipeline,SceneNativeInstanced,SceneRawVertexInputs}ShaderEvidence、SceneLit{Material,Shader,ShaderEvidence}、MaterialRawDump、visual-acquisition | 47,186 | 2.2 G |
| **合计** | | **78,697** | **~31 G** |

用途速查：RuntimeRepair=运行时来源选择与修复（含 `runtime-source-selection.json` 身份链）；DataAcquisition=官方采集库（基础模型材质/绑骨/贴图导出、帧分析）；RenderingReview=渲染对照与原生回放基建（NativePlayer、Replay 工程）；Evidence=模型验收与渲染/光照/管线取证。

## 清理（仅副本）

- 删除：6 个 `__pycache__` 目录（77 文件）+ 1 个散落垃圾文件（.pyc 等）。78,697 → **78,619**，算术零偏差。
- **保留并注明**：`DataAcquisition/20260904/full-mip-frame-analysis/backup/`——名为 backup，实为捕获钩子回滚套件（原版 `d3d11-1.3.16-original.dll` + `Restore-OriginalCapture.ps1` + 补丁），功能性原件，非过时备份。

## 未囊括项（指针登记于 WORKSPACE_MAP）

- 待裁决：`RemielleFullAudit`（21M）、`reverse-notes`、`vendor`（270M）、`workspace-repair-20260904`（302M）、`RemielleZcodezcode-loops.md` 残留文件。
- 明确不囊括：`extracted\`（99G 全游戏提取主源，Vault 已注册复制 Remielle 相关部分）、游戏本体（UnityPlayer.dll oracle 源）、`FrameTools`（38G）、`tools\`（3.6G）、`RemielleStaticShowcase`（独立 URP 展示工程）、`E:\ZZZ\Remielle`（源引用素材）。

## 过程记录

首轮复制因 MSYS 参数转义两次失败（变量未展开 / 引号路径被前缀补全），**零部分写入**；改为脚本 + cygpath 无引号路径后成功。脚本存于 `70_Automation/AssetScripts/copy_inclusion.sh`。

## git 策略

五组副本与 Vault 同规则：**仅内部 `.md` 文档入库，数据整体排除**（.gitignore 白名单制）。策展后的解读与索引沉淀在 `Analysis/` 与 `00_ProjectHub/Inventory/`。
