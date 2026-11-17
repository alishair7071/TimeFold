; ============================================================================
; TimeFold installer script (Inno Setup 6)
;
; Build it with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\TimeFold.iss
;
; Before building, publish the app first:
;   dotnet publish src\TimeFold.App\TimeFold.App.csproj -c Release -r win-x64 ^
;       --self-contained true -o publish
;
; Key choices, and why:
;   PrivilegesRequired=lowest  -> installs for this user only, no admin prompt.
;   DefaultDirName in localappdata -> no admin rights needed to write there.
;   No Windows startup entry is created, by the installer or by the app.
;   TimeFold runs only when you open it from the desktop shortcut.
; ============================================================================

#define AppName        "TimeFold"
#define AppVersion     "0.1.0"
#define AppPublisher   "TimeFold"
#define AppExeName     "TimeFold.exe"

[Setup]
AppId={{8E2C4F10-3D7A-4C21-9B5E-6A1F0C93D7B2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\{#AppName}\app
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=TimeFold-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\TimeFold.App\Assets\timefold.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Everything produced by "dotnet publish".
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
; Ticked by default: the desktop shortcut is how you are meant to open TimeFold.
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

; No [Run] section on purpose.
; TimeFold does not launch itself after installing, and does not add itself to
; Windows startup. You open it yourself, from the desktop shortcut.

[UninstallDelete]
; Removes only the installed program files.
; Your recorded history in {localappdata}\TimeFold is deliberately NOT deleted
; here - the uninstall step below asks you about it first.
Type: filesandordirs; Name: "{app}"

[Code]
// ---------------------------------------------------------------------------
// On uninstall, ask whether to delete the recorded history too.
// Deleting someone's data without asking would be the wrong default.
// ---------------------------------------------------------------------------
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataFolder := ExpandConstant('{localappdata}\TimeFold');

    if DirExists(DataFolder) then
    begin
      if MsgBox('Do you also want to delete your recorded TimeFold history?' + #13#10 + #13#10 +
                DataFolder + #13#10 + #13#10 +
                'Choose No to keep it, for example if you plan to reinstall.',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataFolder, True, True, True);
      end;
    end;
  end;
end;
