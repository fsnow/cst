; CST Reader - InnoSetup installer script (#403)
; Built by package-windows.ps1, which passes /DAppVersion, /DPublishDir, /DOutputDir.
; Per-user install (no admin/UAC), unsigned for beta (real code-signing cert is a pre-1.0 follow-up, #28).
; Distributed via GitHub Releases + WinGet (fsnow.CSTReader).

#ifndef AppVersion
  #define AppVersion "5.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "bin\Release\net10.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "dist"
#endif

[Setup]
; Stable AppId - do NOT change (it keys the uninstall entry / upgrade detection; WinGet ProductCode is AppId + "_is1").
AppId={{6F3A9C2E-1D4B-4A7E-9F2C-8B5E3D1A0C74}
AppName=CST Reader
AppVersion={#AppVersion}
AppVerName=CST Reader {#AppVersion}
AppPublisher=Frank Snow
AppPublisherURL=https://github.com/fsnow/cst
AppSupportURL=https://github.com/fsnow/cst/issues
DefaultDirName={autopf}\CST Reader
DefaultGroupName=CST Reader
UninstallDisplayName=CST Reader
UninstallDisplayIcon={app}\CST.Avalonia.exe
OutputDir={#OutputDir}
OutputBaseFilename=CST-Reader-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; x64compatible requires Inno Setup 6.3+ (also allows install on ARM64 via x64 emulation). Built with 6.7.x.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install so no UAC prompt; {autopf} resolves to %LOCALAPPDATA%\Programs under lowest privileges.
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The self-contained publish output (app + .NET runtime + CEF + staged xsl/ + dictionaries/).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\CST Reader"; Filename: "{app}\CST.Avalonia.exe"
Name: "{group}\Uninstall CST Reader"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CST Reader"; Filename: "{app}\CST.Avalonia.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CST.Avalonia.exe"; Description: "{cm:LaunchProgram,CST Reader}"; Flags: nowait postinstall skipifsilent
