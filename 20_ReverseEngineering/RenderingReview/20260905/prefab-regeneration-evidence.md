# Remielle 动画 Prefab 重建事件证据

- 受保护旧 SHA-256：`49f0e454fc1a496b8b348a78a69d9d7654e7a13477e3f1024659e1dd2c182d73`
- 首次误重建 SHA-256：`3fcfae06ddf29306362515b3395260362fb3501c2b2e19493388cef3319a90b1`（当时含临时渲染抓帧贴图引用）
- 当前 SHA-256：`8b5167f4373123617cc2260ffd643830e363f4062b81f8126a43ba8a169721d8`（抓帧贴图引用已移出 Prefab）
- 当前字节完全匹配旧基线：`false`
- `baseline.json` 未更新，校验门禁未绕过。

## 已确认的边界

- 当前 Prefab 只序列化原有运行时 LUT 状态字段；抓帧得到的 secondary/signature emission 贴图由渲染审查场景中的边界组件注入。
- 全盘、项目、常规 File History、回收站、Unity Undo 与可访问 Artifact 缓存均未找到旧 YAML 源文件副本。当前 Artifact 只有导入后的二进制对象，不能据此恢复原 YAML 字节。
- 这不是已证明的模型语义回退。最新网格/蒙皮/脸部/朝向/15 个动画/LUT/模型完整性/D3D11 Player/运行时材质检查全部通过。

## 处理原则

- 保留旧基线与红色门禁，不用刷新基线掩盖事件。
- 禁止再次运行 `NativeAnimationBuild.Run` 或 `RebuildRig` 写入受保护 Prefab。
- 若最终无法恢复旧字节，先让生成器可重复地产生完全相同的 Prefab，再建立单独版本的新基线，并永久保留本报告与旧基线。

## 最新语义证据

- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\unity-full.json` — pass=`true`, SHA-256 `a9f1b88dadc5f30e7ba9a1e1d0cbcb461e21f535b69e51c8165ad1dce1804e5f`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\unity-import.json` — pass=`true`, SHA-256 `be660c902009a8d1b2375f76ef6de3be256c85524fddc5ec3bacba0bac3956dc`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\face-regression.json` — pass=`true`, SHA-256 `621a8354b86df52d5f5db946dcb15dd81e77c814d1a77ef8b52d9a7517664510`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\rotation.json` — pass=`true`, SHA-256 `b06beb5121f74134af5234deeb93bdfac2101e2e1284bbcbef1c0edc879b959a`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\lut-gpu-audit.json` — pass=`true`, SHA-256 `b27aebb8201fd991b65e03899758bef1e2534538077a0345a33fd99d6a174ece`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\model-integrity.json` — pass=`true`, SHA-256 `3fb2d9421dee11b50f0fa1b176b22a5bec962726bb4c3d16a9f58410bed154c7`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\Player\build-result.json` — pass=`true`, SHA-256 `d6994cfd2e1e4e020a21b2d5d9e981632428c955abf1a4f7f424fa78d4949bc2`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\player-render-verification.json` — pass=`true`, SHA-256 `0c39993f4cfd036766cbdb52ca18703ad8468a6ce3cffba892dbaafe8e5737f0`
- `E:\ZZZ\local-only\RemielleModelReadiness\20260904\player-smoke.json` — pass=`true`, SHA-256 `4e370117e68bb7385177432c855977c0f7b915781cf9b570493a53f89f5a8267`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\runtime-material-verification.json` — pass=`true`, SHA-256 `9fa6e602f520ca181415281e19fa09141ac4287976c3ddc7df36df7f6d2dd5b6`
- `E:\ZZZ\local-only\RemielleRenderingReview\20260905\player-verification.json` — pass=`true`, SHA-256 `16ab5051c4ebdc0b701f726a65fcc5e20a2e315e4b5fc503cddd3b086c46a1ff`
