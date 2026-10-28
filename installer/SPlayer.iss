; Per-user installer. It does not take over the default player.
; The settings button opens the Windows association dialog.

#define AppVersion "0.1.2.1"

[Setup]
AppId={{8F3E2A41-6C5B-4D7E-9A10-C3B21D4E5F60}
AppName=SPlayer
AppVersion={#AppVersion}
AppPublisher=SinE
AppPublisherURL=https://github.com/SinEAI/SPlayer-with-SinE
DefaultDirName={localappdata}\SinE\SPlayer
DefaultGroupName=SPlayer
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
CloseApplications=yes
CloseApplicationsFilter=SPlayer.exe
RestartApplications=no
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=SPlayer-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SPlayer.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes
SetupIconFile=..\src\SPlayer.App\Assets\SPlayer.ico

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\SPlayer"; Filename: "{app}\SPlayer.exe"
Name: "{autodesktop}\SPlayer"; Filename: "{app}\SPlayer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SPlayer.exe"; Parameters: "--register"; StatusMsg: "正在登记可以打开的文件类型..."; Flags: runhidden waituntilterminated
Filename: "{app}\SPlayer.exe"; Description: "启动 SPlayer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\SPlayer.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated
