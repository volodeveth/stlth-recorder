# Кладе whisper-cli.exe поруч із застосунком.
#
# Це виконуваний файл, а не модель: 8 МБ проти 548 МБ. Тому він іде у складі збірки,
# а моделі довантажуються з меню на вимогу — рекордер, чия основна робота розпізнавання
# не потребує, не мусить тягнути півгігабайта кожному.
#
# Береться офіційна CPU-збірка whisper.cpp: без CUDA, без BLAS. Транскрибація тут —
# пакетна робота після розмови, її ніхто не чекає в реальному часі, а збірка під
# конкретне залізо перетворила б «розпакував і працює» на «а яка в тебе відеокарта».

param(
    [string]$Tag = "latest",
    [string]$Destination = "$PSScriptRoot\..\publish\whisper"
)

$ErrorActionPreference = 'Stop'

$api = if ($Tag -eq "latest") {
    "https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest"
} else {
    "https://api.github.com/repos/ggml-org/whisper.cpp/releases/tags/$Tag"
}

Write-Host "== шукаю збірку whisper.cpp ==" -ForegroundColor Cyan
$release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "stlth-recorder" }
$asset = $release.assets | Where-Object { $_.name -eq "whisper-bin-x64.zip" } | Select-Object -First 1

if (-not $asset) {
    throw "У релізі $($release.tag_name) немає whisper-bin-x64.zip"
}

Write-Host "  $($release.tag_name): $($asset.name), $([math]::Round($asset.size/1MB,1)) МБ"

$temp = Join-Path ([System.IO.Path]::GetTempPath()) "whisper-bin-x64.zip"
$unpacked = Join-Path ([System.IO.Path]::GetTempPath()) "whisper-unpacked"

Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp -UseBasicParsing

if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
Expand-Archive -Path $temp -DestinationPath $unpacked -Force

$cli = Get-ChildItem $unpacked -Recurse -Filter "whisper-cli.exe" | Select-Object -First 1
if (-not $cli) {
    throw "У архіві немає whisper-cli.exe — розкладка релізу змінилася"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# Береться whisper-cli.exe і рівно ті бібліотеки, без яких він не запуститься.
# Решта архіву — демо, тести і споріднені моделі; білий список замість «усе, крім»,
# бо реліз рано чи пізно принесе ще щось, чого ми не чекали.
$wanted = { $_.Name -eq 'whisper-cli.exe' -or $_.Name -eq 'whisper.dll' -or $_.Name -like 'ggml*.dll' }

Get-ChildItem $cli.DirectoryName -File |
    Where-Object $wanted |
    ForEach-Object { Copy-Item $_.FullName -Destination $Destination -Force }

Remove-Item $temp -Force
Remove-Item $unpacked -Recurse -Force

Write-Host "== покладено ==" -ForegroundColor Cyan
Get-ChildItem $Destination | ForEach-Object {
    Write-Host ("  {0}  {1:N1} МБ" -f $_.Name, ($_.Length / 1MB))
}
