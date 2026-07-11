#define MyAppName "EuroScope Launcher"
#define MyAppVersion GetStringFileInfo("..\src\EuroScopeLauncher\bin\Release\net8.0-windows\win-x64\publish\EuroScopeLauncher.exe", "ProductVersion")
#define MyAppPublisher "ab-vatnz"
#define MyAppExeName "EuroScopeLauncher.exe"

[Setup]
AppId={{8AA6C685-BD98-4213-B9D1-045734354B28}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/ab-vatnz/Euroscope-Launcher
DefaultDirName={autopf}\EuroScope Launcher
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=EuroScopeLauncher-Setup
SetupIconFile=..\assets\EuroScope.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest

[Files]
Source: "..\src\EuroScopeLauncher\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
