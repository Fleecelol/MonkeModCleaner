#define AppName "MonkeModCleaner"
#define AppVersion "1.0.0"
#define AppPublisher "MonkeModCleaner"
#define AppExeName "MonkeModCleaner.exe"

[Setup]
AppId={{B8F3D2A1-7C4E-4D9A-A1B2-3C4D5E6F7890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installers
OutputBaseFilename={#AppName}_v{#AppVersion}_{#Arch}
Compression=lzma2/ultra64
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=MonkeModCleaner.ico
WizardStyle=modern
PrivilegesRequired=admin

[Files]
Source: "publish\{#RID}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent