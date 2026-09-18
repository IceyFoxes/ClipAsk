param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$AllowDirty,
    [switch]$SkipVerify,
    [switch]$BuildInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ClipAsk.Desktop\ClipAsk.Desktop.csproj'
$dotnet = if ($env:DOTNET) { $env:DOTNET } else { Join-Path $root '.devin\tools\dotnet-win\dotnet.exe' }
$codexRoot = if ($env:CLIPASK_CODEX_ROOT) { $env:CLIPASK_CODEX_ROOT } else { Join-Path $root '.devin\tools\codex-win' }

[xml]$projectXml = Get-Content -Raw $project
$version = [string]$projectXml.Project.PropertyGroup.Version
$codexPackage = Get-Content -Raw (Join-Path $codexRoot 'codex-package.json') | ConvertFrom-Json
if ($Runtime -ne 'win-x64') { throw 'Only win-x64 is currently supported.' }
if ($codexPackage.version -ne '0.151.0') { throw "Expected Codex 0.151.0, found $($codexPackage.version)." }
if (-not (Test-Path $dotnet)) { throw "dotnet was not found at $dotnet. Set DOTNET to override." }
if (-not (Test-Path (Join-Path $codexRoot 'codex.exe'))) { throw "The pinned Codex runtime is missing at $codexRoot." }

$trackedStatus = (& git -C $root status --porcelain --untracked-files=no) -join "`n"
if (-not $AllowDirty -and $trackedStatus) { throw 'Tracked files are dirty. Commit first or pass -AllowDirty for a local test package.' }

if (-not $SkipVerify) {
    & (Join-Path $PSScriptRoot 'verify.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$publishDir = Join-Path $root "artifacts\publish\ClipAsk-$version-$Runtime"
$releaseDir = Join-Path $root 'artifacts\releases'
$archive = Join-Path $releaseDir "ClipAsk-$version-$Runtime.zip"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir, $releaseDir | Out-Null

$env:DOTNET_ROOT = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path $root '.devin\tools\dotnet-win' }
$env:DOTNET_CLI_HOME = if ($env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME } else { Join-Path $root '.devin\state\dotnet-cli' }
$env:NUGET_PACKAGES = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $root '.devin\state\nuget' }
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null

& $dotnet publish $project -c $Configuration -r $Runtime --self-contained true --nologo '-p:PublishProfile=win-x64' "-p:PublishDir=$publishDir\"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -Recurse -Force (Join-Path $codexRoot '*') $publishDir
$licenseDir = Join-Path $publishDir 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
Copy-Item (Join-Path $root '.devin\tools\dotnet-win\LICENSE.txt') (Join-Path $licenseDir 'Microsoft-dotnet-LICENSE.txt')
Copy-Item (Join-Path $root '.devin\tools\dotnet-win\ThirdPartyNotices.txt') (Join-Path $licenseDir 'Microsoft-dotnet-ThirdPartyNotices.txt')

$codexOutput = (& (Join-Path $publishDir 'codex.exe') --version).Trim()
if ($codexOutput -ne "codex-cli $($codexPackage.version)") { throw "Bundled Codex reported '$codexOutput'." }
$commit = (& git -C $root rev-parse HEAD).Trim()
$dirty = if ((& git -C $root status --porcelain --untracked-files=no) -join '') { ' (working tree contained local changes)' } else { '' }
@"
ClipAsk $version
Runtime: $Runtime, self-contained .NET
Source commit: $commit$dirty
Source: https://github.com/IceyFoxes/ClipAsk/tree/$commit
Bundled Codex CLI: $($codexPackage.version)
Codex source: https://github.com/openai/codex/tree/rust-v$($codexPackage.version)
"@ | Set-Content -Encoding utf8NoBOM (Join-Path $publishDir 'BUILD-INFO.txt')

if (Test-Path $archive) { Remove-Item -Force $archive }
Compress-Archive -Path $publishDir -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archive)" | Set-Content -Encoding ascii "$archive.sha256"

if ($BuildInstaller) {
    $iscc = $env:ISCC
    if (-not $iscc) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $iscc = $command.Source }
    }
    if (-not $iscc) {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'ISCC.exe was not found. Install Inno Setup 6 or set ISCC.' }
    & $iscc "/DMyAppVersion=$version" "/DMySourceDir=$publishDir" "/DMyOutputDir=$releaseDir" (Join-Path $root 'installer\ClipAsk.iss')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $installer = Join-Path $releaseDir "ClipAsk-$version-win-x64-Setup.exe"
    $installerHash = (Get-FileHash -Algorithm SHA256 $installer).Hash.ToLowerInvariant()
    "$installerHash  $(Split-Path -Leaf $installer)" | Set-Content -Encoding ascii "$installer.sha256"
}

Write-Host "Created $archive"
Write-Host "SHA-256: $hash"
