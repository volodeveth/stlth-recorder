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

; Мова, обрана на першому екрані, стає мовою застосунку — і інтерфейсу, і
; розпізнавання мовлення. Передається через реєстр: інсталятор і застосунок — різні
; процеси, і спільної пам'яті між ними немає.
[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ukrainian.AutostartTask=Запускати разом із Windows
ukrainian.AdditionalGroup=Додатково:
ukrainian.LaunchApp=Запустити STLTH Recorder
english.AutostartTask=Start with Windows
english.AdditionalGroup=Additional:
english.LaunchApp=Launch STLTH Recorder

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AdditionalGroup}"

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

; Застосунок забирає це значення при першому запуску і одразу видаляє, щоб повторне
; встановлення не скидало мову, яку людина потім змінила в налаштуваннях.
Root: HKCU; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "SetupLanguage"; \
    ValueData: "{code:SetupLanguageCode}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; \
    Flags: nowait postinstall skipifsilent

[Code]
function SetupLanguageCode(Value: string): string;
begin
  if ActiveLanguage = 'english' then
    Result := 'en'
  else
    Result := 'uk';
end;

[UninstallDelete]
; Налаштування і кеш моделей ідуть разом із застосунком. Записи — НІ: вони належать
; людині, а не інсталятору, і видаляти їх мовчки не можна.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\models"
Type: files; Name: "{localappdata}\{#AppName}\settings.json"
