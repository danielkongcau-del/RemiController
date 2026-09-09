# 10_Unity — 工程层

## Remielle_Main（唯一权威主工程）

- 来源：2026-09-09 自冻结原件 `E:\ZZZ\local-only\RemielleHoyoToon` 复制（剔除 Library/Temp/obj/Logs/UserSettings/*.csproj/*.sln 等可再生产物；8316 文件 / 5.59 GB）。
- Unity 6000.3.17f1，Built-in 管线，Linear。
- **原件已冻结**：不再打开、不再修改；日常开发只在本副本。两者严禁同时在编辑器中打开。
- 保持"任何一次保存都可编译、可进 Play Mode"。试验代码先进 `../60_Experiments` 或下方沙盒，禁止在主工程堆 Test*.cs。
- 首次打开会重新导入生成 Library；打开前先完成 TODO-D1（工具绝对路径审计）更稳妥。

## TestProjects/（沙盒工程）

ControllerSandbox / AnimationSandbox / VFXSandbox / CameraSandbox——隔离验证单一子系统的最小工程。按需创建（从主工程抽取所需资产），不承担长期开发。
