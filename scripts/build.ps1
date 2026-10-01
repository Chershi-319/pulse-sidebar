$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$vendor = Join-Path $projectRoot 'vendor\LibreHardwareMonitor'
$out = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
if (!(Test-Path (Join-Path $vendor 'LibreHardwareMonitorLib.dll'))) { throw '请先运行 scripts\prepare.ps1 获取官方传感器依赖。' }
Get-ChildItem -LiteralPath $vendor -Filter '*.dll' | ForEach-Object {
    $destination = Join-Path $out $_.Name
    if (!(Test-Path $destination) -or (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }
}
if (Test-Path (Join-Path $projectRoot 'vendor\PawnIO_setup.exe')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\PawnIO_setup.exe') -Destination $out -Force }
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Management.dll','System.Drawing.dll','System.Windows.Forms.dll','Microsoft.CSharp.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $framework 'WPF') $_) }
$references += '/reference:' + (Join-Path $out 'LibreHardwareMonitorLib.dll')
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/out:' + (Join-Path $out 'PulseSidebar.exe')) @references @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath (Join-Path $vendor 'LibreHardwareMonitor.exe.config') -Destination (Join-Path $out 'PulseSidebar.exe.config') -Force
Write-Output (Join-Path $out 'PulseSidebar.exe')
