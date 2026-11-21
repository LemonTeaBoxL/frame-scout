$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildTool = Join-Path $PSScriptRoot '重新编译.exe'
& $compiler /nologo /utf8output /reference:System.Core.dll ("/out:" + $buildTool) (Join-Path $PSScriptRoot 'Build.cs')
if ($LASTEXITCODE -ne 0) { throw '构建工具编译失败。' }
$buildProcess = Start-Process -FilePath $buildTool -WindowStyle Hidden -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) { throw '构建未完成，请查看同目录的编译结果.txt' }
Write-Host '编译成功：视频巡检.exe'
