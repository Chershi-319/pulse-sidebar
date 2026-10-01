# 隐私与安全

## 凭据边界

- 不要求输入 OpenAI API Key。
- 通过已登录的本机 `codex app-server` 读取额度和用量；Codex 负责认证与相关网络请求。
- 不读取或复制 Codex 登录凭据，不保存原始服务端日志，不发送模型推理请求。
- `scripts/probe_codex.py` 仅输出能力检测摘要，不输出账号 ID、邮箱、主目录、原始额度响应或用量数字。

## 本地数据

界面设置、温度采样和异常日志保存在当前用户的 `%LOCALAPPDATA%\PulseSidebar`。主动运行 UI 测试或截图命令会在 `artifacts/` 生成真实硬件和账号用量快照。错误日志也可能包含本机文件路径。

这些内容用于本地排错，不应上传公开仓库。`.gitignore` 与暂存区检查脚本提供基础防护，不能保证识别所有形式的个人信息。分享诊断前应人工审阅并脱敏。

## 权限与依赖

主界面和 Codex 连接使用当前用户权限。CPU 温度采集需要单独的 Windows UAC 授权；必要时安装官方 LibreHardwareMonitor 发布包中的 PawnIO 驱动。采集器仅启用 CPU 读取，未实现风扇或电压控制，随主程序退出停止。

依赖脚本固定 LibreHardwareMonitor 版本并校验 SHA-256，安装器校验 Authenticode 签名。仓库不包含二进制驱动、下载缓存或本机凭据。

## 报告问题

普通缺陷可在 GitHub Issues 提交。请不要在 Issue、评论、截图或补丁中附上 API Key、token、账号 ID、私人邮箱或未经脱敏的日志。涉及安全漏洞时优先使用仓库的私密漏洞报告入口（若可用）。
