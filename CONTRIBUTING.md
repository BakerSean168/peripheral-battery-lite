# Contributing to Peripheral Battery Lite

[中文](#中文) · [English](#english)

## 中文

欢迎通过 Issue 和 Pull Request 修复 Bug、改善 UI、贡献更多设备协议。请优先提出**可复现、带证据**的改动。

### 开始

1. 使用 Windows 10/11 x64，以及 .NET Framework C# 编译器（Windows 内置 `csc.exe`）。
2. Fork / 克隆仓库，创建功能分支；修改前先查看 [架构说明](docs/ARCHITECTURE.md) 和 [插件指南](docs/PLUGINS.md)。
3. 执行 `./build.ps1`，然后执行 `./dist/PeripheralBatteryLite.Probe.exe --self-test`。
4. 改动 USB HID 协议时，必须附上已确认的 VID/PID、usage page、report length、查询来源、回包有效性检查和硬件验证结果。
5. 先交代哪些设备**已实机验证**，哪些只是理论支持。不要根据 VID/PID 推测成功率。
6. 提交 PR 时简述功能、边界、风险、测试输出及 UI 截图（适用时）。

### 安全与用户隐私

- **禁止**提交 API 密钥、用户目录、设备序列号、USB 唯一路径、个人识别信息或真实按键内容。
- 不要提交未经核实的 HID 写入命令；不尝试固件更新、设备配对、灯光设置或参数修改。
- 电量查询有可能影响设备休眠。新增自动查询逻辑必须保持默认关闭，并有手动刷新回退。
- 外部 DLL 插件与宿主共享 Windows 用户权限；请勿要求用户执行不可信 DLL。
- 不要通过 Issue 公开已知漏洞的可操作利用细节；先联系维护者讨论私下报告渠道。

### 许可证与协议来源

对本项目原创代码的贡献按 MIT 发布。若使用了其他项目的实现而不仅是协议事实，必须先在 PR 中说明来源、许可证及可否兼容，**尤其是 GPL-3.0 源码不能直接合并到 MIT 实现中而忽略其许可义务**。详见 [协议来源](docs/PROTOCOL_SOURCES.md)。

## English

Contributions are welcome, especially reproducible fixes and tested HID providers.

1. Develop and compile on Windows 10/11 x64 with the .NET Framework compiler; run `build.ps1` and `PeripheralBatteryLite.Probe.exe --self-test`.
2. Read [architecture](docs/ARCHITECTURE.md), [plugin guide](docs/PLUGINS.md), and [protocol provenance](docs/PROTOCOL_SOURCES.md).
3. A new device protocol must include VID/PID, usage page, report lengths, documented read command, response validation, safe timeout behavior, and whether it was **physically verified**.
4. Never add guessed firmware, pairing, configuration, or other mutable HID commands to a battery query.
5. Keep manual-only mode the default; avoid waking sleeping devices and keep stale cache timestamps visible.
6. Never post personal device serials, unique HID paths, private data or key events.
7. Before copying third-party implementation code, check the original license and compatibility; GPL-3.0 implementations cannot simply be relabeled MIT.
8. Include tests, diagnostics, and a concise description of behavioral changes with every PR.

The project prefers small, verifiable improvements over broad new frameworks.
