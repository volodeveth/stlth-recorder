namespace Stlth.Core.Localization;

public enum AppLanguage
{
    /// <summary>Типова мова: продукт розрахований на ширшу аудиторію, ніж одна країна.</summary>
    En,

    Uk,
}

/// <summary>
/// Увесь текст інтерфейсу в одному місці.
///
/// Без resx і сателітних збірок навмисно: мов дві, рядків менше сотні, і головна
/// вимога тут — щоб обидва варіанти було видно поруч. Переклад, який лежить в
/// окремому файлі, розходиться з оригіналом тихо; переклад у сусідньому аргументі
/// розійтися не може.
///
/// Мова інтерфейсу задає й мову розпізнавання (<see cref="WhisperCode"/>): людина,
/// яка обрала англійський інтерфейс, найімовірніше говорить у дзвінках англійською.
/// </summary>
public static class Strings
{
    public static AppLanguage Current { get; set; } = AppLanguage.En;

    private static string Pick(string uk, string en) => Current == AppLanguage.Uk ? uk : en;

    /// <summary>Код мови для whisper.</summary>
    public static string WhisperCode => Current == AppLanguage.Uk ? "uk" : "en";

    // --- Трей ---

    public static string StartRecording => Pick("Почати запис", "Start recording");

    public static string StopRecording => Pick("Зупинити запис", "Stop recording");

    public static string Ready => Pick("Готовий до запису", "Ready to record");

    public static string Preparing => Pick("Готуюся…", "Preparing…");

    public static string Stopping => Pick("Зупиняю…", "Stopping…");

    public static string RecentSessions => Pick("Останні записи", "Recent recordings");

    public static string NoSessionsYet => Pick("Записів ще немає", "No recordings yet");

    public static string OpenRecordingsFolder => Pick("Відкрити теку записів", "Open recordings folder");

    public static string ShowInExplorer => Pick("Показати в Провіднику", "Show in Explorer");

    public static string ListenToConversation => Pick("Прослухати розмову", "Listen to the conversation");

    public static string OpenTranscript => Pick("Відкрити транскрипт", "Open transcript");

    public static string Transcribe => Pick("Транскрибувати", "Transcribe");

    public static string TranscribeInProgress => Pick("Розпізнаю…", "Transcribing…");

    public static string EnableTranscription => Pick("Увімкнути транскрибацію…", "Enable transcription…");

    public static string Delete => Pick("Видалити", "Delete");

    public static string SettingsMenu => Pick("Налаштування…", "Settings…");

    public static string About => Pick("Про застосунок", "About");

    public static string Quit => Pick("Вийти", "Quit");

    public static string InterruptedHint =>
        Pick("Сесію перервано аварійно — записане збережено.",
             "This session was cut short — whatever was recorded is kept.");

    public static string RecordingFor(string elapsed) =>
        Pick($"Запис — {elapsed}", $"Recording — {elapsed}");

    public static string TrayRecording(string elapsed) =>
        Pick($"STLTH Recorder — запис {elapsed}", $"STLTH Recorder — recording {elapsed}");

    public static string DeleteConfirm(string label) =>
        Pick($"Видалити запис від {label}? Аудіо зникне назавжди.",
             $"Delete the recording from {label}? The audio is gone for good.");

    public static string MinutesShort(int minutes) => Pick($"{minutes} хв", $"{minutes} min");

    public static string SecondsShort(int seconds) => Pick($"{seconds} с", $"{seconds} s");

    // --- Дозволи ---

    public static string MicGranted => Pick("Мікрофон: доступ є", "Microphone: access granted");

    public static string MicDeniedMenu =>
        Pick("Мікрофон: доступ заборонено — відкрити налаштування",
             "Microphone: access denied — open settings");

    public static string MicMissing => Pick("Мікрофона не знайдено", "No microphone found");

    public static string MicCheck => Pick("Мікрофон: перевірити доступ", "Microphone: check access");

    public static string MicDenied => Pick("Мікрофон: доступ заборонено", "Microphone: access denied");

    public static string MicUnknown => Pick("Мікрофон: стан невідомий", "Microphone: state unknown");

    public static string MicNoDevice => Pick("Мікрофон: пристрою немає", "Microphone: no device");

    // --- Діалог згоди ---

    public static string ConsentTitle => Pick("Почати запис", "Start recording");

    public static string ConsentQuestion =>
        Pick("Співрозмовник знає про запис?", "Does the other side know about the recording?");

    public static string ConsentExplain =>
        Pick("Підтвердження і його час зберігаються в meta.json разом із сесією. Запис іде лише на цей комп'ютер.",
             "The confirmation and its time are stored in meta.json alongside the session. Nothing leaves this computer.");

    public static string ConsentDevice(string device) =>
        Pick($"Співрозмовника буде записано з «{device}» — переконайтеся, що звук дзвінка йде саме туди.",
             $"The other side is captured from “{device}” — make sure the call plays there.");

    public static string ConsentStart => Pick("Так, почати запис", "Yes, start recording");

    public static string Cancel => Pick("Скасувати", "Cancel");

    public static string Close => Pick("Закрити", "Close");

    public static string DiskCritical =>
        Pick("На диску майже не лишилося місця — запис може обірватися.",
             "The disk is nearly full — the recording may be cut short.");

    public static string DiskLow(int minutes) =>
        Pick($"Вільного місця вистачить приблизно на {minutes} хв запису.",
             $"Free space is enough for roughly {minutes} min of recording.");

    // --- Діалог дозволу ---

    public static string PermissionTitle =>
        Pick("Немає доступу до мікрофона", "No microphone access");

    public static string PermissionHeading =>
        Pick("Windows не дає доступу до мікрофона", "Windows is blocking microphone access");

    public static string PermissionBody1 =>
        Pick("Без нього запишеться лише співрозмовник — ваш голос до сесії не потрапить.",
             "Without it only the other side is recorded — your own voice will not be in the session.");

    public static string PermissionBody2 =>
        Pick("Відкрийте «Конфіденційність → Мікрофон» і дозвольте доступ класичним застосункам, потім спробуйте ще раз.",
             "Open Privacy → Microphone, allow access for desktop apps, then try again.");

    public static string PermissionOpen =>
        Pick("Відкрити налаштування", "Open settings");

    public static string PermissionContinue =>
        Pick("Писати без мікрофона", "Record without the microphone");

    // --- Налаштування ---

    public static string SettingsTitle => Pick("Налаштування", "Settings");

    public static string SettingsWindowTitle =>
        Pick("Налаштування — STLTH Recorder", "Settings — STLTH Recorder");

    public static string AutostartLabel =>
        Pick("Запускатися разом із Windows", "Start with Windows");

    public static string AutostartHint =>
        Pick("Застосунок, який треба вмикати руками, не рятує від забутого запису.",
             "An app you have to start by hand does not save you from a forgotten recording.");

    public static string RemindersLabel => Pick("Нагадувати про зустрічі", "Remind me about meetings");

    public static string RemindersHint =>
        Pick("Питає на початку дзвінка і коли запис лишився увімкненим. Ніколи не вмикає і не вимикає його сам.",
             "Asks when a call starts and when a recording is still running. It never starts or stops one by itself.");

    public static string MixdownLabel =>
        Pick("Робити зведений файл для прослуховування", "Build a mixdown to listen back");

    public static string MixdownHint =>
        Pick("session.m4a поруч із доріжками: я ліворуч, співрозмовник праворуч. Вихідні файли лишаються недоторканими.",
             "session.m4a beside the tracks: me on the left, the other side on the right. The source files stay untouched.");

    public static string AutoTranscribeLabel =>
        Pick("Розпізнавати мову після кожного запису", "Transcribe after every recording");

    public static string AutoTranscribeHint =>
        Pick("Працює у фоні й повністю на цьому комп'ютері. Займає приблизно стільки ж часу, скільки тривала розмова.",
             "Runs in the background and entirely on this computer. Takes roughly as long as the conversation itself.");

    public static string AutoTranscribeNeedsModels =>
        Pick("Спершу встановіть моделі — у меню будь-якої сесії.",
             "Install the models first — from any session's menu.");

    public static string DeleteAudioLabel =>
        Pick("Видаляти аудіо після розпізнавання",
             "Delete the audio after transcription");

    public static string DeleteAudioHint =>
        Pick("Вихідні доріжки видаляються назавжди, лишаються транскрипт і зведений файл. Година розмови — це ~700 МБ проти ~43 МБ. Спрацьовує лише тоді, коли в транскрипті є мовлення.",
             "The source tracks are deleted for good; the transcript and the mixdown remain. An hour of talking is ~700 MB against ~43 MB. Only applies when the transcript actually contains speech.");

    public static string DeleteAudioNoMixdown =>
        Pick("Зведений файл вимкнено — після видалення доріжок від сесії лишиться тільки текст.",
             "The mixdown is switched off — once the tracks are gone, only text remains of the session.");

    public static string AudioRemoved(string label) =>
        Pick($"Аудіо сесії {label} видалено після розпізнавання.",
             $"The audio of session {label} was deleted after transcription.");

    public static string LanguageLabel => Pick("Мова", "Language");

    public static string LanguageHint =>
        Pick("Мова інтерфейсу і розпізнавання мовлення.",
             "Language of the interface and of speech recognition.");

    public static string OpenFolder => Pick("Тека записів", "Recordings folder");

    public static string StorageFree(double gigabytes, int hours) =>
        Pick($"Вільно {gigabytes:F1} ГБ — приблизно {hours} год запису.",
             $"{gigabytes:F1} GB free — roughly {hours} h of recording.");

    // --- Транскрибація ---

    public static string TranscriptionSetupTitle =>
        Pick("Увімкнути транскрибацію", "Enable transcription");

    public static string TranscriptionHeading =>
        Pick("Локальна транскрибація", "Local transcription");

    public static string TranscriptionExplain =>
        Pick("Розпізнавання працює повністю на цьому комп'ютері. Аудіо нікуди не надсилається — ні зараз, ні під час розпізнавання. Із мережі завантажуються лише моделі.",
             "Recognition runs entirely on this computer. Audio is never sent anywhere — not now, not during recognition. Only the models are downloaded.");

    public static string TranscriptionSize(long megabytes) =>
        Pick($"Потрібно завантажити ≈ {megabytes} МБ: модель розпізнавання і модель визначення мовлення. Завантаження можна перервати — наступна спроба продовжить із того самого місця.",
             $"About {megabytes} MB to download: the recognition model and the speech-detection model. You can interrupt it — the next attempt resumes where it stopped.");

    public static string TranscriptionInstalled => Pick("Моделі вже встановлені.", "The models are already installed.");

    public static string TranscriptionDownload => Pick("Завантажити моделі", "Download models");

    public static string TranscriptionDownloading => Pick("Завантаження…", "Downloading…");

    public static string TranscriptionProgress(double share) =>
        Pick($"Завантажено {share * 100:F0}%", $"Downloaded {share * 100:F0}%");

    public static string TranscriptionInterrupt => Pick("Перервати", "Stop");

    public static string TranscriptionCancelled =>
        Pick("Перервано. Наступна спроба продовжить із того самого місця.",
             "Stopped. The next attempt resumes from the same place.");

    public static string TranscriptionDone =>
        Pick("Готово. Розпізнавання доступне в меню кожної сесії.",
             "Done. Transcription is available from every session's menu.");

    public static string TranscriptHeader(string session) =>
        Pick($"# Транскрипт сесії {session}", $"# Transcript of session {session}");

    public static string TranscriptNote =>
        Pick("Розпізнано локально, whisper.cpp. Ролі не вгадані моделлю: доріжка мікрофона — це завжди я, доріжка системного звуку — співрозмовник.",
             "Recognised locally with whisper.cpp. The roles are not guessed by a model: the microphone track is always me, the system track is always the other side.");

    public static string TranscriptEmpty => Pick("_Мовлення не розпізнано._", "_No speech recognised._");

    public static string SpeakerMe => Pick("Я", "Me");

    public static string SpeakerPeer => Pick("Співрозмовник", "The other side");

    // --- Сповіщення ---

    public static string RecoveredTitle => Pick("Відновлено після збою", "Recovered after a crash");

    public static string RecoveredOne =>
        Pick("Сесію, перервану аварійно, збережено — аудіо ціле.",
             "A session cut short by a crash was recovered — the audio is intact.");

    public static string RecoveredMany(int count) =>
        Pick($"Сесій, перерваних аварійно: {count}. Аудіо ціле.",
             $"Sessions cut short by a crash: {count}. The audio is intact.");

    public static string StartFailedTitle => Pick("Не вдалося почати запис", "Could not start recording");

    public static string NoPeerAudioTitle =>
        Pick("Запис завершено, але співрозмовника не чути",
             "Recording finished, but the other side is silent");

    public static string NoPeerAudioBody(string device) =>
        Pick($"У системному каналі не було звуку. Перевірте, що звук дзвінка йде на «{device}».",
             $"The system channel carried no audio. Check that the call plays through “{device}”.");

    public static string MeetingStartedTitle(string app) =>
        Pick($"Почалася зустріч у {app}", $"A meeting started in {app}");

    public static string MeetingStartedBody =>
        Pick("Увімкнути запис? Клацніть іконку STLTH Recorder.",
             "Start recording? Click the STLTH Recorder icon.");

    public static string MeetingEndedTitle => Pick("Зустріч завершено", "The meeting is over");

    public static string MeetingEndedBody =>
        Pick("Запис досі триває. Зупинити?", "The recording is still running. Stop it?");

    public static string TranscriptReadyTitle => Pick("Транскрипт готовий", "Transcript ready");

    public static string TranscriptReadyBody =>
        Pick("Відкрити його можна в меню сесії.", "You can open it from the session menu.");

    public static string TranscriptFailedTitle =>
        Pick("Не вдалося розпізнати мову", "Could not transcribe");

    // --- Помилки, які бачить людина ---

    public static string StartFailed(string reason) =>
        Pick($"Не вдалося почати запис: {reason}", $"Could not start recording: {reason}");

    public static string StoppedButMetaFailed(string reason) =>
        Pick($"Запис зупинено, але не вдалося оновити meta.json: {reason}",
             $"Recording stopped, but meta.json could not be updated: {reason}");

    public static string StoppedWithError(string reason) =>
        Pick($"Запис зупинено з помилкою: {reason}", $"Recording stopped with an error: {reason}");

    public static string NoRenderDevice =>
        Pick("У системі немає пристрою відтворення — записувати співрозмовника нізвідки.",
             "There is no playback device — nothing to capture the other side from.");

    public static string UnsupportedSampleRate(string device, int actual, int wanted) =>
        Pick($"Пристрій «{device}» працює на {actual} Гц і не приймає перетворення до {wanted} Гц. Змініть частоту пристрою в налаштуваннях звуку Windows.",
             $"Device “{device}” runs at {actual} Hz and refuses conversion to {wanted} Hz. Change its rate in Windows sound settings.");

    public static string MixMissingTrack(string name) =>
        Pick($"У теці сесії немає {name}", $"The session folder has no {name}");

    public static string MixWriteFailed(string reason) =>
        Pick($"Не вдалося записати зведений файл: {reason}",
             $"Could not write the mixdown: {reason}");

    public static string MixNoSpace(long megabytes) =>
        Pick($"Замало місця для зведеного файлу (потрібно ≈ {megabytes} МБ)",
             $"Not enough space for the mixdown (about {megabytes} MB needed)");

    public static string ModelServerRefused(string name, int status) =>
        Pick($"Не вдалося завантажити {name}: сервер відповів {status}",
             $"Could not download {name}: the server answered {status}");

    public static string ModelIncomplete(string name, long actual, long total) =>
        Pick($"{name} завантажився не повністю ({actual:N0} з {total:N0} Б) — спробуйте ще раз, завантаження продовжиться.",
             $"{name} did not download fully ({actual:N0} of {total:N0} B) — try again, it will resume.");

    public static string WhisperMissing =>
        Pick("Не знайдено whisper-cli.exe поруч із застосунком.",
             "whisper-cli.exe was not found next to the app.");

    public static string ModelsMissing(long megabytes) =>
        Pick($"Моделі не встановлені (потрібно ≈ {megabytes} МБ).",
             $"The models are not installed (about {megabytes} MB needed).");

    public static string WhisperFailed(int code, string details) =>
        Pick($"whisper-cli завершився з кодом {code}: {details}",
             $"whisper-cli exited with code {code}: {details}");

    public static string WhisperUnreadable(string file) =>
        Pick($"whisper-cli не зміг прочитати {file}. Файл пошкоджений або має несподіваний формат.",
             $"whisper-cli could not read {file}. The file is damaged or in an unexpected format.");

    public static string WhisperBlocked =>
        Pick("Windows заблокував whisper-cli.exe політикою Smart App Control. Транскрибація недоступна, доки цей файл не підписаний або політику не змінено. Запис і зведення від цього не залежать.",
             "Windows blocked whisper-cli.exe under Smart App Control. Transcription stays unavailable until the file is signed or the policy changes. Recording and the mixdown do not depend on it.");

    public static string WhisperLaunchFailed(string reason) =>
        Pick($"Не вдалося запустити whisper-cli.exe: {reason}",
             $"Could not launch whisper-cli.exe: {reason}");

    public static string AboutBody =>
        Pick("Запис розмови у два синхронні канали. Нічого не залишає цей комп'ютер.",
             "Records a conversation into two synchronised channels. Nothing leaves this computer.");
}
