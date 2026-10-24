#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#define MyAppName "MadNegar"
#define MyAppExeName "MadNegar.exe"
#ifndef MyAppPublishDir
  #define MyAppPublishDir "..\publish"
#endif

[Setup]
AppId={{7E1DAFFB-8C47-4EBB-B5B1-A94973FCAFD4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=MadNegar
DefaultDirName={autopf}\MadNegar
DefaultGroupName=MadNegar
UninstallDisplayName=MadNegar
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=..\output
OutputBaseFilename=MadNegar-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyAppPublishDir}\MadNegar.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MadNegar"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MadNegar"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MadNegar"; Flags: nowait postinstall skipifsilent

; Models and preferences are stored under %LOCALAPPDATA%\MadNegar and are intentionally preserved on uninstall.
