@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "FRAME_SCOUT_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%FRAME_SCOUT_CSC%" (
  echo 请先安装 .NET Framework 4.8。
  exit /b 1
)
"%FRAME_SCOUT_CSC%" /nologo /utf8output /reference:System.Core.dll /out:"源码\重新编译.exe" "源码\Build.cs"
if errorlevel 1 exit /b 1
"源码\重新编译.exe"
if errorlevel 1 (
  type "源码\编译结果.txt"
  exit /b 1
)
echo 构建成功：视频巡检.exe
endlocal
