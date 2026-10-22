; IBDM Pro — Windows installer (per user, no administrator rights), Arabic first.
; من تصميم و تطوير و تعريب ابراهيم الخزرجي
; Built by the GitHub workflow (or BUILD-ON-WINDOWS.ps1), which passes /DAppVersion=1.0.N /DSource=<published folder>

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Source
  #define Source "..\build\publish"
#endif

[Setup]
AppId={{5B7E3A90-1D4C-4F8B-A2E6-9C31D0B7F4A1}
AppName=IBDM Pro
AppVersion={#AppVersion}
AppVerName=IBDM Pro {#AppVersion}
AppPublisher=ابراهيم الخزرجي — IBRAPROGRAM
AppPublisherURL=https://github.com/ibrata2/ibdm-pro
AppSupportURL=https://t.me/ibraprogram
AppUpdatesURL=https://github.com/ibrata2/ibdm-pro/releases/latest
AppCopyright=من تصميم و تطوير و تعريب ابراهيم الخزرجي
VersionInfoVersion={#AppVersion}
VersionInfoDescription=IBDM Pro — مدير التحميل
DefaultDirName={autopf}\IBDM Pro
DefaultGroupName=IBDM Pro
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
SetupIconFile=..\assets\ibdm.ico
WizardImageFile=wizard-side-100.bmp,wizard-side-150.bmp,wizard-side-200.bmp
WizardSmallImageFile=wizard-corner-100.bmp,wizard-corner-150.bmp,wizard-corner-200.bmp
UninstallDisplayIcon={app}\IBDMPro.exe
UninstallDisplayName=IBDM Pro
OutputDir=..\build\installer
OutputBaseFilename=IBDM-Pro-Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=none
MinVersion=10.0.17763
CloseApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"

[CustomMessages]
ar.StartWithWindows=تشغيل IBDM Pro مع بدء Windows (حتى يلتقط التحميلات من المتصفح دائماً)
ar.DesktopIcon=إنشاء اختصار على سطح المكتب
ar.Extras=خيارات:
ar.Launch=تشغيل IBDM Pro

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Extras}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Extras}"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\IBDM Pro"; Filename: "{app}\IBDMPro.exe"
Name: "{autoprograms}\IBDM Pro — إضافة المتصفح"; Filename: "{app}\extension"
Name: "{autodesktop}\IBDM Pro"; Filename: "{app}\IBDMPro.exe"; Tasks: desktopicon

[Registry]
; The same value the app's "start with Windows" setting writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IBDM Pro"; ValueData: """{app}\IBDMPro.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
; ibdmpro:// opens the app (used by the browser extension)
Root: HKCU; Subkey: "Software\Classes\ibdmpro"; ValueType: string; ValueName: ""; ValueData: "URL:IBDM Pro"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ibdmpro"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\ibdmpro\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\IBDMPro.exe"",0"
Root: HKCU; Subkey: "Software\Classes\ibdmpro\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\IBDMPro.exe"" ""%1"""

[Run]
Filename: "{app}\IBDMPro.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent
; After an update from inside the app (silent install), start it again.
Filename: "{app}\IBDMPro.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im IBDMPro.exe /f"; Flags: runhidden; RunOnceId: "StopIbdmPro"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
{ Closes a running IBDM Pro so every file can be replaced. Downloads in progress resume when it starts again. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im IBDMPro.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im yt-dlp.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
  Result := '';
end;
