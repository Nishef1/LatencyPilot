#define AppName "LatencyPilot"
#define AppPublisher "Nishef1"
#define AppUrl "https://github.com/Nishef1/LatencyPilot"
#define AppVersion GetEnv("LATENCYPILOT_VERSION")

#if AppVersion == ""
  #error LATENCYPILOT_VERSION must be set before compiling the installer.
#endif

[Setup]
AppId={{7F56C628-609D-4A6F-9B46-2910939517EE}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\LatencyPilot
DefaultGroupName=LatencyPilot
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=LatencyPilot-{#AppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayIcon={app}\App\LatencyPilot.exe
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\payload\App\*"; DestDir: "{app}\App"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\payload\Service\*"; DestDir: "{app}\Service"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\payload\Install-Service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\Uninstall-Service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\PHYSICAL_VALIDATION.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\VERSION.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\BUILD_INFO.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LatencyPilot"; Filename: "{app}\App\LatencyPilot.exe"; WorkingDir: "{app}\App"
Name: "{autodesktop}\LatencyPilot"; Filename: "{app}\App\LatencyPilot.exe"; WorkingDir: "{app}\App"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-Service.ps1"""; StatusMsg: "Installing the read-only observation service..."; Flags: runhidden waituntilterminated
Filename: "{app}\App\LatencyPilot.exe"; Description: "Launch LatencyPilot"; WorkingDir: "{app}\App"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-Service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveObservationService"

[Code]
const
  DotNetRuntimeUrl = 'https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe';
  WindowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

function OnDownloadProgress(
  const Url, FileName: String;
  const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    'Preparing LatencyPilot',
    'Downloading required Microsoft runtimes when needed...',
    @OnDownloadProgress);
end;

function HasDotNet10Runtime(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(
    ExpandConstant('{autopf}\dotnet\shared\Microsoft.NETCore.App\10.*'),
    FindRec);
  if Result then
    FindClose(FindRec);
end;

function VerifyMicrosoftSignature(const FileName: String): Boolean;
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"& { param([string]$p) ' +
    '$s=Get-AuthenticodeSignature -LiteralPath $p; ' +
    'if ($s.Status -eq ''Valid'' -and $null -ne $s.SignerCertificate -and ' +
    '$s.SignerCertificate.Subject -match ''Microsoft'') { exit 0 } else { exit 42 } }" ' +
    '"' + FileName + '"';

  Result :=
    Exec(
      PowerShellPath,
      Parameters,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and
    (ResultCode = 0);
end;

function RunInstaller(
  const FileName, Parameters, FriendlyName: String;
  var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';

  if not VerifyMicrosoftSignature(FileName) then
  begin
    Result := FriendlyName + ' failed Microsoft signature verification.';
    Exit;
  end;

  if not Exec(
    FileName,
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := 'Could not start ' + FriendlyName + '.';
    Exit;
  end;

  if ResultCode = 3010 then
  begin
    NeedsRestart := True;
    Exit;
  end;

  if ResultCode <> 0 then
    Result := FriendlyName + ' failed with exit code ' + IntToStr(ResultCode) + '.';
end;

function EnsurePrerequisites(var NeedsRestart: Boolean): String;
var
  NeedDotNet: Boolean;
  DotNetInstaller: String;
  WindowsAppRuntimeInstaller: String;
begin
  Result := '';
  NeedDotNet := not HasDotNet10Runtime();
  DotNetInstaller := ExpandConstant('{tmp}\dotnet-runtime-win-x64.exe');
  WindowsAppRuntimeInstaller := ExpandConstant('{tmp}\windowsappruntimeinstall-x64.exe');

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add(DotNetRuntimeUrl, 'dotnet-runtime-win-x64.exe', '');

  // The official Windows App Runtime installer validates/repairs the complete
  // Framework/Main/Singleton/DDLM set. Running it is more reliable than making
  // a partial package-registration guess in LatencyPilot setup.
  DownloadPage.Add(
    WindowsAppRuntimeUrl,
    'windowsappruntimeinstall-x64.exe',
    '');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := 'Required Microsoft runtime download failed: ' + GetExceptionMessage;
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  if NeedDotNet then
  begin
    Result := RunInstaller(
      DotNetInstaller,
      '/install /quiet /norestart',
      '.NET 10 Runtime',
      NeedsRestart);
    if Result <> '' then
      Exit;
  end;

  Result := RunInstaller(
    WindowsAppRuntimeInstaller,
    '--quiet',
    'Windows App Runtime 2.4',
    NeedsRestart);
end;

function StopExistingService(): String;
var
  ResultCode: Integer;
  PowerShellPath: String;
begin
  Result := '';

  if not (Exec(
    ExpandConstant('{sys}\sc.exe'),
    'query LatencyPilot.Observation',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and
    (ResultCode = 0)) then
    Exit;

  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not Exec(
    PowerShellPath,
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"Stop-Service -Name ''LatencyPilot.Observation'' -Force -ErrorAction SilentlyContinue; ' +
    '$s=Get-Service -Name ''LatencyPilot.Observation'' -ErrorAction SilentlyContinue; ' +
    'if ($null -ne $s) { $s.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(15)) }"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) or
    (ResultCode <> 0) then
    Result := 'The existing LatencyPilot observation service could not be stopped. Close LatencyPilot and retry setup.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EnsurePrerequisites(NeedsRestart);
  if Result <> '' then
    Exit;

  Result := StopExistingService();
end;
