#ifndef PayloadDir
  #error PayloadDir must point to the extracted RightAgent release bundle.
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output directory.
#endif
#ifndef AppVersion
  #error AppVersion must be the display version, for example 1.0.1.
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
PrivilegesRequired=admin
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
english.InstallingRightAgent=Installing the signed RightAgent package for the current Windows user...
english.InstallFailed=RightAgent installation failed with exit code %1. Review the Setup log for details.
english.TrustFailed=The RightAgent signing certificate could not be trusted. The elevated step exited with code %1.
english.MissingInstaller=The embedded RightAgent installation script is missing.
english.InstallComplete=RightAgent was installed successfully. If File Explorer cached the old menu, close all File Explorer windows or sign out once.
chinesesimplified.InstallingRightAgent=正在为当前 Windows 用户安装已签名的 RightAgent 包…
chinesesimplified.InstallFailed=RightAgent 安装失败，退出代码为 %1。请查看安装日志了解详情。
chinesesimplified.TrustFailed=无法信任 RightAgent 签名证书，管理员阶段退出代码为 %1。
chinesesimplified.MissingInstaller=内嵌的 RightAgent 安装脚本缺失。
chinesesimplified.InstallComplete=RightAgent 已安装成功。如果资源管理器仍缓存旧菜单，请关闭全部资源管理器窗口或注销一次。

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\RightAgentPayload"; Flags: recursesubdirs createallsubdirs deleteafterinstall

[Code]
procedure InstallRightAgentPackage;
var
  PowerShellPath: String;
  InstallerScriptPath: String;
  PowerShellArguments: String;
  ResultCode: Integer;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  InstallerScriptPath := ExpandConstant('{tmp}\RightAgentPayload\Install-RightAgent.ps1');

  if not FileExists(InstallerScriptPath) then
    RaiseException(CustomMessage('MissingInstaller'));

  WizardForm.StatusLabel.Caption := CustomMessage('InstallingRightAgent');
  PowerShellArguments :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    InstallerScriptPath + '" -TrustCertificateOnly';

  if not Exec(
    PowerShellPath,
    PowerShellArguments,
    ExpandConstant('{tmp}\RightAgentPayload'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    RaiseException(FmtMessage(CustomMessage('TrustFailed'), ['not started']));
  end;

  if ResultCode <> 0 then
    RaiseException(FmtMessage(CustomMessage('TrustFailed'), [IntToStr(ResultCode)]));

  PowerShellArguments :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    InstallerScriptPath + '"';

  if not ExecAsOriginalUser(
    PowerShellPath,
    PowerShellArguments,
    ExpandConstant('{tmp}\RightAgentPayload'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    RaiseException(FmtMessage(CustomMessage('InstallFailed'), ['not started']));
  end;

  if ResultCode <> 0 then
    RaiseException(FmtMessage(CustomMessage('InstallFailed'), [IntToStr(ResultCode)]));
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
