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
SetupIconFile=..\artifacts\payload\App\Assets\AppIcon.ico
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
Source: "..\artifacts\payload\Export-Diagnostics.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\PHYSICAL_VALIDATION.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\PORTABLE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\DIAGNOSTICS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\VERSION.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\BUILD_INFO.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\payload\PAYLOAD_SHA256.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LatencyPilot"; Filename: "{app}\App\LatencyPilot.exe"; WorkingDir: "{app}\App"
Name: "{autodesktop}\LatencyPilot"; Filename: "{app}\App\LatencyPilot.exe"; WorkingDir: "{app}\App"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-Service.ps1"""; StatusMsg: "Installing the read-only observation service..."; Flags: runhidden waituntilterminated
Filename: "{app}\App\LatencyPilot.exe"; Description: "Launch LatencyPilot"; WorkingDir: "{app}\App"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function CheckExistingRecoverySafety(): String;
var
  CheckerPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if not DirExists(ExpandConstant('{app}')) then
    exit;

  CheckerPath := ExpandConstant('{app}\Service\LatencyPilot.Service.exe');
  if not FileExists(CheckerPath) then
  begin
    Result := 'The existing LatencyPilot recovery Service is missing. Repair or safely remove the current installation before upgrading.';
    exit;
  end;

  if not Exec(
    CheckerPath,
    '--check-uninstall',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := 'The existing LatencyPilot recovery safety check could not start. Setup will not replace recovery tools.';
    exit;
  end;

  if ResultCode <> 0 then
    Result := 'LatencyPilot has a retained or unresolved managed change, or its mutation journal cannot be verified. Restore Baseline/recover first; setup will not replace recovery tools.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if not Exec(
      ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Uninstall-Service.ps1') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      SuppressibleMsgBox('The Service removal safety check could not start. LatencyPilot will remain installed.', mbError, MB_OK, IDOK);
      Abort;
    end;
    if ResultCode <> 0 then
    begin
      SuppressibleMsgBox('LatencyPilot could not safely remove its Service. Restore and verify all managed changes, and ensure the mutation journal is readable before retrying. The recovery tools will remain installed.', mbError, MB_OK, IDOK);
      Abort;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := CheckExistingRecoverySafety();
  if Result <> '' then
    exit;

  if Exec(
    ExpandConstant('{sys}\sc.exe'),
    'query LatencyPilot.Observation',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0) then
  begin
    Exec(
      ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -Command "Stop-Service -Name ''LatencyPilot.Observation'' -Force -ErrorAction SilentlyContinue; $s = Get-Service -Name ''LatencyPilot.Observation'' -ErrorAction SilentlyContinue; if ($null -ne $s) { $s.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(15)) }"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);

    if ResultCode <> 0 then
    begin
      Result := 'The existing LatencyPilot observation service could not be stopped. Close LatencyPilot and retry setup.';
      exit;
    end;
  end;

  { Re-read after shutdown so a journal transition during service stop cannot be missed. }
  Result := CheckExistingRecoverySafety();
end;
