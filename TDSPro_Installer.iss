#define AppName      "TDS Pro"
#define AppVersion   "2.0.0"
#define AppPublisher "TDS Pro Software"
#define AppURL       "https://tdspro.in"
#define AppExeName   "TDSPro.exe"
#define AppDesc      "TDS Compliance Software - Income-tax Act 2025"
#define SourceDir    "..\publish"
#define BuildStamp   GetDateTimeString('yyyymmdd_hhnnss', '', '')

[Setup]
AppId={{A3F2B1C4-9D8E-4F7A-B2C3-D1E5F6A7B8C9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/support
AppUpdatesURL={#AppURL}/updates
AppCopyright=Copyright 2026 TDS Pro Software
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=no
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=TDSPro_Setup_v{#AppVersion}_{#BuildStamp}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
PrivilegesRequired=admin
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppDesc}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName} v{#AppVersion}
CreateUninstallRegKey=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Main executable
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs
; FVU validation engine (NSDL patched JARs + dependencies)
Source: "C:\Users\anand\AppData\Local\Temp\TDS_FVU_extracted\TDS_STANDALONE_FVU_9.4\*"; DestDir: "{commonpf}\TDS Pro\FVU"; Flags: ignoreversion skipifsourcedoesntexist
; Docs
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "CHANGELOG.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
Name: "{userappdata}\TDSPro"; Permissions: users-full
Name: "{userappdata}\TDSPro\Backup"; Permissions: users-full
Name: "{userappdata}\TDSPro\Logs"; Permissions: users-full
Name: "{userdocs}\TDSPro"; Permissions: users-full
Name: "{userdocs}\TDSPro\Returns"; Permissions: users-full
Name: "{userdocs}\TDSPro\FVU"; Permissions: users-full
Name: "{userdocs}\TDSPro\Reports"; Permissions: users-full
Name: "{userdocs}\TDSPro\Challans"; Permissions: users-full
Name: "{userdocs}\TDSPro\Form16"; Permissions: users-full
Name: "{userdocs}\TDSPro\2025-26\Q1"; Permissions: users-full
Name: "{userdocs}\TDSPro\2025-26\Q2"; Permissions: users-full
Name: "{userdocs}\TDSPro\2025-26\Q3"; Permissions: users-full
Name: "{userdocs}\TDSPro\2025-26\Q4"; Permissions: users-full
Name: "{userdocs}\TDSPro\2026-27\Q1"; Permissions: users-full
Name: "{userdocs}\TDSPro\2026-27\Q2"; Permissions: users-full
Name: "{userdocs}\TDSPro\2026-27\Q3"; Permissions: users-full
Name: "{userdocs}\TDSPro\2026-27\Q4"; Permissions: users-full
Name: "{userdocs}\TDSPro\2027-28\Q1"; Permissions: users-full
Name: "{userdocs}\TDSPro\2027-28\Q2"; Permissions: users-full
Name: "{userdocs}\TDSPro\2027-28\Q3"; Permissions: users-full
Name: "{userdocs}\TDSPro\2027-28\Q4"; Permissions: users-full

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDesc}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; Comment: "{#AppDesc}"

[Registry]
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "DataPath"; ValueData: "{userappdata}\TDSPro"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\TDSPro\Logs"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
  DeleteData: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataPath := ExpandConstant('{userappdata}\TDSPro');
    if DirExists(DataPath) then
    begin
      DeleteData := MsgBox(
        'Do you want to DELETE your TDS Pro data?' + #13#10#13#10 +
        'Location: ' + DataPath + #13#10#13#10 +
        'This includes your database, all TDS entries, challans and backups.' + #13#10 +
        'THIS CANNOT BE UNDONE.' + #13#10#13#10 +
        'Click YES to delete all data.' + #13#10 +
        'Click NO to keep your data (recommended).',
        mbConfirmation, MB_YESNO) = IDYES;
      if DeleteData then
        DelTree(DataPath, True, True, True);
    end;
  end;
end;
