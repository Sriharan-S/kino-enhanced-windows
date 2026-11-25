; Kino installer (Inno Setup 6). Built by tools\Build-Release.ps1, one per architecture:
;   ISCC /DAppVersion=0.1.0 /DNumericVersion=0.1.0 /DArch=x64 /DSourceDir=<publish folder> /DOutputDir=<dist> Kino.iss
;
; Installs per user by default (no admin rights; %LOCALAPPDATA%\Programs\Kino). Admins can choose "all users".
; Registers kino:// invite links, adds a Start menu entry and an uninstaller in Settings > Apps.
; A newer version installs over an older one (same AppId). User data (%LOCALAPPDATA%\Kino) is kept unless the
; uninstaller is asked to remove it.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error SourceDir (the published app folder) is required
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{79988B3E-4E6E-41A8-A872-1EB17989BCF1}
AppName=Kino
AppVersion={#AppVersion}
AppVerName=Kino {#AppVersion}
AppPublisher=Kino
AppPublisherURL=https://github.com/Sriharan-S/kino-enhanced
AppSupportURL=https://github.com/Sriharan-S/kino-enhanced-windows/issues
AppUpdatesURL=https://github.com/Sriharan-S/kino-enhanced-windows/releases/latest
AppCopyright=Copyright (C) 2026 Sriharan-S
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoDescription=Kino setup ({#Arch})
DefaultDirName={autopf}\Kino
DefaultGroupName=Kino
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=Kino-Setup-{#Arch}
SetupIconFile=..\src\Kino.App\Assets\Kino.ico
UninstallDisplayIcon={app}\Kino.exe
UninstallDisplayName=Kino
; "max" rather than "ultra64": the compiler is a 32-bit process and runs out of memory on ultra64 with this many files.
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.19041
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x86compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Kino"; Filename: "{app}\Kino.exe"; Comment: "Watch together, in sync"
Name: "{autodesktop}\Kino"; Filename: "{app}\Kino.exe"; Tasks: desktopicon

[Registry]
; kino://join/<theatre>/<code> invite links
Root: HKA; Subkey: "Software\Classes\kino"; ValueType: string; ValueName: ""; ValueData: "URL:Kino invite link"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\kino"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\kino\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Kino.exe"",0"
Root: HKA; Subkey: "Software\Classes\kino\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Kino.exe"" ""%1"""

[Run]
Filename: "{app}\Kino.exe"; Description: "{cm:LaunchProgram,Kino}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the folder itself, if empty: never anything the user put there (the uninstaller removes what it installed).
Type: dirifempty; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app also registers kino:// for the current user when it runs; remove that too. }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\kino');
    if not UninstallSilent then
      if MsgBox('Also delete your Kino settings, preferences and device identity?' #13#10#13#10 +
                'Keep them if you plan to reinstall: your identity is how screens recognise you (host status, roles).',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{localappdata}\Kino'), True, True, True);
  end;
end;
