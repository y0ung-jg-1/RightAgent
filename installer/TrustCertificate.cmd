@echo off
setlocal
set "SCRIPT=%~dp0Install-RightAgent.ps1"
set "CERT=%~dp0RightAgent.cer"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%SCRIPT%" -TrustCertificateOnly -CertificatePath "%CERT%"
exit /b %ERRORLEVEL%
