#ifndef MyAppName
  #define MyAppName "smartPrinter"
#endif

#ifndef MyAppExeName
  #define MyAppExeName "MyPrinter.exe"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppPublisher
  #define MyAppPublisher "smartPrinter"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef KeysetFileName
  #define KeysetFileName "license_keyset_prod_smartprinter.json"
#endif

#ifndef AppIconFile
  #define AppIconFile "..\desktop\app.ico"
#endif

[Setup]
AppId={{D6C1F5D4-CB54-4C6E-A2A5-541E5C52B7B1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#OutputDir}
OutputBaseFilename=smartPrinter-setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#AppIconFile}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\MyPrinter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\smartprinter.appsettings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Activation\{#KeysetFileName}"; DestDir: "{app}\Activation"; Flags: ignoreversion
Source: "{#PublishDir}\frontend\*"; DestDir: "{app}\frontend"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.backup,_fix_guide.js,tests\*"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser
