#define MyAppName "EuroScope Launcher"
#define MyAppVersion GetStringFileInfo("..\src\EuroScopeLauncher\bin\Release\net8.0-windows\win-x64\publish\EuroScopeLauncher.exe", "ProductVersion")
#define MyAppPublisher "ab-vatnz"
#define MyAppExeName "EuroScopeLauncher.exe"

[Setup]
AppId={{8AA6C685-BD98-4213-B9D1-045734354B28}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\EuroScope Launcher
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=EuroScopeLauncher-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest

[Files]
Source: "..\src\EuroScopeLauncher\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
