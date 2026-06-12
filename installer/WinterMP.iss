; WinterMP Launcher installer — build with:  .\tools\build-installer.ps1

#ifexist "..\src\WinterMP.Launcher\bin\publish\win-x64\WinterMPLauncher.exe"
  #define SourceDir "..\src\WinterMP.Launcher\bin\publish\win-x64"
#else
  #define SourceDir "..\src\WinterMP.Launcher\bin\Release\net8.0-windows"
#endif

[Setup]
AppId={{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}
AppName=WinterMP
AppVersion=0.1.1
AppVerName=WinterMP 0.1.1
AppPublisher=WinterMP
AppPublisherURL=https://github.com/Steinkoloss/our-winter-car
AppSupportURL=https://github.com/Steinkoloss/our-winter-car/issues
AppUpdatesURL=https://github.com/Steinkoloss/our-winter-car/releases
DefaultDirName={autopf}\WinterMP
DefaultGroupName=WinterMP
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=WinterMP-Setup
UninstallDisplayIcon={app}\WinterMPLauncher.exe
UninstallDisplayName=WinterMP Launcher
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
InfoBeforeFile=welcome.txt
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: launchafter; Description: "Launch WinterMP Launcher when setup finishes"; GroupDescription: "Options:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinterMP Launcher"; Filename: "{app}\WinterMPLauncher.exe"
Name: "{group}\Player guide"; Filename: "{app}\PLAYERS.md"
Name: "{group}\Uninstall WinterMP"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WinterMP Launcher"; Filename: "{app}\WinterMPLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WinterMPLauncher.exe"; Description: "Launch WinterMP Launcher"; Flags: nowait postinstall skipifsilent; Tasks: launchafter

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\WinterMP\downloads"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  WizardForm.StatusLabel.Caption := 'Installing BepInEx and WinterMP mod into My Winter Car...';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  if not Exec(ExpandConstant('{app}\WinterMPLauncher.exe'),
    '--install-mod --silent', ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('WinterMP Launcher was installed, but the mod installer could not run.'#13#13 +
      'Open WinterMP Launcher and click Install / Repair.',
      mbError, MB_OK);
  end
  else if ResultCode <> 0 then
  begin
    MsgBox('WinterMP Launcher is installed, but the mod could not be installed automatically.'#13#13 +
      'Make sure My Winter Car is installed via Steam, then open WinterMP Launcher and click Install / Repair.'#13#13 +
      'Details: %LOCALAPPDATA%\WinterMP\last-install.log',
      mbInformation, MB_OK);
  end;

  WizardForm.ProgressGauge.Style := npbstNormal;
end;
