# Pulse Sidebar

**把 Codex 额度和电脑状态放到 Windows 屏幕边缘。**

A native Windows sidebar for Codex quota and hardware monitoring, built with C# and WPF.

深色紧凑圆环、展开式详情面板、实时硬件指标，以及来自本机 Codex 登录的额度和 Token 用量。无需填写 API Key，也不会为了读取额度发起模型推理。

## 快速开始

要求 Windows 10 / 11 x64、.NET Framework 4.8 和 PowerShell 7。Codex 功能需要本机已安装并登录 Codex；GPU 指标目前使用 NVIDIA 驱动的 `nvidia-smi`。

```powershell
git clone https://github.com/Chershi-319/pulse-sidebar.git
cd pulse-sidebar
.\scripts\prepare.ps1
.\scripts\build.ps1
Start-Process .\bin\PulseSidebar.exe -ArgumentList '--install-shortcut'
```

构建完成后可直接双击 `bin\PulseSidebar.exe`。`--install-shortcut` 会在当前用户桌面创建快捷方式。请先退出正在运行的 Pulse，再重新构建。

仓库只包含源码、构建脚本和文档；依赖由 `prepare.ps1` 从官方固定版本下载并校验，不包含开发者的登录信息、用量快照或运行截图。

## 操作

- 默认停靠右侧；点击圆环或“展开”查看详情，按 Esc 收起。
- 拖动 PULSE 标题调整纵向位置，拖向屏幕另一边可切换停靠侧。
- 右键：左右停靠、显示器选择、置顶、80% / 100% / 120% 大小、开机启动、退出。
- 隐藏后，双击系统托盘图标重新显示。开机启动默认关闭，可自行在菜单开启。
- CPU 温度未连接时，点击“连接 CPU 温度”。Windows 会要求 UAC 授权；必要时安装官方传感器驱动。界面和 Codex 连接仍以普通用户权限运行。

## 数据来源与刷新

| 指标 | 来源 | 周期 |
| --- | --- | --- |
| Codex 剩余额度、重置时间、可用重置次数 | 本机 Codex app-server，只读 `account/rateLimits/read` | 60 秒，可手动刷新 |
| 今日、本月已返回记录、累计 Token | 只读 `account/usage/read` | 5 分钟，服务端统计可能延迟 |
| CPU 使用率 | Windows GetSystemTimes，两次采样差值 | 2 秒 |
| CPU 温度 | LibreHardwareMonitor 0.9.6 + PawnIO，独立采集进程 | 2 秒 |
| GPU 温度、负载、显存、功耗 | NVIDIA 驱动 nvidia-smi | 2 秒 |
| 物理内存 | Windows GlobalMemoryStatusEx | 2 秒 |
| 网络上传 / 下载 | 活动物理以太网与 Wi-Fi 累计字节差值 | 2 秒 |
| 磁盘已用 / 总容量 | 本地固定磁盘 | 30 秒 |

额度根据实际 `windowDurationMins` 标注，支持 5 小时、12 小时、7 天等任意窗口及多个额度桶。未返回的窗口显示说明，不显示虚假的 100%。断线保留上次值并明确标为待更新，超过 150 秒视作过期。重置倒计时到零后等待服务端更新，不自行把额度恢复到 100%。

Token 每日记录按服务端日期归属；本月只对服务端已返回的当月记录求和，不保证覆盖整月。缺失记录用“—”。订阅额度不等于 API 账单，本工具不推算人民币金额或不存在的生成速度。当前实现仅连接本机当前登录的 Codex 账号，不读取其他 AI 服务账号。

## CPU 温度权限

使用官方 LibreHardwareMonitor 发布包中附带、数字签名有效的 PawnIO 安装器。仅启用 CPU 传感器采集，不修改风扇、频率或电压。

CPU 温度采集进程以管理员权限运行，随主程序退出而停止；它不读取 Codex 登录信息。每次重新连接可能出现 UAC，未创建长期高权限计划任务或服务。PawnIO 驱动会保留，若不再使用可从 Windows“已安装的应用”卸载。

## 本地文件

- `src/`：C# 源码；`scripts/`：依赖准备、构建及只读探测。
- `bin/`：可直接运行的应用和传感器依赖。
- `%LOCALAPPDATA%\PulseSidebar\settings.json`：界面偏好。
- `%LOCALAPPDATA%\PulseSidebar\sensors.json`：CPU 温度快照，不包含凭据。
- `%LOCALAPPDATA%\PulseSidebar\error.txt`：若发生异常，写入诊断。
- `artifacts/`：本机测试记录和界面渲染。

不复制或输出 Codex token；额度通过已安装 Codex 提供的 JSON-RPC 读取，不发起推理。若程序找不到 Codex，可设置环境变量 `PULSE_CODEX_PATH` 指向本机 `codex.exe`。

## 构建和验证

使用 Windows 的 .NET Framework 与 WPF，无需额外安装 Node/Electron 或 .NET SDK。

```powershell
.\scripts\prepare.ps1
.\scripts\build.ps1
Start-Process .\bin\PulseSidebar.exe -ArgumentList '--self-test' -Wait
Start-Process .\bin\PulseSidebar.exe -ArgumentList '--ui-test artifacts' -Wait
```

运行 `--self-test` 验证窗口映射、缺失值、过期时间等关键数据语义；结果在本地应用目录。`--ui-test artifacts` 读取你自己的真实数据，保存展开 / 紧凑界面和交互检查。测试前退出正在运行的 Pulse，程序使用单实例保护。测试方式与验证边界见 [测试说明](docs/TESTING.md)。

已有实例时，`--capture` 可让应用保存当前真实数据与完整界面检查到 `artifacts/`；`--quit` 可正常退出。再次双击应用会显示现有实例。

## 隐私与提交检查

应用通过本机 Codex 子进程读取账号额度，认证由 Codex 管理；不读取、复制或打印登录 token。GitHub 发布不需要把任何 Codex API Key 加进仓库。

`.gitignore` 排除了凭据文件、构建产物、真实运行快照和本地验证报告。每次提交前可运行：

```powershell
git add .
.\scripts\check-publication.ps1
```

检查器只扫描 Git 暂存区，发现疑似凭据或私人文件时阻止通过，且不输出匹配的秘密内容。它不能代替人工检查；提交截图和错误报告前也请检查个人信息。详细边界见 [SECURITY.md](SECURITY.md)。

## 参考与依赖

- [OpenAI Codex app-server 文档](https://learn.chatgpt.com/docs/app-server)
- [LibreHardwareMonitor 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6)，MPL 2.0；原始 DLL 未修改。
- 第三方许可存放在 `licenses/`。依赖准备脚本固定版本并校验 SHA-256，PawnIO 安装器单独验证 Authenticode 签名。

## 已知限制

GPU 采集目前支持 NVIDIA，AMD / Intel GPU 需要增加适配。网络指标排除常见虚拟网卡，但特殊 VPN 驱动可能需要定制过滤。多显示器和高 DPI 已实现工作区与缩放处理，额外硬件组合仍需实测。没有配置额外 AI 服务、多账号切换、账单金额或 Token/s。

这是独立个人项目，与 OpenAI、NVIDIA 或 LibreHardwareMonitor 无隶属关系。第三方依赖的许可保留在 `licenses/`。
