# Remielle 完整 mip / Texture2DArray 与 sampler descriptor 采集器

## 目的

ZZMI 1.3.16 的 F8 frame analysis 会先用 `CopyResource` 把整个 Texture2D 复制到 staging 资源，但随后的 DirectXTK `SaveDDSTextureToFile` 只写 DDS 的 subresource 0。因此旧采集只包含 mip 0，Texture2DArray 也只包含第 0 层；这正是 Remielle 原生材质 8 个纹理仍缺低级 mip 的原因。

本地补丁让 DDS 导出完整遍历 `array item -> mip level`，移除 GPU row padding 后依次写入；数组使用 DX10 DDS 头。单层、单 mip 的 legacy DDS 路径保持原格式。

## 已验证内容

- 同源仓库：`SpectrumQT/XXMI-Libs-Package`，commit `48c24f8d19b48909a2a4405c64394f42d35e9796`。
- 原 DLL 和新 DLL 的 FileVersion / ProductVersion 都是 `1.3.16`。
- `validation/ScreenGrabFullResourceTest.cpp` 使用 WARP D3D11 创建 BC1 纹理并调用实际导出函数。
- 单层 × 单 mip：legacy `DXT1` 头、160 字节、像素负载逐字节一致。
- 2 层 × 4 mip：DX10 头、260 字节、`arraySize=2`、`mipMapCount=4`，112 字节负载按全部 8 个 subresource 逐字节一致。
- 完整 `DirectX11` Release x64 构建：0 error。

精确哈希、命令和输出见 `build-report.json`。源码差异在 `FullMip-and-SamplerDesc.patch`，原 DLL 位于 `backup/`。

同一 DLL 还把 `ID3D11SamplerState::GetDesc` 的完整 `D3D11_SAMPLER_DESC` 写入 frame-analysis API 日志。每条记录保留 Filter、AddressU/V/W、MipLODBias、MaxAnisotropy、ComparisonFunc、BorderColor、MinLOD 与 MaxLOD；所有 float 同时以原始 32 位十六进制位记录，避免文本舍入。原有 `handle=` 字段保持不变，旧日志解析不会失效。

接口依据：[Microsoft `ID3D11SamplerState::GetDesc`](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11samplerstate-getdesc)、[`D3D11_SAMPLER_DESC`](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_sampler_desc)。

Remielle 专用回填器 `Verify-And-Import-RemielleFullMipCapture.py` 不依赖下次抓帧沿用旧 draw 序号。它先从旧抓帧恢复资源哈希，再以描述符和旧 mip0 的 SHA-256 对新帧做内容匹配；只有 mip 数、数组层数和完整负载字节数全部等于资源描述符时才接收。重复 draw 产生的相同文件允许去重，完整文件内容出现分歧则拒绝选择。

校验器已做两种回归：旧 2026-09-04 抓帧能找到 8/8 目标，但实际 mip 数均为 1且日志无 sampler descriptor，因此 0/8 通过；合成完整链与三种 sampler 状态的正向夹具为纹理 8/8、sampler handle 3/3 通过。`Test-RemielleCaptureVerifier.py` 可重复执行正向回归。

## 当前部署状态：已恢复官方原版，补采暂停

2026-09-05 用户实机启动确认：自编译 DLL 被 XXMI 的官方软件包签名校验拒绝。之前的编译、WARP 导出和合成夹具测试只验证离线功能，不能证明启动器兼容。此前“安装后直接启动并按 F8”的步骤已撤回。

已从替换前备份恢复 `E:\ZZZ\FrameTools\XXMI Launcher\ZZMI\d3d11.dll`，并用 XXMI 官方公钥与本机 `Resources/Packages/XXMI/Manifest.json` 验证恢复后的 DLL 和现有编译器 DLL，均通过。签名检查和启动器配置没有改动。证据：`launcher-signature-recovery.json`；当前状态：`installation-report.json`。 用户随后确认“这次没问题了，成功启动”；启动恢复已经实机验证。

`Install-FullMipCapture.ps1` 已停用并明确报错，防止再次覆盖官方文件。实验 DLL、源码补丁与离线测试保留供研究；它们目前不能通过此启动路径完成采集。原版 F8 仍受 mip0 / 第零数组层导出的限制，不能完成剩余 8 条 mip 链与采样器描述符的补采。

`Verify-FullMipCapture.ps1` 在官方原版状态报告 `fullMipCaptureReady=false`，在实验 DLL 状态拒绝视为可用。导入器保留，但仅能消费未来验证通过的新采集，不能把旧抓帧或合成测试当作真实数据。

可重复的离线检查：

```powershell
python .\Test-RemielleCaptureVerifier.py
.\Verify-FullMipCapture.ps1
```

需要恢复本地已锁定的原版时，关闭游戏和启动器后运行 `Restore-OriginalCapture.ps1`。下一步先验证兼容官方启动流程的采集方案，再通知用户进行实机操作。
