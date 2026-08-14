@echo off
setlocal
set "ROOT=%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%Install-RightAgent.ps1" -RemoveCommandPackages
exit /b %ERRORLEVEL%
