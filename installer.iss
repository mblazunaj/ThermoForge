#ifndef MyAppVersion
  #define MyAppVersion "1.2.3"
#endif
#define ShortcutIconName "ThermoForge-forge.ico"
[Setup]
AppId={{53DE7C77-C9B7-4600-9501-EF29E07652DA}
AppName=ThermoForge
AppVersion={#MyAppVersion}
AppPublisher=mblazunaj
AppPublisherURL=https://github.com/mblazunaj/ThermoForge
AppUpdatesURL=https://github.com/mblazunaj/ThermoForge/releases
DefaultDirName={autopf}\ThermoForge
DefaultGroupName=ThermoForge
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\outputs
OutputBaseFilename=ThermoForge-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=ThermoForge\Assets\ThermoForge.ico
UninstallDisplayIcon={app}\ThermoForge.exe
; Refresh Explorer's icon cache for existing shortcuts after replacing the executable.
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ThermoForge.UI
SetupMutex=ThermoForge.Setup
VersionInfoVersion={#MyAppVersion}
InfoBeforeFile=INSTALL-INFO.txt
DisableWelcomePage=no
SetupLogging=yes

[Languages]
Name: "croatian"; MessagesFile: "Croatian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
croatian.LaunchThermoForge=Pokreni ThermoForge nakon instalacije
english.LaunchThermoForge=Launch ThermoForge after installation
croatian.ConfirmLaunchThermoForge=ThermoForge je uspješno instaliran.%n%nŽelite li sada pokrenuti novu verziju?
english.ConfirmLaunchThermoForge=ThermoForge has been installed successfully.%n%nWould you like to launch the new version now?

[Tasks]
Name: "desktopicon"; Description: "Stvori prečac na radnoj površini"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ThermoForge\Assets\ThermoForge.ico"; DestDir: "{app}"; DestName: "{#ShortcutIconName}"; Flags: ignoreversion
Source: "vendor\PawnIO_setup.exe"; Flags: dontcopy
Source: "INSTALL-INFO.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ThermoForge"; Filename: "{app}\ThermoForge.exe"; IconFilename: "{app}\{#ShortcutIconName}"; Comment: "CPU i GPU temperature i stress test"
Name: "{autodesktop}\ThermoForge"; Filename: "{app}\ThermoForge.exe"; IconFilename: "{app}\{#ShortcutIconName}"; Tasks: desktopicon

[Run]
Filename: "{app}\ThermoForge.exe"; Description: "{cm:LaunchThermoForge}"; Flags: nowait postinstall unchecked skipifsilent runascurrentuser
Filename: "{app}\ThermoForge.exe"; Flags: nowait skipifnotsilent runascurrentuser; Check: ConfirmSilentLaunch

[Code]
var
  DriverNeedsRestart: Boolean;
  LaunchDecisionMade, LaunchRequested: Boolean;

function ConfirmSilentLaunch: Boolean;
begin
  Result := False;
  if not WizardSilent or DriverNeedsRestart then Exit;
  if not LaunchDecisionMade then begin
    LaunchDecisionMade := True;
    LaunchRequested := SuppressibleMsgBox(
      CustomMessage('ConfirmLaunchThermoForge'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  end;
  Result := LaunchRequested;
end;

function PawnIOCurrent: Boolean;
var V: String; Packed, Minimum: Int64;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO', 'DisplayVersion', V) then
    if StrToVersion(V, Packed) and StrToVersion('2.2.0', Minimum) then Result := ComparePackedVersion(Packed, Minimum) >= 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if not PawnIOCurrent then begin
    ExtractTemporaryFile('PawnIO_setup.exe');
    if not Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'Nije moguće pokrenuti PawnIO driver. Instalacija je prekinuta.'
    else if (ExitCode = 3010) or (ExitCode = 1641) then DriverNeedsRestart := True
    else if ExitCode <> 0 then Result := 'PawnIO driver nije instaliran. Kod greške: ' + IntToStr(ExitCode) + '. Instalacija ThermoForgea je prekinuta.'
    else if not PawnIOCurrent then Result := 'PawnIO instalacija nije potvrdila verziju 2.2.0. Ponovi instalaciju nakon ponovnog pokretanja Windowsa.';
  end;
end;

function NeedRestart(): Boolean;
begin Result := DriverNeedsRestart; end;
