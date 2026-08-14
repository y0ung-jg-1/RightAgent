@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "try { $existing = [System.Threading.Mutex]::OpenExisting('Global\RightAgent.Setup'); $existing.Dispose(); exit 1618 } catch [System.Threading.WaitHandleCannotBeOpenedException] { exit 0 } catch { exit 1618 }"
exit /b %ERRORLEVEL%
