#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="${RUNTIME:-win-x64}"
ALLOW_DIRTY="${ALLOW_DIRTY:-0}"
SKIP_VERIFY="${SKIP_VERIFY:-0}"
BUILD_INSTALLER="${BUILD_INSTALLER:-0}"

PROJECT="$ROOT/src/ClipAsk.Desktop/ClipAsk.Desktop.csproj"
DOTNET="${DOTNET:-$ROOT/.devin/tools/dotnet-win/dotnet.exe}"
CODEX_ROOT="${CLIPASK_CODEX_ROOT:-$ROOT/.devin/tools/codex-win}"
CODEX_MANIFEST="$ROOT/eng/codex-win-x64-0.151.0.sha256"
VERSION="$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$PROJECT" | head -1)"
CODEX_VERSION="$(sed -n 's|.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*|\1|p' "$CODEX_ROOT/codex-package.json" | head -1)"
PUBLISH_DIR="$ROOT/artifacts/publish/ClipAsk-$VERSION-$RUNTIME"
VALIDATION_DIR="$ROOT/artifacts/validation/ClipAsk-$VERSION-$RUNTIME"

fail() {
  printf 'package: %s\n' "$1" >&2
  exit 1
}

[[ -n "$VERSION" ]] || fail "could not read the ClipAsk version"
[[ "$RUNTIME" == "win-x64" ]] || fail "only win-x64 is currently supported"
[[ -x "$DOTNET" ]] || fail "dotnet was not found at $DOTNET (set DOTNET to override)"
[[ -f "$CODEX_ROOT/codex.exe" ]] || fail "the pinned Codex runtime is missing at $CODEX_ROOT"
[[ -f "$CODEX_MANIFEST" ]] || fail "the pinned Codex hash manifest is missing"
[[ "$CODEX_VERSION" == "0.151.0" ]] || fail "expected Codex 0.151.0, found ${CODEX_VERSION:-unknown}"

TRACKED_STATUS="$(git -C "$ROOT" status --porcelain --untracked-files=no)"
if [[ "$ALLOW_DIRTY" != "1" ]] && [[ -n "$TRACKED_STATUS" ]]; then
  fail "tracked files are dirty; commit first or set ALLOW_DIRTY=1 for a local test package"
fi
if [[ -n "$TRACKED_STATUS" ]]; then
  RELEASE_DIR="$ROOT/artifacts/local-releases"
else
  RELEASE_DIR="$ROOT/artifacts/releases"
fi
ARCHIVE="$RELEASE_DIR/ClipAsk-$VERSION-$RUNTIME.zip"
INSTALLER="$RELEASE_DIR/ClipAsk-$VERSION-win-x64-Setup.exe"
SOURCE_ARTIFACT="$RELEASE_DIR/ClipAsk-$VERSION-source.zip"

EXPECTED_CODEX_FILES="$(sed -n 's/^[0-9a-f]\{64\}  //p' "$CODEX_MANIFEST" | sort)"
ACTUAL_CODEX_FILES="$(cd "$CODEX_ROOT" && find . -type f -printf '%P\n' | sort)"
[[ "$ACTUAL_CODEX_FILES" == "$EXPECTED_CODEX_FILES" ]] || fail "the Codex runtime file list differs from the pinned allowlist"
(cd "$CODEX_ROOT" && sha256sum -c "$CODEX_MANIFEST")

if [[ "$SKIP_VERIFY" != "1" ]]; then
  bash "$ROOT/scripts/dev.sh" verify
fi

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR" "$RELEASE_DIR"

export DOTNET_ROOT="${DOTNET_ROOT:-$ROOT/.devin/tools/dotnet-win}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ROOT/.devin/state/dotnet-cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$ROOT/.devin/state/nuget}"
export WSLENV="DOTNET_ROOT/p:DOTNET_CLI_HOME/p:NUGET_PACKAGES/p${WSLENV:+:$WSLENV}"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

"$DOTNET" publish "$(wslpath -w "$PROJECT")" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  --nologo \
  -p:PublishProfile=win-x64 \
  -p:PublishDir="$(wslpath -w "$PUBLISH_DIR")\\"

cp -a "$CODEX_ROOT/." "$PUBLISH_DIR/"
mkdir -p "$PUBLISH_DIR/licenses"
cp "$ROOT/.devin/tools/dotnet-win/LICENSE.txt" "$PUBLISH_DIR/licenses/Microsoft-dotnet-LICENSE.txt"
cp "$ROOT/.devin/tools/dotnet-win/ThirdPartyNotices.txt" "$PUBLISH_DIR/licenses/Microsoft-dotnet-ThirdPartyNotices.txt"
find "$PUBLISH_DIR" -type f -name '*.pdb' -delete

CODEX_OUTPUT="$("$PUBLISH_DIR/codex.exe" --version | tr -d '\r')"
[[ "$CODEX_OUTPUT" == "codex-cli $CODEX_VERSION" ]] || fail "bundled Codex reported '$CODEX_OUTPUT'"

COMMIT="$(git -C "$ROOT" rev-parse HEAD)"
DIRTY_SUFFIX=""
if [[ -n "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]]; then
  DIRTY_SUFFIX=" (working tree contained local changes)"
fi
mkdir -p "$PUBLISH_DIR/source"
git -C "$ROOT" archive --format=zip --prefix="ClipAsk-$VERSION-source/" \
  --output="$PUBLISH_DIR/source/ClipAsk-$VERSION-source.zip" HEAD
printf '%s\n' \
  "ClipAsk $VERSION" \
  "Runtime: $RUNTIME, self-contained .NET" \
  "Source commit: $COMMIT$DIRTY_SUFFIX" \
  "Source: https://github.com/IceyFoxes/ClipAsk/tree/$COMMIT" \
  "Included source archive: source/ClipAsk-$VERSION-source.zip" \
  "Bundled Codex CLI: $CODEX_VERSION" \
  "Codex source: https://github.com/openai/codex/tree/rust-v$CODEX_VERSION" \
  > "$PUBLISH_DIR/BUILD-INFO.txt"

rm -rf "$VALIDATION_DIR"
mkdir -p "$VALIDATION_DIR/ui"
chmod +x "$PUBLISH_DIR/ClipAsk.exe"
env -u CLIPASK_CODEX_PATH "$PUBLISH_DIR/ClipAsk.exe" \
  --smoke-test "$(wslpath -w "$VALIDATION_DIR/ui")"
env -u CLIPASK_CODEX_PATH "$PUBLISH_DIR/ClipAsk.exe" \
  --check-provider "$(wslpath -w "$VALIDATION_DIR/provider-probe.json")"

rm -f "$ARCHIVE" "$ARCHIVE.sha256" "$INSTALLER" "$INSTALLER.sha256" "$SOURCE_ARTIFACT" "$SOURCE_ARTIFACT.sha256"
cp "$PUBLISH_DIR/source/ClipAsk-$VERSION-source.zip" "$SOURCE_ARTIFACT"
(
  cd "$ROOT/artifacts/publish"
  zip -9 -q -r "$ARCHIVE" "ClipAsk-$VERSION-$RUNTIME"
)
(
  cd "$RELEASE_DIR"
  sha256sum "$(basename "$ARCHIVE")" > "$(basename "$ARCHIVE").sha256"
  sha256sum "$(basename "$SOURCE_ARTIFACT")" > "$(basename "$SOURCE_ARTIFACT").sha256"
)

printf 'Created %s\n' "$ARCHIVE"
printf 'SHA-256: %s\n' "$(cut -d ' ' -f 1 "$ARCHIVE.sha256")"
printf 'Created %s\n' "$SOURCE_ARTIFACT"
printf 'SHA-256: %s\n' "$(cut -d ' ' -f 1 "$SOURCE_ARTIFACT.sha256")"

if [[ "$BUILD_INSTALLER" == "1" ]]; then
  ISCC_PATH="${ISCC:-}"
  if [[ -z "$ISCC_PATH" ]] && [[ -x /mnt/c/Windows/System32/cmd.exe ]]; then
    WINDOWS_LOCAL_APPDATA="$(cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /d /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r' | tail -1)"
    if [[ -n "$WINDOWS_LOCAL_APPDATA" ]]; then
      ISCC_CANDIDATE="$(wslpath -u "$WINDOWS_LOCAL_APPDATA")/Programs/Inno Setup 6/ISCC.exe"
      if [[ -x "$ISCC_CANDIDATE" ]]; then
        ISCC_PATH="$ISCC_CANDIDATE"
      fi
    fi
  fi
  [[ -x "$ISCC_PATH" ]] || fail "ISCC.exe was not found; install Inno Setup 6 or set ISCC"
  "$ISCC_PATH" \
    "/DMyAppVersion=$VERSION" \
    "/DMySourceDir=$(wslpath -w "$PUBLISH_DIR")" \
    "/DMyOutputDir=$(wslpath -w "$RELEASE_DIR")" \
    "$(wslpath -w "$ROOT/installer/ClipAsk.iss")"
  (
    cd "$RELEASE_DIR"
    sha256sum "$(basename "$INSTALLER")" > "$(basename "$INSTALLER").sha256"
  )
  printf 'Created %s\n' "$INSTALLER"
  printf 'SHA-256: %s\n' "$(cut -d ' ' -f 1 "$INSTALLER.sha256")"
else
  printf 'Installer source: %s (set BUILD_INSTALLER=1 to compile)\n' "$ROOT/installer/ClipAsk.iss"
fi
