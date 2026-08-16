# Збирає self-contained застосунок, portable ZIP та інсталятор для поточного користувача.
#
# Self-contained — не оптимізація «щоб напевно», а необхідність: framework-dependent
# збірка зустрічає користувача діалогом «You must install .NET Desktop Runtime» ще до
# того, як він побачить продукт.

param(
    [string]$Version = "0.1.0",
    [switch]$SkipInstaller,
    [switch]$SkipWhisper
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# Агент живе в треї і легко лишається запущеним із попереднього прогону, а тоді
# публікація падає на «файл зайнятий іншим процесом» — помилка, що виглядає як
# поломка збірки, хоча це просто відкритий застосунок.
$running = Get-Process -Name "STLTH Recorder" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "== зупиняю запущений застосунок ==" -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Host "== публікація self-contained ==" -ForegroundColor Cyan
& $dotnet publish .\src\Stlth.App\Stlth.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o .\publish --nologo
if ($LASTEXITCODE -ne 0) { throw "публікація не вдалася" }

Get-ChildItem .\publish -Filter *.pdb | Remove-Item -Force

# Після публікації, а не до неї: `dotnet publish` перезаписує теку, і покладене
# наперед довелося б класти вдруге.
if (-not $SkipWhisper) {
    try {
        & "$root\tools\fetch-whisper.ps1" -Destination "$root\publish\whisper"
    }
    catch {
        # Транскрибація опційна: без неї застосунок повноцінний, і зривати через це
        # збірку релізу немає підстав.
        Write-Host "whisper не додано: $_" -ForegroundColor Yellow
    }
}

Write-Host "== portable ZIP ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path .\installer\Output | Out-Null
$zip = ".\installer\Output\STLTH-Recorder-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path .\publish\* -DestinationPath $zip
Write-Host "  $zip"

if ($SkipInstaller) { return }

$iscc = @(
    # Per-user встановлення — перше в списку: продукт, який ставиться без прав
    # адміністратора, логічно й збирати так само.
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup не знайдено — зібрано лише portable ZIP." -ForegroundColor Yellow
    Write-Host "Встановити: winget install JRSoftware.InnoSetup"
    return
}

Write-Host "== інсталятор ==" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" .\installer\stlth-recorder.iss
if ($LASTEXITCODE -ne 0) { throw "інсталятор не зібрався" }

Get-ChildItem .\installer\Output | ForEach-Object {
    Write-Host ("  {0}  {1:N1} МБ" -f $_.Name, ($_.Length / 1MB))
}
