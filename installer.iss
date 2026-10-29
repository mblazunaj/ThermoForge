#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
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

[Tasks]
Name: "desktopicon"; Description: "Stvori prečac na radnoj površini"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "vendor\PawnIO_setup.exe"; Flags: dontcopy
Source: "INSTALL-INFO.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ThermoForge"; Filename: "{app}\ThermoForge.exe"; Comment: "CPU i GPU temperature i stress test"
Name: "{autodesktop}\ThermoForge"; Filename: "{app}\ThermoForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ThermoForge.exe"; Description: "Pokreni ThermoForge"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  DriverNeedsRestart: Boolean;

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
