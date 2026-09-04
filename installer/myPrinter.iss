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

#ifndef AppIconFile
  #define AppIconFile "..\desktop\app.ico"
#endif

#ifndef WebView2BootstrapperPath
  #error WebView2BootstrapperPath must point to the signed Microsoft Evergreen Bootstrapper
#endif

#ifndef SumatraPdfInstallerPath
  #error SumatraPdfInstallerPath must point to the verified SumatraPDF installer
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
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,appsettings.Development.json,THIRD-PARTY-NOTICES.txt,frontend\*.backup,frontend\_fix_guide.js,frontend\tests\*"
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#WebView2BootstrapperPath}"; Flags: dontcopy
Source: "{#SumatraPdfInstallerPath}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
const
  WebView2ClientId = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2BootstrapperName = 'MicrosoftEdgeWebview2Setup.exe';
  SumatraPdfInstallerName = 'SumatraPDF-3.6.1-64-install.exe';

function HasWebView2Version(RootKey: Integer): Boolean;
var
  Version: String;
  ClientKey: String;
begin
  ClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2ClientId;
  Result :=
    RegQueryStringValue(RootKey, ClientKey, 'pv', Version) and
    (Trim(Version) <> '') and
    (CompareText(Version, '0.0.0.0') <> 0);
end;

function IsWebView2RuntimeInstalled: Boolean;
begin
  Result :=
    HasWebView2Version(HKLM32) or
    HasWebView2Version(HKLM64) or
    HasWebView2Version(HKCU32) or
    HasWebView2Version(HKCU64);
end;

function InstallWebView2IfNeeded(var NeedsRestart: Boolean): String;
var
  BootstrapperPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if IsWebView2RuntimeInstalled then
    Exit;

  ExtractTemporaryFile(WebView2BootstrapperName);
  BootstrapperPath := ExpandConstant('{tmp}\') + WebView2BootstrapperName;

  if not Exec(
    BootstrapperPath,
    '/silent /install',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result :=
      'Smart Printer requires Microsoft Edge WebView2 Runtime, but its installer could not be started.' + #13#10 +
      'Please check Windows security settings and run Smart Printer setup again.';
    Exit;
  end;

  if ResultCode = 3010 then
    NeedsRestart := True;

  if not IsWebView2RuntimeInstalled then
  begin
    Result :=
      'Microsoft Edge WebView2 Runtime installation did not complete (exit code ' +
      IntToStr(ResultCode) + ').' + #13#10 +
      'Smart Printer was not installed because it cannot open without this runtime.';
  end;
end;

function IsCompleteSumatraPdfFolder(Folder: String): Boolean;
begin
  Result :=
    FileExists(AddBackslash(Folder) + 'SumatraPDF.exe') and
    (FileExists(AddBackslash(Folder) + 'libmupdf.dll') or
     FileExists(AddBackslash(Folder) + 'libsumatrapdf.dll'));
end;

function IsSumatraPdfInstalled: Boolean;
begin
  Result :=
    IsCompleteSumatraPdfFolder(ExpandConstant('{autopf}\SumatraPDF')) or
    IsCompleteSumatraPdfFolder(ExpandConstant('{localappdata}\SumatraPDF'));
end;

function InstallSumatraPdfIfNeeded: String;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if IsSumatraPdfInstalled then
    Exit;

  ExtractTemporaryFile(SumatraPdfInstallerName);
  InstallerPath := ExpandConstant('{tmp}\') + SumatraPdfInstallerName;

  if not Exec(
    InstallerPath,
    '-install -silent -all-users',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result :=
      'Smart Printer requires SumatraPDF for reliable PDF printing, but its installer could not be started.' + #13#10 +
      'Please check Windows security settings and run Smart Printer setup again.';
    Exit;
  end;

  if not IsSumatraPdfInstalled then
  begin
    Result :=
      'SumatraPDF installation did not complete (exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Smart Printer was not installed because PDF printing would be unreliable.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := InstallWebView2IfNeeded(NeedsRestart);
  if Result <> '' then
    Exit;

  Result := InstallSumatraPdfIfNeeded;
end;
