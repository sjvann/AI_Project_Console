#ifndef MyAppVersion
  #define MyAppVersion "0.6.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\dist\win-x64"
#endif

#define MyAppName "AI_Project 控制台"
#define MyAppPublisher "sjvann"
#define MyAppURL "https://github.com/sjvann/AI_Project_Console"
#define MyAppExeName "AI_Project_Console.exe"

[Setup]
AppId={{E7C3A91F-4B2D-48A6-9F15-6D8E2C1B0A47}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\AI_Project_Console
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=AI_Project_Console-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\..\src\AiProject.Console.App\Assets\app.ico
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesetraditional"; MessagesFile: "Languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "捷徑:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\解除安裝 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "啟動 {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
