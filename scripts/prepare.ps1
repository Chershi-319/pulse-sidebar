$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$vendorRoot = Join-Path $projectRoot 'vendor'
$release = Join-Path $vendorRoot 'LibreHardwareMonitor'
$archive = Join-Path $vendorRoot 'LibreHardwareMonitor.zip'
New-Item -ItemType Directory -Force -Path $vendorRoot | Out-Null
if (!(Test-Path $archive)) {
    Invoke-WebRequest 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' -OutFile $archive
}
$expected = '086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw '传感器依赖校验失败。' }
Expand-Archive -LiteralPath $archive -DestinationPath $release -Force
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $release 'LibreHardwareMonitor.exe'))
$resource = $assembly.GetManifestResourceNames() | Where-Object { $_ -like '*PawnIO_setup.exe' } | Select-Object -First 1
if (!$resource) { throw '官方传感器包不包含 PawnIO 安装器。' }
$stream = $assembly.GetManifestResourceStream($resource)
$installer = Join-Path $vendorRoot 'PawnIO_setup.exe'
$file = [IO.File]::Create($installer)
try { $stream.CopyTo($file) } finally { $file.Dispose(); $stream.Dispose() }
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid') { throw 'PawnIO 安装器签名验证失败。' }
Write-Output '已获取并校验 LibreHardwareMonitor 0.9.6 与其签名 PawnIO 安装器。'
$licenses = Join-Path $projectRoot 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LICENSE' -OutFile (Join-Path $licenses 'LibreHardwareMonitor-MPL-2.0.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/THIRD-PARTY-NOTICES.txt' -OutFile (Join-Path $licenses 'THIRD-PARTY-NOTICES.txt')
