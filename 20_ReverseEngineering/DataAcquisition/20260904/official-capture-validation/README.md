# Remielle 原始 mip 与采样器补采：RenderDoc 独立路线

2026-09-05。状态：**本地采集、回放和数据保真测试通过；游戏实际尝试出现启动后自行退出，未生成 RDC，补采路线暂不可用。**

当前 XXMI 官方 `d3d11.dll` 保持原版，SHA256 为 `02e5f1dcf926f6a1c00517307213265b1671797e9de1e49cedf731a95447a403`。用户已确认恢复后启动成功。本路线使用本机安装的 RenderDoc 1.45；不替换 XXMI 的 DLL，不修改签名清单或启动器设置。

## 13:45 启动失败与修复

首次用户实机尝试在创建进程时失败，RenderDoc 返回退出码 4 / `Failed to launch process`，没有生成 RDC。检查游戏内嵌 manifest 确认 `requestedExecutionLevel=requireAdministrator`；原入口缺少权限处理，这是已确认的工具缺陷。用独立本地测试程序复现了 Windows 740（需要提升权限）及完全相同的 RenderDoc 错误。原始游戏报错没有携带 Win32 错误码，因此不把测试程序的 740 冒记为该次游戏的直接日志。

`Start-RemielleCapture.ps1` 现已增加 Windows 标准 UAC 提权：普通双击时提示原因并申请 RunAs，已是管理员时直接执行；拒绝 UAC 就停止，未提权的子进程不递归重试。原始控制台保留操作说明，辅助 PowerShell 隐藏运行。所有提权结果、CLI 输出和启动结果分别保存为 `elevation.json`、`renderdoc-launch.log`、`launch-result.json`。`-CheckOnly` 只检查并报告 `elevationNeeded` / `directLaunchReady`，不会显示 UAC 或启动游戏。

证据：`launch-diagnostics/elevation-diagnosis.json` 和 `launcher-control-flow-tests.json`。后者四项是模拟 UAC 的控制流测试，不是实机提权启动成功证明；修复后的游戏启动及捕获仍待用户手动确认。

## 已验证的内容

### 14:04 启动返回值误判修复

第二次用户尝试已获得管理员权限，RenderDoc 明确输出 `Launched as ID 38920`。官方 `CaptureCommand::Execute` 在不使用 `--wait-for-exit` 时，会把成功建立的 target-control identifier 作为 CLI 返回值；它不是普通进程 PID，也不是失败码。原入口仍按非零即失败处理，因此写出了错误的失败报告。

现已为 `capture` 增加 `--wait-for-exit`：等待游戏退出后，成功的 ExecuteAndInject 返回 0。`launch-diagnostics/cli-exit-integration.json` 用本机真实 RenderDoc 和自有 D3D11 测试程序验证了默认模式返回 target ID、等待模式返回 0，且等待模式退出前 RDC 已生成。这次验证没有启动游戏或显示 UAC。原 UAC 控制流回归也已复验。

用户这次的原始日志保留在 `captures/20260905-140420-962`，纠正说明见 `corrected-interpretation.json`。它证明 RenderDoc 的启动/注入调用报告成功，不能单独证明游戏能持续运行或实际完成捕获；当次尚未发现 RDC。

用户随后确认游戏窗口出现后自行退出，因此撤回立即重试建议。当前保留修复后的入口作后续诊断，但不要求重复启动或 F12。原始控制台在等待模式下持续等待属于预期；它不能解决尚未定位的游戏自行退出。

`CaptureFixture.cpp` 用 D3D11 创建已知字节的测试资源，自行录制一帧；测试窗口始终隐藏，不操作桌面或游戏。`ReplayExport.cpp` 使用同版本官方原生回放 API 导出绑定和子资源。`verify_export.py` 对照独立保存的创建输入。

- 6 份纹理、64 个子资源：BC1、BC7、BC6H、RGBA16Float、R32Float，以及 8 层 BC7 数组；所有 mip / layer 的原始字节完全相同。压缩块未解码或重新压缩。
- 3 份 `CreateSamplerState` 输入描述符，每份 52 字节完全相同；包括 Filter、U/V/W 寻址、MipLODBias、MaxAnisotropy、ComparisonFunc、BorderColor 与 Min/MaxLOD。
- PS 的原始 DXBC 完全相同，使用工作区 3DMigoto 的 **seed=0 FNV-1** 与既有 shader 身份连接。
- mip 字节损坏、采样器字段损坏、缺失采样器来源、错误 shader 内容四种负向对照均被拒绝。前两种依赖独立 fixture 真值；普通游戏导出尚无该独立真值，不能把一般完整性检查说成逐字节源资产匹配。

证据：`export-v2/validation.json`、`negative-controls.json`、`offline-validation-report.json`。当前没有游戏 RDC，不能据此宣称游戏内 8 条 mip 尾或 3 种采样状态已经取得。

## 实际游戏退出的证据与边界

`captures/20260905-140420-962/startup-exit-analysis.json` 保存结论，`startup-evidence/` 保存当次与前次正常运行日志。游戏于 14:04:22 启动，日志到 14:04:35 停止：D3D11 feature level 11.1 / RTX 5070 Ti Laptop 已初始化，Wwise 音频已初始化，当前没有正常的 OnApplicationQuit 标记；前次正常运行有该标记。

当次的 Legacy Shader / RTXGI 警告及 OnShowProtocolTimeout 在前次正常运行中也存在，因此不把它们当作根因。限定时间窗口内未找到匹配的 Windows Application 1000/1001/1002 崩溃事件或 Zenless/RenderDoc 崩溃转储，也没有足以确认根因的异常堆栈。现有证据不能区分 SDK/捕获兼容性、程序主动退出或其他原因，更不能认定为反作弊拦截。

未生成 RDC；游戏的 8 条 mip 尾及精确采样状态仍未补齐。原版 XXMI 文件和模型资产均保持原样。本轮不再要求用户重复这条失败路线；后续应先拿到可定位原因的诊断证据或验证其他可用的数据来源。

## 采样器的精度边界

RenderDoc 保存的是游戏传给 `CreateSamplerState` 的原始参数。驱动的 `GetDesc` 可能规范化不参与当前模式的字段：本机测试中，非各向异性模式的 MaxAnisotropy 被清零；不用 Border 寻址的 BorderColor 被清零。因此本路线的真值定义是 **原始创建参数**，不是声称所有位都等于驱动规范化后的 GetDesc。测试报告保留两者差异；接入时按原始参数重建，再检查实际采样行为。

## 用户手动采集步骤（保留作兼容性解决后的操作参考）

此前约定由用户操作游戏。以下脚本已准备并通过路径、版本和原版 DLL 检查，代理没有代为启动游戏。

1. 退出现有游戏。
2. 双击本目录 `Start-RemielleCapture.cmd`，在 Windows UAC 中核对后选择“是”。游戏本身要求管理员权限，所以 RenderDoc 启动链也必须提权。它从 RenderDoc 独立启动游戏，此次不要通过 XXMI 再启动第二个游戏进程；原始控制台会保留说明，辅助进程隐藏运行。
3. 到试玩实战或训练场，让原皮 Remielle、面部、武器和双翼完整出现，稳定显示后按 **F12 一次**。这里用 F12；F8 仍是另一条旧采集路线。
4. 等待抓帧完成。确认本目录 `captures/日期时间/` 下出现非空 `.rdc`，然后退出游戏并告诉代理已采集。首次先验证这一帧，不必重复之前三种场景。

如果启动失败、游戏提示不支持图形调试或按键后没有 RDC，保留提示并反馈。本地测试不能证明游戏支持 RenderDoc；不得以修改保护机制或签名校验方式处理失败。

## 离线导出

```powershell
& 'E:\ZZZ\local-only\RemielleDataAcquisition\20260904\official-capture-validation\Export-Capture.ps1'
```

默认选择本目录最新 RDC，也可显式传入 `-Capture '绝对路径.rdc'`。按 `remielle-pixel-shaders.txt` 中已有六个 PS 哈希筛选，导出每个目标 draw 的 PS、SRV 视图范围、采样器资源 ID 和原始创建块，以及所绑定资源的全部 mip/layer。文件仅写入捕获旁的新 `export-日期时间` 目录。

`validation.json` 的 pass 只说明该次导出内部完整、绑定的采样器都有原始来源。它不表示 8 个 Remielle 缺口已齐全，也不修改 Unity。还必须用既有 DDS mip0 字节、格式、尺寸与 PS/槽位核对新资源身份，检查 mip 链完整后再接入回填。

资源 ID 和事件编号仅在各自 RDC 中有意义，不能当作旧 F8 的指针或 draw 编号。纹理/缓冲默认只在首次匹配 draw 导出，`exportedAtEvent` 标明时间点；适用于本次静态纹理补齐。动态阴影和场景缓冲的不同 draw 快照不可混用。3D / MSAA 资源目前明确拒绝，避免输出未验证布局。

## 构建与复现

- `Build.ps1`：本机 MSVC 14.51 + Windows SDK 10.0.26100.0，编译两个本地程序；不接触游戏目录。
- `CaptureFixture.exe <新输出目录>` → `ReplayExport.exe <fixture.rdc> <新导出目录>` → `verify_export.py <导出目录> --fixture <创建输入目录>`。
- 原生回放程序必须导出 `renderdoc__replay__marker`，否则 RenderDoc 会按捕获进程初始化，虽能读取 RDC 但无法正确回放。本实现已处理。
- 原生头文件固定为 v1.45，来源和 SHA256 见 `renderdoc-header-sources.json`；已安装 DLL 的 commit 为 `2fc0bc04cb95499635f63986a55bc6f67849dd9f`，运行时拒绝其他版本。

## 官方依据

- [RenderDoc 应用内采集 API](https://github.com/baldurk/renderdoc/blob/v1.45/docs/in_application_api.rst)
- [RenderDoc 回放接口：GetTextureData / GetSamplerDescriptors / GetStructuredFile](https://github.com/baldurk/renderdoc/blob/v1.45/renderdoc/api/replay/renderdoc_replay.h)
- [RenderDoc Windows 回放标记初始化](https://github.com/baldurk/renderdoc/blob/v1.45/renderdoc/os/win32/win32_libentry.cpp)
- [RenderDoc CaptureCommand 返回值与 wait-for-exit](https://github.com/baldurk/renderdoc/blob/v1.45/renderdoccmd/renderdoccmd.cpp#L213-L242)
- [Microsoft D3D11_SAMPLER_DESC 字段](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_sampler_desc)
- [Microsoft manifest 的 requireAdministrator](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests#trustinfo)
- [Microsoft Start-Process 的 RunAs](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/start-process?view=powershell-5.1)

游戏文件、源模型、Prefab 和原始动画均未修改；所有捕获只留在本机。
