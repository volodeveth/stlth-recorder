param(
    [switch]$Test,
    [switch]$Run,
    [switch]$Publish,
    [switch]$Cli
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    throw ".NET SDK не знайдено: $dotnet. Встановити: https://dot.net/v1/dotnet-install.ps1 -Channel 8.0"
}

$sln = '.\STLTH Recorder.sln'

if ($Test) {
    # Спершу зібрати ВСЕ рішення: `dotnet test` будує лише тестовий проєкт і його
    # залежності, а застосунок серед них не значиться — і його помилки компіляції
    # тихо не потрапляють у «зелено».
    & $dotnet build $sln --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet test $sln --nologo --no-build
    exit $LASTEXITCODE
}

if ($Run) {
    & $dotnet run --project .\src\Stlth.App\Stlth.App.csproj
    exit $LASTEXITCODE
}

if ($Cli) {
    & $dotnet build .\src\Stlth.Cli\Stlth.Cli.csproj --nologo
    exit $LASTEXITCODE
}

if ($Publish) {
    & $dotnet publish .\src\Stlth.App\Stlth.App.csproj -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o .\publish
    exit $LASTEXITCODE
}

& $dotnet build $sln --nologo
exit $LASTEXITCODE
