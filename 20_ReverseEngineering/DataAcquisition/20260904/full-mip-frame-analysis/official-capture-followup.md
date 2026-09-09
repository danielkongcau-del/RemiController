# 原版采集后续方案检查

2026-09-05：用户确认恢复原版后成功启动。补采 DLL 的官方签名兼容问题仍存在，启动恢复不代表新采集功能已就绪。

## 已确认

- 官方原版签名验证及用户实机启动通过；当前保留原版，实验安装入口已停用。
- 同版本 `HackerDevice::CreateSamplerState` 只把参数转发给原设备，不记录描述符。当前 `ZZMI` 根目录也没有 `d3d11_log.txt`。因此不能声称打开旧日志就已取得精确采样状态。
- 原版命令列表支持自定义 Shader、资源引用与 `dump`。Microsoft HLSL `Texture2D.Load` 可以指定 mip 并读取未经过滤的像素；这是进一步离线验证的候选方案。

## 尚未证明

- 自定义 Shader 导出的解码 texel 不是 BC1/BC7 原始压缩字节，不能直接交给当前要求原始 DDS 完整字节链的回填器，也不能宣称源纹理已无损完整恢复。
- HLSL 像素读取不提供 `D3D11_SAMPLER_DESC`，不能同时解决 Filter、Address、Comparison 与 LOD 等字段的精确来源。
- 改变 Resource 的 `mips` 数只改变资源描述符，并不是已验证的逐层导出接口。
- 目前没有向正在运行的游戏安装新的采集配置；不要求用户重复 F8。先证明候选数据保真度及启动兼容，再安排新采集。

## 2026-09-05 后续结果：独立 RenderDoc 路线

已在相邻 `official-capture-validation/` 准备独立启动、原生回放和校验工具，并完成本机合成帧回归：6 份纹理 / 64 个子资源原始字节一致，3 份 sampler 创建描述符各 52 字节一致，4 种损坏/缺项负向对照被拒绝。原版 XXMI DLL 未改动。

驱动会规范化 sampler 未使用字段，所以该路线明确记录原始 `CreateSamplerState` 参数，不将其误称为与 `GetDesc` 全位一致。游戏兼容性和 8 条真实 mip 尾仍待一次用户手动 F12 抓帧。操作入口、证据与边界见 [新方案说明](../official-capture-validation/README.md)。不再要求用原版 F8 重复采集缺失项。

## 官方资料

- [3DMigoto 配置中的 CustomShader / dump 接口](https://github.com/bo3b/3Dmigoto/blob/master/Dependencies/d3dx.ini)
- [Microsoft HLSL Load](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-to-load)
- [Microsoft CopySubresourceRegion 的格式与子资源约束](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion)

本地 HackerDevice.cpp SHA256：`e7edb91a93c13a6c6a67002828a259452a7b5fcfac3d96d8de0f79acc81601b3`。
