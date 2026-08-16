# STLTH Recorder

Records my calls **without a bot in the meeting**: my voice from the microphone, the
other side from the system output, into **two separate synchronised audio files**.

I built this for myself. Recording bots solve the same problem at the price of an
extra participant in the call — one everybody can see, who needs consent at the
platform level, and who ships the audio somewhere else. Capturing locally does not
cost that.

> **Status:** the core and the app work, 169 tests green. Channel drift is measured
> over an hour. The acceptance matrix is partly closed — what is verified and what is
> not is written out honestly below.

## What it is

| | |
|---|---|
| Platform | Windows 10/11 (x64) |
| Language | English or Ukrainian — chosen during setup, changeable in settings |
| Type | Tray agent, starts with Windows — toggleable |
| Capture | WASAPI loopback (system output) + WASAPI capture (microphone) |
| Synchronisation | Shared QPC scale + the timeline invariant |
| Format | WAV / LPCM 16-bit, 48 000 Hz: `mic.wav` (mono), `system.wav` (stereo) |
| Data | Everything stays on this computer; no network requests |
| Privileges | No administrator, no drivers, no services |
| Reminders | Notices when a meeting starts and ends, and asks — but never records on its own |
| Listening back | `session.m4a` — both channels mixed, me on the left, the other side on the right |
| Transcription | Local whisper.cpp, runs automatically after each recording |

## Installing

1. Download `STLTH-Recorder-<version>-setup.exe` from the releases page.
2. Run it. It installs **into your user profile, without administrator rights**.

**About the SmartScreen warning.** The file is not signed with a developer
certificate, so on first launch Windows shows a blue dialog. That is about the missing
signature, not about the contents. Click **More info → Run anyway**. Once is enough.

A signing certificate costs money and adds no safety for someone building this from
the repository themselves. If you would rather not click through the warning, take
`STLTH-Recorder-<version>-portable.zip`: unpack and run, nothing installed.

## Uninstalling

Normally: Settings → Apps, or `unins000.exe` in the install folder.

**If Windows refuses to run the uninstaller**, you have Smart App Control enabled. It
blocks unsigned executables outright — there is no "run anyway" for that policy, and
the uninstaller Inno Setup generates is unsigned like everything else here. Installing
works, uninstalling does not.

The way out ships with the app:

```powershell
& "$env:LOCALAPPDATA\STLTH Recorder\uninstall.ps1"
```

PowerShell scripts are governed by the execution policy, not by the reputation check
that blocks the binary, so this runs where the uninstaller cannot. It removes the
program files, the shortcut and the registry entries.

Your recordings and the ~1 GB of recognition models are **kept** — they belong to you,
not to the installer. Add `-RemoveModels`, `-RemoveRecordings`, or `-WhatIf` to see
what would go without deleting anything.

Note that installing a new version does not need an uninstall first: the installer
upgrades in place.

## Using it

**Click the tray icon** to start recording, click again to stop. Before starting, the
app asks whether the other side knows about the recording; the fact and its timestamp
go into `meta.json` alongside the session.

While recording, the icon turns **red** and the elapsed time shows in the tooltip —
without opening the menu.

The menu holds recent recordings (show in Explorer, listen back, transcribe, delete),
the microphone permission state, and settings.

## Session folder

```
%LOCALAPPDATA%\STLTH Recorder\Sessions\<UUID>\
├── mic.wav        my voice, mono        — source of truth
├── system.wav     the other side, stereo — source of truth
├── session.m4a    mixdown for listening back (me on the left), derived
├── meta.json      metadata, consent, devices, duration
└── transcript.md  optional, local transcription
```

The two `.wav` files can be deleted automatically after transcription — see
[below](#deleting-the-audio-afterwards). That is off by default.

## How synchronisation holds

The microphone and the playback device are two different devices with two different
clocks that slowly drift apart. Stitching two streams by arrival order would
accumulate that drift and then require correcting it.

Instead, the shared reference is **QPC** — the single monotonic clock common to the
whole machine. WASAPI hands out a timestamp with every packet, both streams are
normalised to the moment the session started, and each packet lands in the timeline
**at its own time**, not when it happened to be picked up.

The result has to be stated carefully: drift here is not "absent by construction" but
**bounded by the accuracy of the QPC anchoring, and equal to the measured number**.
That makes measurement a required acceptance step, not a bonus.

**The invariant that holds it together:** in each file the number of samples equals
the session duration times 48000. Always. Silence is written as silence and never cut
out — that single mechanism covers pauses in the conversation, a device change
mid-recording, and the machine going to sleep.

## Meeting reminders

A recorder that relies on your discipline does not solve the problem of the forgotten
recording. So the app watches for meetings itself:

- **a meeting started** → "A meeting started in Zoom — start recording?"
- **the meeting ended while recording continues** → "The recording is still running. Stop it?"

The second matters more than the first. Forgetting to start loses the conversation;
forgetting to stop keeps recording the room afterwards, collecting audio nobody
consented to.

**The decision is always mine.** The app asks — it never starts or stops a recording
by itself.

A meeting is recognised by the one signature no conferencing app can avoid: **something
is holding the microphone open**. Neither browser tabs nor window titles are read. One
mechanism covers Zoom, Meet in Chrome, Teams, Slack and Webex; dictation raises no
reminder.

## Permissions

One permission is needed — the **microphone**. Capturing system audio needs no separate
permission on Windows; that is a property of the platform, not a merit of the app.

The permission state is always visible in the menu. If access is denied, the app
explains it and offers a button straight to the right settings page. You can also
record without a microphone — the session is then honestly marked `system-only` and
`mic.wav` is written as full-length silence: a conversation recorded from one side can
still be listened to, one that was never recorded cannot.

## Transcription

**Runs on its own after every recording** — switchable off in settings. It works in the
background, one session at a time: recognition takes roughly as long as the
conversation itself, per track, and two sessions in a row would otherwise compete for
the machine of somebody who has just hung up.

Manually: `Recent recordings` → session → **Transcribe**. Runs on whisper.cpp entirely
on the device; audio is never uploaded.

Speaker attribution is not guessed by a diarisation model: `mic.wav` is always me,
`system.wav` is always the other side. What is usually a task with its own error rate
is here a property of the recording.

The models (≈1032 MB) install from the menu, with progress and resume. They are
deliberately not bundled: a gigabyte inside a recorder whose main job does not need it
is a gigabyte downloaded by everyone who does not want transcription. Recording works
without them.

**VAD is mandatory, not optional.** Whisper always decodes something on its input
window: give it silence and you get a plausible sentence nobody said. And a meeting is
mostly silence. Verified on a real recording — without VAD, forty seconds of room noise
produced four invented Ukrainian sentences, complete with punctuation.

The recognition language follows the interface language.

### Deleting the audio afterwards

There is an option — **off by default** — to delete the source tracks once a session has
been transcribed. An hour of conversation is roughly 700 MB in the two tracks against
~43 MB for the mixdown, so the saving is real. So is the loss: the originals cannot be
recovered from anything.

It only fires when the transcript actually contains speech. An empty transcript means
recognition found nothing — deleting the audio at that moment would be the worst
possible outcome: the recording gone, and a file saying "no speech recognised" left in
its place.

The mixdown and the transcript are kept. If you have also switched the mixdown off,
the settings window says so plainly: nothing but text will remain. The deletion is
recorded in `meta.json`, so a session without audio never looks damaged — the
difference between "removed on purpose" and "vanished" stays written down.

> On systems with Smart App Control enabled, the unsigned `whisper-cli.exe` may be
> blocked — the app detects that specifically and says so plainly instead of failing
> cryptically. From an installed build it runs normally. **Recording and the mixdown do
> not depend on transcription at all.**

## Acceptance matrix

Every row closes with a number and an artefact, not an opinion. An empty result means
"not tested yet", not "works".

| № | Scenario | Expected | Result |
|---|---|---|---|
| 1 | Channel drift over an hour | < 300 ms | ✅ upper bound **7.4 ms/h** (95% confidence), 56 min run |
| 2 | Track lengths | Identical, sample for sample | ✅ 1 466 220 frames both, difference **0** |
| 3 | The other side's view | No extra participant, no notifications | ⬜ not tried on a live call |
| 4 | Ten minutes of silence | Silence recorded, timeline not shifted | ⬜ covered by tests, not by a live run |
| 5 | Installing without admin rights | Installs into the user profile, app launches | ✅ release installer, marked as downloaded, as a non-administrator — exit code 0 |
| 6 | Reviewing a session | Folder opens, files play, `meta.json` valid | ✅ manual pass |
| 7 | Crash recovery | Session becomes `interrupted` with a real duration, files readable | ✅ `Stop-Process -Force` mid-recording → 25.6 s from the shorter track, header repaired |

## Measured numbers

| What | Value |
|---|---|
| **Channel divergence, 56 min** | **0.4 ms/h (95% CI ±7.0)** against a 300 ms threshold |
| Track lengths, 30 s | 1 466 220 frames both, difference **0 frames** |
| Same on the installed build | 1 109 778 frames both, difference **0 frames** |
| WASAPI packet interval | 10.0 ± 0.15 ms on both streams |
| Accuracy of the drift instrument itself | **0.1 ms/h** on synthetic offsets of 0 / 150 / 400 / −250 |
| Core tests | 169, all green |
| Mixdown of a 24.6 s session | 0.3 MB in 0.4 s (≈98 kbit/s) |

**About the channel divergence — it is not "0.4 ms/h".** The confidence interval
(±7.0) is wider than the value itself, meaning the divergence is **indistinguishable
from zero** on this baseline. The honest phrasing is "upper bound 7.4 ms/h at 95%
confidence", roughly 40× under the threshold. Early samples on a short baseline swung
by ±50 ms/h — and that was not drift, it was the absence of a baseline.

Full reports live in [docs/notes/evidence/](docs/notes/evidence/).

## What is honestly NOT verified

The list is not tidied away at the end. An empty row in the matrix above means "not
tested", not "works".

- **Microphone level on live speech.** The track reliably receives packets, but on most
  runs the room was quiet. That the path is alive is measured; that a voice comes
  through clearly is not.
- **Ukrainian recognition quality.** The pipeline is verified end to end, but an honest
  WER needs a recording of live speech against a known reference text.
- **A real call.** Zoom and Meet were never launched: verification used a synthetic
  tone through the system output.
- **A Bluetooth headset.** The hardest case for synchronisation — its own clock domain
  and reconnects mid-conversation.
- **Device change mid-recording.** The code rebuilds the stream and writes the event
  into `meta.json`, but it has never fired on a real switch.
- **The watchdog.** Covered by tests as a rule, never observed on a real fault.
- **Long sessions.** The longest recording is 30 seconds. Drift was measured with a
  separate instrument, but there has been no hour of continuous recording to files.
- **The SmartScreen dialog.** The installer was verified programmatically, not by a
  double click.

## Development

```powershell
.\build.ps1 -Test          # build the solution and run the tests
.\build.ps1 -Cli           # headless bench
.\build.ps1 -Publish       # self-contained build
.\installer\build-installer.ps1 -Version 1.1.0   # portable ZIP + installer
```

Bench and measurements:

```powershell
dotnet run --project src/Stlth.Cli -- devices      # default devices
dotnet run --project src/Stlth.Cli -- probe 10     # raw packets and their timestamps
dotnet run --project src/Stlth.Cli -- record 30    # record a session and report
dotnet run --project src/Stlth.Cli -- drift 3600   # clock rates, regression
dotnet run --project src/Stlth.Cli -- models       # download the transcription models
dotnet run --project src/Stlth.Cli -- transcribe <dir>
```

```powershell
python tools\selftest_drift.py                     # verify the instrument itself
python tools\gen_clicks.py --out clicks.wav --duration 3600
python tools\drift_check.py <session_dir>          # drift from a click track
python tools\wav_stats.py <session_dir>            # is there signal, or silence
python tools\speech_map.py <file.wav>              # where speech actually is
```

**.NET SDK not on PATH?** The scripts look for it in `%LOCALAPPDATA%\Microsoft\dotnet`.
Install it without administrator rights:

```powershell
& ([scriptblock]::Create((Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1))) -Channel 8.0
```

## Run artefacts

Every number in this file has a run behind it, not an estimate:

- [docs/notes/evidence/p1-gate-capture.md](docs/notes/evidence/p1-gate-capture.md) —
  first capture on real devices, loopback behaviour in silence
- [docs/notes/evidence/drift-60min.md](docs/notes/evidence/drift-60min.md) —
  channel divergence over an hour, with its confidence interval
