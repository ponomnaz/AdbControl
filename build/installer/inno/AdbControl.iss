#define MyAppName "AdbControl"
#define MyAppPublisher "ponomnaz"
#define MyAppExeName "AdbControl.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\..\artifacts\publish\installer\win-x64"
#endif

[Setup]
AppId={{A3E209D8-5A49-413E-A7FE-C4E629D1C4C5}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\..\artifacts\installer\win-x64
OutputBaseFilename=AdbControl-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\..\..\assets\icons\icon_adb.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Полная очистка после удаления: рабочие данные приложения и сама папка установки.
Type: filesandordirs; Name: "{localappdata}\AdbControl"
Type: filesandordirs; Name: "{app}"
