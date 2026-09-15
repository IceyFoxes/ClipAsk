$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.devin\tools\dotnet-win\dotnet.exe'
$env:DOTNET_ROOT = Join-Path $root '.devin\tools\dotnet-win'
$env:DOTNET_CLI_HOME = Join-Path $root '.devin\state\dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $root '.devin\state\nuget'
$env:SCREENSHOT_CODEX_PATH = Join-Path $root '.devin\tools\codex-win\codex.exe'
$evidence = Join-Path $root '.devin\evidence'
$ui = Join-Path $evidence 'ui'
$probe = Join-Path $evidence 'provider-probe.json'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $evidence, $ui | Out-Null
& $dotnet build (Join-Path $root 'Screenshot.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet test (Join-Path $root 'tests\Screenshot.Core.Tests\Screenshot.Core.Tests.csproj') -c Release --no-build --logger trx --results-directory (Join-Path $evidence 'test-results')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $root 'src\Screenshot.Desktop\bin\Release\net10.0-windows\Screenshot.dll'
& $dotnet $dll --smoke-test $ui
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet $dll --check-provider $probe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
