# 测试说明

## 自动检查

从源码构建后运行：

```powershell
$test = Start-Process .\bin\PulseSidebar.exe -ArgumentList '--self-test' -PassThru -Wait
if ($test.ExitCode -ne 0) { throw 'Self-test failed' }
```

覆盖：多个额度桶、5 / 12 小时及 7 天窗口映射、缺失值、剩余比例边界、已到期重置时间、非有限数字和缺失用量。测试数据为合成数据，不需要登录 Codex。

## 本机集成检查

退出已运行的 Pulse 后执行 `--ui-test artifacts`，或对已有实例执行 `--capture`。

- 检查额度与 Codex 显示一致，缺失窗口不能显示为 100%。
- 检查 CPU / GPU、内存、网速、磁盘等真实读数更新。
- 检查温度采集授权取消、驱动缺失或传感器不可用的提示。
- 检查紧凑 / 展开、左右停靠、托盘显示 / 隐藏和键盘操作。
- 退出后检查 Codex 子进程与温度采集进程结束。

**这些命令会记录你自己的真实用量和硬件信息。`artifacts/` 已被忽略，不要强制添加到 Git。**

当前实现已在单显示器 Windows x64 与 AMD CPU / NVIDIA GPU 环境验证过基本读数和交互。混合 DPI、多显示器和重启后的自动启动仍应在对应设备上验证；不保证每一种主板的温度传感器都可用。

## 公开提交检查

`scripts/check-publication.ps1` 审查暂存区中的完整文件快照，检查疑似 token、私钥、用户绝对路径以及不应发布的文件类别。输出只包含文件名、行号和检测类型。新增模式前应使用合成内容验证，不要用真实凭据测试。
