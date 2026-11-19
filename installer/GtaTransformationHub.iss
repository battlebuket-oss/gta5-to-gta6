#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "GTA V Transformation Hub"
#define AppExeName "GtaTransformationHub.exe"
#define PublisherName "BattleBuket OSS"

[Setup]
AppId={{B0D92D6C-6C4B-4D6A-9E25-7C5F8B7A2D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
DefaultDirName={localappdata}\Programs\GtaTransformationHub
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
OutputDir=..\artifacts\installer
OutputBaseFilename=GtaTransformationHub-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent
