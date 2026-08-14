@echo off
setlocal
set "ROOT=%~dp0"
set "TARGET=%ROOT:~0,-1%"
set "LOG=%TEMP%\RightAgent-setup.log"
set "ERR=%TEMP%\RightAgent-setup-error.txt"
set "COPY=%LOCALAPPDATA%\RightAgent\last-setup-error.txt"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%Install-RightAgent.ps1" -SkipAppCopy -TargetDirectory "%TARGET%" -AppDirectory "%TARGET%" -CertificatePath "%ROOT%RightAgent.cer" -ResultPath "%ERR%" > "%LOG%" 2>&1
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" (
  echo RightAgent setup failed. See %ERR% and %LOG%. 1>&2
  if exist "%ERR%" (
    type "%ERR%" 1>&2
    if not exist "%LOCALAPPDATA%\RightAgent" mkdir "%LOCALAPPDATA%\RightAgent" >nul 2>&1
    copy /y "%ERR%" "%COPY%" >nul 2>&1
  )
  if exist "%LOG%" (
    echo ---- RightAgent-setup.log ---- 1>&2
    type "%LOG%" 1>&2
  )
)
exit /b %CODE%
