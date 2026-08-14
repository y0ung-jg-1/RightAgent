@echo off
setlocal
set "ROOT=%~dp0"
set "TARGET=%ROOT:~0,-1%"
set "LOG=%TEMP%\RightAgent-setup.log"
set "ERR=%TEMP%\RightAgent-setup-error.txt"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%Install-RightAgent.ps1" -SkipAppCopy -TargetDirectory "%TARGET%" -AppDirectory "%TARGET%" -CertificatePath "%ROOT%RightAgent.cer" -ResultPath "%ERR%" > "%LOG%" 2>&1
exit /b %ERRORLEVEL%
