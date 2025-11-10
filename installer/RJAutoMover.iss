#define MyAppName "RJAutoMover"
#define MyAppPublisher "RJ Software"
#define MyAppURL "https://github.com/toohighonthehog/RJAutoMover"
#define MyAppServiceName "RJAutoMoverService"
#define MyAppServiceDisplayName "RJAutoMover Service"
#define MyAppServiceDescription "Monitors folders and automatically moves files based on configured rules"

; Version will be read from version.txt during compilation
; If not specified via command line, use default
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

[Setup]
AppId={{12345678-1234-5678-9ABC-123456789ABC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf64}\RJAutoMover
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=.
OutputBaseFilename=RJAutoMoverSetup
SetupIconFile=..\Icons\base.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\RJAutoMoverTray.exe
UninstallDisplayName={#MyAppName}
DirExistsWarning=no
UninstallFilesDir={app}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Application files - copy all files from publish directories to support both self-contained and framework-dependent deployments
; Exclude debug symbols (.pdb files) from production installations
Source: "publish\service\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "publish\tray\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "publish\config\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "default-config.yaml"; DestDir: "{app}"; DestName: "config.yaml"; Flags: onlyifdoesntexist; Check: ShouldInstallConfigToAppFolder
Source: "readme.txt"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Tasks]
Name: "startservice"; Description: "Start service after installation"; GroupDescription: "Additional tasks:"; Flags: checkedonce
Name: "starttray"; Description: "Start tray after installation"; GroupDescription: "Additional tasks:"; Flags: checkedonce
Name: "autostartup"; Description: "Create auto-startup task (starts tray icon automatically at login for all users)"; GroupDescription: "Additional tasks:"; Flags: checkedonce

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\RJAutoMoverTray.exe"
Name: "{group}\Start Tray Icon"; Filename: "{app}\RJAutoMoverTray.exe"; WorkingDir: "{app}"; Comment: "Start the RJAutoMover Tray Icon"; IconFilename: "{app}\RJAutoMoverTray.exe"
Name: "{group}\Configuration Editor"; Filename: "{app}\RJAutoMoverConfig.exe"; WorkingDir: "{app}"; Comment: "Edit RJAutoMover configuration files"; IconFilename: "{app}\RJAutoMoverConfig.exe"; IconIndex: 0
Name: "{group}\Start {#MyAppServiceDisplayName}"; Filename: "sc.exe"; Parameters: "start ""{#MyAppServiceName}"""; WorkingDir: "{app}"; Comment: "Start the RJAutoMover Service"; IconFilename: "{app}\RJAutoMoverTray.exe"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
; Install the service if it doesn't already exist (works for both fresh installs and upgrades)
Filename: "sc.exe"; Parameters: "create ""{#MyAppServiceName}"" binPath= ""{app}\RJAutoMoverService.exe"" DisplayName= ""{#MyAppServiceDisplayName}"" start= auto depend= ""HTTP/NlaSvc/EventLog/LanmanWorkstation/Netlogon"""; Flags: runhidden waituntilterminated; Check: not IsServiceInstalled
; Always set the service description (for both new installs and upgrades)
Filename: "sc.exe"; Parameters: "description ""{#MyAppServiceName}"" ""{#MyAppServiceDescription}"""; Flags: runhidden waituntilterminated
; Note: Scheduled task creation handled in CurStepChanged procedure

[UninstallRun]
; Stop and remove the service
Filename: "sc.exe"; Parameters: "stop ""{#MyAppServiceName}"""; Flags: runhidden waituntilterminated; RunOnceId: "stop_service"
Filename: "sc.exe"; Parameters: "delete ""{#MyAppServiceName}"""; Flags: runhidden waituntilterminated; RunOnceId: "delete_service"
; Remove the scheduled task (only if it exists) - note: task is created per-user
Filename: "schtasks.exe"; Parameters: "/delete /tn ""RJAutoMover"" /f"; Flags: runhidden waituntilterminated; Check: IsScheduledTaskExists; RunOnceId: "delete_task"

[Code]
var
  ResultCode: Integer;
  IsUpgrade: Boolean;
  BackupConfigPath: String;
  BackupLogsPath: String;
  BackupActivityDbPath: String;
  ServiceWasRunning: Boolean;
  TrayWasRunning: Boolean;

function CheckDotNetRuntime(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;

  // Check if dotnet command is available and can list runtimes
  if Exec('cmd.exe', '/c dotnet --list-runtimes 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
    begin
      // Check for .NET 10.0 runtime (any patch version)
      if Exec('cmd.exe', '/c dotnet --list-runtimes | findstr "Microsoft.NETCore.App 10.0" >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        if ResultCode = 0 then
        begin
          // Also check for ASP.NET Core runtime which is required
          if Exec('cmd.exe', '/c dotnet --list-runtimes | findstr "Microsoft.AspNetCore.App 10.0" >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          begin
            Result := (ResultCode = 0);
          end;
        end;
      end;
    end;
  end;
end;

function IsUpgradeInstallation(): Boolean;
begin
  // Check if the application is already installed by looking for the main executable
  Result := FileExists(ExpandConstant('{app}\RJAutoMoverService.exe')) or FileExists(ExpandConstant('{app}\RJAutoMoverTray.exe'));
end;

function IsServiceInstalled(): Boolean;
begin
  // Check if the service exists using sc query
  Result := Exec('sc.exe', 'query "' + '{#MyAppServiceName}' + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function IsServiceRunning(): Boolean;
begin
  // Check if the service is running using sc query
  Result := Exec('sc.exe', 'query "' + '{#MyAppServiceName}' + '" | findstr "RUNNING"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function IsTrayRunning(): Boolean;
begin
  // Check if RJAutoMoverTray.exe is running using tasklist
  Result := Exec('tasklist.exe', '/FI "IMAGENAME eq RJAutoMoverTray.exe" | findstr "RJAutoMoverTray.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function IsScheduledTaskExists(): Boolean;
begin
  // Check if the RJAutoMover scheduled task exists
  Result := Exec('schtasks.exe', '/query /tn "RJAutoMover"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function ShouldInstallConfigToAppFolder(): Boolean;
var
  ProgramDataConfigPath: String;
begin
  // Check if config.yaml exists in ProgramData location
  ProgramDataConfigPath := ExpandConstant('{commonappdata}\RJAutoMover\config.yaml');

  // If we're upgrading AND config exists in ProgramData, don't install to Program Files
  if IsUpgradeInstallation() and FileExists(ProgramDataConfigPath) then
  begin
    Log('Upgrade detected with config in ProgramData - skipping Program Files config creation');
    Result := False;
  end
  else
  begin
    // For fresh installs or upgrades without ProgramData config, allow installation
    Result := True;
  end;
end;

function GetCurrentUserDomainAndName(): String;
var
  UserDomain: String;
  UserName: String;
  TempFile: String;
  ResultFile: AnsiString;
begin
  // Get environment variables for current user domain and name
  UserDomain := GetEnv('USERDOMAIN');
  UserName := GetEnv('USERNAME');

  Log('Environment USERDOMAIN: ' + UserDomain);
  Log('Environment USERNAME: ' + UserName);

  // Fallback: try to get current user via whoami command
  if (UserName = '') or (UserDomain = '') then
  begin
    Log('Environment variables empty, trying whoami command');
    TempFile := ExpandConstant('{tmp}\whoami.txt');
    if Exec('cmd.exe', '/c whoami > "' + TempFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if LoadStringFromFile(TempFile, ResultFile) then
      begin
        ResultFile := Trim(ResultFile);
        Log('whoami result: ' + String(ResultFile));
        DeleteFile(TempFile);
        if Pos('\', String(ResultFile)) > 0 then
        begin
          Result := String(ResultFile);
          Exit;
        end;
      end;
    end;
  end;

  if (UserDomain = '') then
    UserDomain := GetEnv('COMPUTERNAME');

  if (UserName = '') then
    UserName := 'Unknown';

  Result := UserDomain + '\' + UserName;
  Log('Final user result: ' + Result);
end;


procedure UpdateServiceConfiguration();
begin
  if IsUpgrade then
  begin
    Log('Updating service binary path for upgrade');

    // Update the service binary path to point to the new executable
    Exec('sc.exe', 'config "' + '{#MyAppServiceName}' + '" binPath= "' + ExpandConstant('{app}') + '\RJAutoMoverService.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    if ResultCode = 0 then
      Log('Successfully updated service binary path')
    else
      Log('Warning: Failed to update service binary path. Error code: ' + IntToStr(ResultCode));

    // Note: Service description is updated in [Run] section
  end;
end;

procedure BackupUserData();
var
  TempDir: String;
begin
  if IsUpgradeInstallation() then
  begin
    IsUpgrade := True;
    TempDir := ExpandConstant('{tmp}');

    // Detect running state before stopping anything
    ServiceWasRunning := IsServiceRunning();
    TrayWasRunning := IsTrayRunning();

    if ServiceWasRunning then
      Log('Service was running: True')
    else
      Log('Service was running: False');

    if TrayWasRunning then
      Log('Tray was running: True')
    else
      Log('Tray was running: False');

    // Backup config.yaml if it exists
    if FileExists(ExpandConstant('{app}\config.yaml')) then
    begin
      BackupConfigPath := TempDir + '\config.yaml.backup';
      if CopyFile(ExpandConstant('{app}\config.yaml'), BackupConfigPath, False) then
        Log('Backed up config.yaml to: ' + BackupConfigPath)
      else
        Log('Failed to backup config.yaml');
    end;

    // Backup Logs directory if it exists in old location ({app}\Logs)
    // This is for upgrading from old versions that stored logs in Program Files
    if DirExists(ExpandConstant('{app}\Logs')) then
    begin
      BackupLogsPath := TempDir + '\Logs.backup';
      if CreateDir(BackupLogsPath) then
      begin
        if Exec('xcopy.exe', '"' + ExpandConstant('{app}\Logs') + '" "' + BackupLogsPath + '" /E /I /H /Y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          Log('Backed up Logs directory from {app}\Logs to: ' + BackupLogsPath)
        else
          Log('Failed to backup Logs directory from {app}\Logs');
      end;
    end
    // Also check ProgramData location (current location)
    else if DirExists(ExpandConstant('{commonappdata}\RJAutoMover\Logs')) then
    begin
      BackupLogsPath := TempDir + '\Logs.backup';
      if CreateDir(BackupLogsPath) then
      begin
        if Exec('xcopy.exe', '"' + ExpandConstant('{commonappdata}\RJAutoMover\Logs') + '" "' + BackupLogsPath + '" /E /I /H /Y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          Log('Backed up Logs directory from ProgramData to: ' + BackupLogsPath)
        else
          Log('Failed to backup Logs directory from ProgramData');
      end;
    end;

    // Backup ActivityHistory.db if it exists (check multiple possible locations)
    // Priority 1: ProgramData\RJAutoMover\Data (new shared location)
    // Priority 2: {app}\Data (old location)
    // Priority 3: {app}\ (very old location)
    if FileExists(ExpandConstant('{commonappdata}\RJAutoMover\Data\ActivityHistory.db')) then
    begin
      BackupActivityDbPath := TempDir + '\ActivityHistory.db.backup';
      if CopyFile(ExpandConstant('{commonappdata}\RJAutoMover\Data\ActivityHistory.db'), BackupActivityDbPath, False) then
        Log('Backed up ActivityHistory.db from ProgramData\RJAutoMover\Data to: ' + BackupActivityDbPath)
      else
        Log('Failed to backup ActivityHistory.db from ProgramData\RJAutoMover\Data');
    end
    else if FileExists(ExpandConstant('{app}\Data\ActivityHistory.db')) then
    begin
      BackupActivityDbPath := TempDir + '\ActivityHistory.db.backup';
      if CopyFile(ExpandConstant('{app}\Data\ActivityHistory.db'), BackupActivityDbPath, False) then
        Log('Backed up ActivityHistory.db from {app}\Data to: ' + BackupActivityDbPath)
      else
        Log('Failed to backup ActivityHistory.db from {app}\Data');
    end
    else if FileExists(ExpandConstant('{app}\ActivityHistory.db')) then
    begin
      BackupActivityDbPath := TempDir + '\ActivityHistory.db.backup';
      if CopyFile(ExpandConstant('{app}\ActivityHistory.db'), BackupActivityDbPath, False) then
        Log('Backed up ActivityHistory.db from {app} root to: ' + BackupActivityDbPath)
      else
        Log('Failed to backup ActivityHistory.db from {app} root');
    end;
  end
  else
  begin
    IsUpgrade := False;
    ServiceWasRunning := False;
    TrayWasRunning := False;
  end;
end;

procedure RestoreUserData();
begin
  if IsUpgrade then
  begin
    // Restore config.yaml if we backed it up
    if (BackupConfigPath <> '') and FileExists(BackupConfigPath) then
    begin
      if CopyFile(BackupConfigPath, ExpandConstant('{app}\config.yaml'), False) then
        Log('Restored config.yaml from backup')
      else
        Log('Failed to restore config.yaml from backup');
    end;

    // Restore Logs directory if we backed it up (to new ProgramData location)
    if (BackupLogsPath <> '') and DirExists(BackupLogsPath) then
    begin
      // Create ProgramData\RJAutoMover\Logs folder if it doesn't exist
      if not DirExists(ExpandConstant('{commonappdata}\RJAutoMover\Logs')) then
        ForceDirectories(ExpandConstant('{commonappdata}\RJAutoMover\Logs'));

      if Exec('xcopy.exe', '"' + BackupLogsPath + '" "' + ExpandConstant('{commonappdata}\RJAutoMover\Logs') + '" /E /I /H /Y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        Log('Restored Logs directory to ProgramData\RJAutoMover\Logs from backup')
      else
        Log('Failed to restore Logs directory to ProgramData\RJAutoMover\Logs from backup');
    end;

    // Restore ActivityHistory.db if we backed it up (to new ProgramData location)
    if (BackupActivityDbPath <> '') and FileExists(BackupActivityDbPath) then
    begin
      // Create ProgramData\RJAutoMover\Data folder if it doesn't exist
      if not DirExists(ExpandConstant('{commonappdata}\RJAutoMover\Data')) then
        ForceDirectories(ExpandConstant('{commonappdata}\RJAutoMover\Data'));

      if CopyFile(BackupActivityDbPath, ExpandConstant('{commonappdata}\RJAutoMover\Data\ActivityHistory.db'), False) then
        Log('Restored ActivityHistory.db to ProgramData\RJAutoMover\Data from backup')
      else
        Log('Failed to restore ActivityHistory.db to ProgramData\RJAutoMover\Data from backup');
    end;
  end;
end;

function GetUserDownloadsFolder(): String;
var
  TempFile: String;
  ResultFile: AnsiString;
  PowerShellCmd: String;
begin
  // Try to get Downloads folder using PowerShell
  TempFile := ExpandConstant('{tmp}\downloads_path.txt');
  PowerShellCmd := '-NoProfile -Command "[Environment]::GetFolderPath(''MyDocuments'') -replace ''Documents$'', ''Downloads'' | Out-File -FilePath ''' + TempFile + ''' -Encoding ASCII -NoNewline"';

  Log('Getting user Downloads folder');
  if Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if (ResultCode = 0) and FileExists(TempFile) then
    begin
      if LoadStringFromFile(TempFile, ResultFile) then
      begin
        Result := Trim(String(ResultFile));
        Log('Downloads folder from PowerShell: ' + Result);
        DeleteFile(TempFile);

        // Validate the path exists
        if DirExists(Result) then
          Exit;
      end;
    end;
  end;

  // Fallback: construct from USERPROFILE
  Result := GetEnv('USERPROFILE') + '\Downloads';
  Log('Fallback Downloads folder: ' + Result);

  // Final fallback if environment variable doesn't work
  if not DirExists(Result) then
  begin
    Result := 'C:\Users\' + GetEnv('USERNAME') + '\Downloads';
    Log('Final fallback Downloads folder: ' + Result);
  end;
end;

procedure ProcessConfigFile();
var
  ConfigPath: String;
  ConfigContent: AnsiString;
  DownloadsFolder: String;
  ConfigString: String;
begin
  // Only process config for new installations (not upgrades)
  if not IsUpgrade then
  begin
    ConfigPath := ExpandConstant('{app}\config.yaml');

    if FileExists(ConfigPath) then
    begin
      Log('Processing config.yaml for first-time installation');

      // Get the user's Downloads folder
      DownloadsFolder := GetUserDownloadsFolder();
      Log('Using Downloads folder: ' + DownloadsFolder);

      // Read the config file
      if LoadStringFromFile(ConfigPath, ConfigContent) then
      begin
        ConfigString := String(ConfigContent);

        // Replace the placeholder with the actual Downloads folder path
        StringChangeEx(ConfigString, '<InstallingUserDownloads>', DownloadsFolder, True);

        // Write back to the file
        if SaveStringToFile(ConfigPath, AnsiString(ConfigString), False) then
        begin
          Log('Successfully processed config.yaml');
        end
        else
        begin
          Log('ERROR: Failed to write processed config.yaml');
        end;
      end
      else
      begin
        Log('ERROR: Failed to read config.yaml');
      end;
    end
    else
    begin
      Log('ERROR: Config file not found at: ' + ConfigPath);
    end;
  end
  else
    Log('Upgrade detected - skipping config processing (existing config preserved)');
end;

function InitializeSetup(): Boolean;
begin
  // Self-contained deployment - no .NET runtime check needed
  // The .NET runtime is embedded in the executables
  Result := True;
end;

procedure CreateAutoStartupTask();
var
  PowerShellCmd: String;
  LauncherContent: String;
  LauncherPath: String;
begin
  try
    // Create launcher batch file
    LauncherPath := ExpandConstant('{app}') + '\RJTrayLauncher.bat';
    LauncherContent := '@echo off' + #13#10 + 'start "" "' + ExpandConstant('{app}') + '\RJAutoMoverTray.exe"';

    Log('Creating launcher batch file: ' + LauncherPath);
    if SaveStringToFile(LauncherPath, LauncherContent, False) then
      Log('Successfully created launcher batch file')
    else
      Log('Failed to create launcher batch file');

    // Create a scheduled task that runs for ALL users at logon with LIMITED (non-admin) privileges
    // This allows any user to run the tray icon without requiring admin privileges
    PowerShellCmd := '-Command "' +
      '$trigger = New-ScheduledTaskTrigger -AtLogOn; ' +
      '$action = New-ScheduledTaskAction -Execute ''' + ExpandConstant('{app}') + '\RJTrayLauncher.bat''; ' +
      '$principal = New-ScheduledTaskPrincipal -GroupId ''Users'' -LogonType Interactive -RunLevel Limited; ' +
      '$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0; ' +
      'Register-ScheduledTask -TaskName ''RJAutoMover'' -Trigger $trigger -Action $action -Principal $principal -Settings $settings -Force"';

    Log('Creating all-user scheduled task with standard (non-admin) privileges');
    Log('PowerShell command: ' + PowerShellCmd);

    if Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 0 then
        Log('Successfully created all-user scheduled task (non-admin)')
      else
        Log('Failed to create scheduled task. Exit code: ' + IntToStr(ResultCode));
    end
    else
      Log('Failed to execute PowerShell');
  except
    Log('Exception occurred while creating scheduled task');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Detect upgrade and backup user data now that {app} is available
    BackupUserData();

    // Always stop tray application if it's running (for both fresh installs and upgrades)
    if IsTrayRunning() then
    begin
      Log('Stopping tray application before installation');
      Exec('taskkill.exe', '/F /IM RJAutoMoverTray.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
    end;

    // Always stop service if it's running (for both fresh installs and upgrades)
    if IsServiceRunning() then
    begin
      Log('Stopping service before installation');
      Exec('sc.exe', 'stop "' + '{#MyAppServiceName}' + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(2000);
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    // Restore user data after installation
    RestoreUserData();

    // Create shared ProgramData folder structure (accessible to all users)
    Log('Creating shared ProgramData\RJAutoMover folder structure');

    // Create Logs folder
    if ForceDirectories(ExpandConstant('{commonappdata}\RJAutoMover\Logs')) then
      Log('Successfully created ProgramData\RJAutoMover\Logs')
    else
      Log('Warning: Failed to create ProgramData\RJAutoMover\Logs folder');

    // Create Data folder for database
    if ForceDirectories(ExpandConstant('{commonappdata}\RJAutoMover\Data')) then
      Log('Successfully created ProgramData\RJAutoMover\Data')
    else
      Log('Warning: Failed to create ProgramData\RJAutoMover\Data folder');

    // Set permissions so all users can write to these folders
    // icacls grants modify permissions to the Users group
    if Exec('icacls.exe', '"' + ExpandConstant('{commonappdata}\RJAutoMover') + '" /grant Users:(OI)(CI)M /T /Q', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log('Successfully set permissions on ProgramData\RJAutoMover folder')
    else
      Log('Warning: Failed to set permissions. Exit code: ' + IntToStr(ResultCode));

    // Process config file for new installations (replace placeholders with actual paths)
    ProcessConfigFile();

    // Update service configuration (binary path) for upgrades
    UpdateServiceConfiguration();

    // Restart service based on previous state or user selection
    if WizardIsTaskSelected('startservice') or ServiceWasRunning then
    begin
      Log('Starting service');
      Exec('sc.exe', 'start "' + '{#MyAppServiceName}' + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // Start tray application if user selected the option or it was running before
    if WizardIsTaskSelected('starttray') or TrayWasRunning then
    begin
      Log('Starting tray application');
      Exec(ExpandConstant('{app}\RJAutoMoverTray.exe'), '', ExpandConstant('{app}'), SW_HIDE, ewNoWait, ResultCode);
    end;

    // Create/recreate scheduled task if:
    // 1. User selected auto-startup during installation, OR
    // 2. This is an upgrade AND the task is missing (was previously configured but got deleted)
    if WizardIsTaskSelected('autostartup') then
    begin
      Log('Auto-startup task selected - creating scheduled task');
      CreateAutoStartupTask();
    end
    else if IsUpgrade and not IsScheduledTaskExists() then
    begin
      // During upgrade, if the auto-startup task is missing, assume it should be recreated
      // This handles cases where the task was deleted or corrupted between versions
      Log('Upgrade detected and auto-startup task is missing - recreating scheduled task');
      CreateAutoStartupTask();
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Stop the tray application
    if Exec('taskkill.exe', '/F /IM RJAutoMoverTray.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Sleep(1000);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    // Remove any remaining files and directories (logs, temp files, etc.)
    if DirExists(ExpandConstant('{app}')) then
    begin
      DelTree(ExpandConstant('{app}'), True, True, True);
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Set tasks as checked by default when we reach the tasks page
  if CurPageID = wpSelectTasks then
  begin
    WizardSelectTasks('startservice');
    WizardSelectTasks('starttray');
    WizardSelectTasks('autostartup');
  end;
end;


