@echo off
setlocal
set "ROOT=%~dp0"
if "%~1"=="" (
  set "ARGS=-RemoveCommandPackages"
) else (
  set "ARGS=%*"
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%Install-RightAgent.ps1" %ARGS%
exit /b %ERRORLEVEL%
