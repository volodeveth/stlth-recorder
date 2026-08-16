; Інсталятор STLTH Recorder.
;
; PrivilegesRequired=lowest — не примха, а вимога: продукт мусить ставитися і
; працювати без прав адміністратора. Застосунок, який просить пароль адміністратора,
; щоб записувати власні дзвінки, ставити не будуть.

#define AppName "STLTH Recorder"
#define AppExe  "STLTH Recorder.exe"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{9C2F5A31-6B4E-4E7B-9C1D-2F8A4E5B7C10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Volodymyr
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=STLTH-Recorder-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Stlth.App\Resources\idle.ico

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "autostart"; Description: "Запускати разом із Windows"; GroupDescription: "Додатково:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; whisper-cli кладеться поруч, якщо його зібрали: транскрибація опційна, і її
; відсутність не має ламати встановлення.
Source: "..\publish\whisper\*"; DestDir: "{app}\whisper"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Видалити {#AppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустити {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Налаштування і кеш моделей ідуть разом із застосунком. Записи — НІ: вони належать
; людині, а не інсталятору, і видаляти їх мовчки не можна.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\models"
Type: files; Name: "{localappdata}\{#AppName}\settings.json"
