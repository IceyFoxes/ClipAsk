$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.devin\tools\dotnet-win\dotnet.exe'
$env:DOTNET_ROOT = Join-Path $root '.devin\tools\dotnet-win'
$env:DOTNET_CLI_HOME = Join-Path $root '.devin\state\dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $root '.devin\state\nuget'
$env:SCREENSHOT_CODEX_PATH = Join-Path $root '.devin\tools\codex-win\codex.exe'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null
& $dotnet run -c Release --project (Join-Path $root 'src\Screenshot.Desktop\Screenshot.Desktop.csproj') @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
