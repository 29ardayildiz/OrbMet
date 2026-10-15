; OrbMet installer (Inno Setup 6). Built by tools\publish.ps1, which passes
; AppVersion, SourceDir (the self-contained publish folder) and OutputDir.
;
; Per user: no administrator rights, installs under
; %LOCALAPPDATA%\Programs\OrbMet, Start menu shortcut, Add/Remove Programs
; entry. Uninstalling first runs "OrbMet.exe --uninstall-cleanup", which gives
; Claude Code its previous status line back and removes the Start with
; Windows entry, then asks whether to delete %LOCALAPPDATA%\OrbMet too.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the self-contained publish folder.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{D7931010-D851-43EC-AE6D-7D44F7B54866}
AppName=OrbMet
AppVersion={#AppVersion}
AppVerName=OrbMet {#AppVersion}
AppPublisher=Arda YILDIZ
AppPublisherURL=https://github.com/29ardayildiz/OrbMet
AppSupportURL=https://github.com/29ardayildiz/OrbMet/issues
AppUpdatesURL=https://github.com/29ardayildiz/OrbMet/releases
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\OrbMet
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; The running panel holds this mutex (App.xaml.cs); setup and uninstall ask to close it.
AppMutex=OrbMet.SingleInstance.7c1f6e4a
CloseApplications=yes
SetupIconFile=..\..\src\OrbMet.Desktop\Assets\OrbMet.ico
UninstallDisplayIcon={app}\OrbMet.exe
UninstallDisplayName=OrbMet
OutputDir={#OutputDir}
OutputBaseFilename=OrbMet-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; One file per DPI scale, 100% to 200%; Inno Setup picks the closest.
WizardImageFile=wizard-large-0.png,wizard-large-1.png,wizard-large-2.png,wizard-large-3.png,wizard-large-4.png
WizardSmallImageFile=wizard-small-0.png,wizard-small-1.png,wizard-small-2.png,wizard-small-3.png,wizard-small-4.png
WizardImageStretch=no
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
; Inno Setup's Turkish file spells "için" without the cedilla here.
tr.SelectDirBrowseLabel=İlerlemek için Sonraki üzerine tıklayın. Farklı bir klasör seçmek için Göz at üzerine tıklayın.

[CustomMessages]
en.StartWithWindows=Start OrbMet with Windows
en.StartupGroup=Startup:
tr.StartWithWindows=OrbMet'i Windows açılışında başlat
tr.StartupGroup=Başlangıç:
en.DeleteData=Also delete OrbMet's settings and saved readings (%1)?%n%nChoose No to keep them for a later install.
tr.DeleteData=OrbMet'in ayarları ve kayıtlı ölçümleri (%1) de silinsin mi?%n%nDaha sonra yeniden kuracaksan Hayır'ı seç.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OrbMet"; Filename: "{app}\OrbMet.exe"
Name: "{autodesktop}\OrbMet"; Filename: "{app}\OrbMet.exe"; Tasks: desktopicon

[Registry]
; The same entry the app's own "Start with Windows" item writes (StartupEntry).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OrbMet"; ValueData: """{app}\OrbMet.exe"""; Tasks: startup

[Run]
Filename: "{app}\OrbMet.exe"; Description: "{cm:LaunchProgram,OrbMet}"; Flags: nowait postinstall skipifsilent
; An update from the menu runs setup silently with /relaunch=1 and exits; this starts the new version.
Filename: "{app}\OrbMet.exe"; Flags: nowait; Check: Relaunching

[UninstallRun]
Filename: "{app}\OrbMet.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "OrbMetCleanup"

[Code]
function Relaunching: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\OrbMet');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
