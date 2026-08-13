#ifndef PayloadDir
  #error PayloadDir must point to the extracted RightAgent release bundle.
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output directory.
#endif
#ifndef AppVersion
  #error AppVersion must be the display version, for example 1.2.3.
#endif
#ifndef PackageVersion
  #error PackageVersion must be the four-part MSIX version.
#endif
#ifndef ChineseLanguageFile
  #error ChineseLanguageFile must point to the pinned Inno Setup translation.
#endif

[Setup]
AppId=RightAgent.Setup
AppName=RightAgent
AppVersion={#AppVersion}
AppPublisher=RightAgent
AppPublisherURL=https://github.com/y0ung-jg-1/RightAgent
AppSupportURL=https://github.com/y0ung-jg-1/RightAgent/issues
DefaultDirName={autopf}\RightAgent
CreateAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
Uninstallable=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=RightAgent-{#AppVersion}-x64-Setup
SetupIconFile=..\RightAgent.Package\Assets\Agents\rightagent.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=no
SetupLogging=yes
SetupMutex=RightAgent.Setup.Installation
VersionInfoVersion={#PackageVersion}
VersionInfoCompany=RightAgent
VersionInfoDescription=RightAgent installer
VersionInfoProductName=RightAgent
VersionInfoProductVersion={#PackageVersion}
VersionInfoOriginalFileName=RightAgent-{#AppVersion}-x64-Setup.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "{#ChineseLanguageFile}"

[CustomMessages]
english.InstallingRightAgent=Installing RightAgent for the current user. The first installation may take several minutes...
english.InstallingRightAgentProgress=Installing RightAgent for the current user... %1%%
english.InstallFailed=RightAgent installation failed with exit code %1. Review the Setup log for details.
english.AnotherInstall=Another RightAgent installation is already running. Wait for it to finish before starting Setup again.
english.MissingInstaller=The embedded RightAgent installation script is missing.
english.InstallComplete=RightAgent was installed successfully. Setup refreshed File Explorer to match the current menu. If the menu is still stale, close all File Explorer windows or sign out once.
chinesesimplified.InstallingRightAgent=正在为当前用户安装 RightAgent。首次安装可能需要几分钟，请耐心等待…
chinesesimplified.InstallingRightAgentProgress=正在为当前用户安装 RightAgent… %1%%
chinesesimplified.InstallFailed=RightAgent 安装失败，退出代码为 %1。请查看安装日志了解详情。
chinesesimplified.AnotherInstall=另一个 RightAgent 安装过程正在运行，请等待它结束后再重新启动安装器。
chinesesimplified.MissingInstaller=内嵌的 RightAgent 安装脚本缺失。
chinesesimplified.InstallComplete=RightAgent 已安装成功。安装器已刷新资源管理器以匹配当前菜单。若菜单仍未更新，请关闭全部资源管理器窗口或注销一次。

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\RightAgentPayload"; Flags: recursesubdirs createallsubdirs deleteafterinstall

[Code]
const
  InstallerProgressPrefix = 'RIGHTAGENT_PROGRESS:';

var
  InstallerOutputError: Boolean;

procedure HandleInstallerOutput(const OutputLine: String; const Error, FirstLine: Boolean);
var
  PercentComplete: Integer;
begin
  if Error then
  begin
    InstallerOutputError := True;
    Log('RightAgent installer output error: ' + OutputLine);
    exit;
  end;

  Log('RightAgent installer output: ' + OutputLine);
  if Pos(InstallerProgressPrefix, OutputLine) <> 1 then
    exit;

  PercentComplete := StrToIntDef(
    Copy(OutputLine, Length(InstallerProgressPrefix) + 1, MaxInt),
    -1);
  if (PercentComplete < 0) or (PercentComplete > 100) then
  begin
    Log('RightAgent installer returned an invalid progress value: ' + OutputLine);
    exit;
  end;

  WizardForm.ProgressGauge.Style := npbstNormal;
  WizardForm.ProgressGauge.Min := 0;
  WizardForm.ProgressGauge.Max := 100;
  WizardForm.ProgressGauge.Position := PercentComplete;
  WizardForm.StatusLabel.Caption := FmtMessage(CustomMessage('InstallingRightAgentProgress'), [IntToStr(PercentComplete)]);
  WizardForm.ProgressGauge.Update;
  WizardForm.StatusLabel.Update;
end;

procedure InstallRightAgentPackage;
var
  PowerShellPath: String;
  InstallerScriptPath: String;
  InstallerResultPath: String;
  PowerShellArguments: String;
  FailureDetail: AnsiString;
  ResultCode: Integer;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  InstallerScriptPath := ExpandConstant('{tmp}\RightAgentPayload\Install-RightAgent.ps1');
  InstallerResultPath := ExpandConstant('{tmp}\RightAgent-install-result.txt');

  if not FileExists(InstallerScriptPath) then
    RaiseException(CustomMessage('MissingInstaller'));

  WizardForm.StatusLabel.Caption := CustomMessage('InstallingRightAgent');
  WizardForm.ProgressGauge.Style := npbstMarquee;
  InstallerOutputError := False;
  PowerShellArguments :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    InstallerScriptPath + '" -ResultPath "' + InstallerResultPath + '"';

  if not ExecAndLogOutput(
    PowerShellPath,
    PowerShellArguments,
    ExpandConstant('{tmp}\RightAgentPayload'),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode,
    @HandleInstallerOutput) then
  begin
    WizardForm.ProgressGauge.Style := npbstNormal;
    RaiseException(FmtMessage(CustomMessage('InstallFailed'), ['not started']));
  end;

  WizardForm.ProgressGauge.Style := npbstNormal;
  if InstallerOutputError then
    Log('RightAgent installer output could not be captured completely.');

  if ResultCode = 1618 then
    RaiseException(CustomMessage('AnotherInstall'))
  else if ResultCode <> 0 then
  begin
    if LoadStringFromFile(InstallerResultPath, FailureDetail) then
      Log('RightAgent PowerShell installer failure: ' + FailureDetail);
    RaiseException(FmtMessage(CustomMessage('InstallFailed'), [IntToStr(ResultCode)]));
  end
  else
    WizardForm.ProgressGauge.Position := 100;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallRightAgentPackage;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := CustomMessage('InstallComplete');
end;
