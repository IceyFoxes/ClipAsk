#!/usr/bin/env bash
set -euo pipefail

CLIPASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
DOTNET="$CLIPASK_ROOT/.devin/tools/dotnet-win/dotnet.exe"
STORE_TOOL="$CLIPASK_ROOT/tools/ClipAsk.StorePrep/ClipAsk.StorePrep.csproj"
APP_PROJECT="$CLIPASK_ROOT/src/ClipAsk.Desktop/ClipAsk.Desktop.csproj"

: "${CLIPASK_STORE_IDENTITY_NAME:?Set CLIPASK_STORE_IDENTITY_NAME to the exact Partner Center Package/Identity/Name value}"
: "${CLIPASK_STORE_PUBLISHER:?Set CLIPASK_STORE_PUBLISHER to the exact Partner Center Package/Identity/Publisher value}"
CLIPASK_STORE_PUBLISHER_DISPLAY_NAME="${CLIPASK_STORE_PUBLISHER_DISPLAY_NAME:-IceyFoxes}"
CLIPASK_STORE_PACKAGE_VERSION="${CLIPASK_STORE_PACKAGE_VERSION:-1.0.0.0}"

[[ -x "$DOTNET" ]] || { printf 'package-store: project-local Windows dotnet was not found\n' >&2; exit 1; }
[[ "$CLIPASK_STORE_IDENTITY_NAME" =~ ^[A-Za-z0-9.-]+$ ]] || { printf 'package-store: invalid Partner Center identity name\n' >&2; exit 1; }
[[ "$CLIPASK_STORE_PACKAGE_VERSION" =~ ^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$ ]] || {
  printf 'package-store: package version must be four numeric components, start at 1, and end in .0\n' >&2
  exit 1
}
IFS='.' read -r -a version_components <<< "$CLIPASK_STORE_PACKAGE_VERSION"
for component in "${version_components[@]}"; do
  if (( 10#$component > 65535 )); then
    printf 'package-store: each package version component must be between 0 and 65535\n' >&2
    exit 1
  fi
done

tracked_status="$(git -C "$CLIPASK_ROOT" status --porcelain --untracked-files=no)"
if [[ "${ALLOW_DIRTY:-0}" != "1" && -n "$tracked_status" ]]; then
  printf 'package-store: tracked files are dirty; commit first or set ALLOW_DIRTY=1\n' >&2
  exit 1
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$CLIPASK_ROOT/.devin/tools/dotnet-win}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$CLIPASK_ROOT/.devin/state/dotnet-cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$CLIPASK_ROOT/.devin/state/nuget}"
export WSLENV="DOTNET_ROOT/p:DOTNET_CLI_HOME/p:NUGET_PACKAGES/p${WSLENV:+:$WSLENV}"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

"$DOTNET" run -c Release --project "$(wslpath -w "$STORE_TOOL")" -- \
  assets \
  "$(wslpath -w "$CLIPASK_ROOT/assets/brand/clipask-icon.png")" \
  "$(wslpath -w "$CLIPASK_ROOT/store/assets")"

ALLOW_DIRTY="${ALLOW_DIRTY:-0}" SKIP_VERIFY="${SKIP_VERIFY:-0}" bash "$CLIPASK_ROOT/scripts/package.sh"

app_version="$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$APP_PROJECT" | head -1)"
publish_dir="$CLIPASK_ROOT/artifacts/publish/ClipAsk-$app_version-win-x64"
store_root="$CLIPASK_ROOT/artifacts/store/ClipAsk-$CLIPASK_STORE_PACKAGE_VERSION-win-x64"
layout_dir="$store_root/layout"
if [[ -n "$tracked_status" ]]; then
  release_dir="$CLIPASK_ROOT/artifacts/local-store-releases"
else
  release_dir="$CLIPASK_ROOT/artifacts/store-releases"
fi
package_path="$release_dir/ClipAsk-$CLIPASK_STORE_PACKAGE_VERSION-win-x64.msix"

rm -rf "$store_root"
mkdir -p "$layout_dir" "$release_dir"
cp -a "$publish_dir/." "$layout_dir/"
cp -a "$CLIPASK_ROOT/store/assets" "$layout_dir/Assets"

"$DOTNET" run -c Release --project "$(wslpath -w "$STORE_TOOL")" -- \
  manifest \
  "$(wslpath -w "$CLIPASK_ROOT/store/AppxManifest.xml.in")" \
  "$(wslpath -w "$layout_dir/AppxManifest.xml")" \
  "$CLIPASK_STORE_IDENTITY_NAME" \
  "$CLIPASK_STORE_PUBLISHER" \
  "$CLIPASK_STORE_PUBLISHER_DISPLAY_NAME" \
  "$CLIPASK_STORE_PACKAGE_VERSION"

makeappx_path="${MAKEAPPX:-}"
windows_sdk_bin='/mnt/c/Program Files (x86)/Windows Kits/10/bin'
if [[ -z "$makeappx_path" && -d "$windows_sdk_bin" ]]; then
  makeappx_path="$(find "$windows_sdk_bin" -type f -ipath '*/x64/makeappx.exe' -print | sort -V | tail -1)"
fi
if [[ -z "$makeappx_path" || ! -x "$makeappx_path" ]]; then
  printf 'package-store: MSIX layout prepared at %s\n' "$layout_dir" >&2
  printf 'package-store: MakeAppx.exe was not found; install the Windows 10/11 SDK or set MAKEAPPX\n' >&2
  exit 1
fi

# Target-size and unplated icon variants are selected through the package
# resource index. MakeAppx does not build that index for a manual MSIX layout.
makepri_path="${MAKEPRI:-$(dirname "$makeappx_path")/makepri.exe}"
if [[ ! -x "$makepri_path" ]]; then
  printf 'package-store: MakePri.exe was not found beside MakeAppx.exe; set MAKEPRI\n' >&2
  exit 1
fi
# MakePri can silently skip files while enumerating a \\wsl.localhost share
# (it dropped targetsize-30_altform-unplated, which 125% displays use), so index
# a copy of the manifest and assets on a Windows drive.
windows_temp="$(cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /d /c 'echo %TEMP%' 2>/dev/null | tr -d '\r' | tail -1)"
pri_root="$(mktemp -d "$(wslpath -u "$windows_temp")/clipask-pri.XXXXXX")"
trap 'rm -rf "$pri_root"' EXIT
mkdir -p "$pri_root/project"
cp -a "$layout_dir/AppxManifest.xml" "$layout_dir/Assets" "$pri_root/project/"
"$makepri_path" createconfig /cf "$(wslpath -w "$pri_root/priconfig.xml")" /dq en-US /o >/dev/null
# A single MSIX has no resource packages, so keep every scale in resources.pri.
sed -i '/<packaging>/,/<\/packaging>/d' "$pri_root/priconfig.xml"
"$makepri_path" new /pr "$(wslpath -w "$pri_root/project")" /cf "$(wslpath -w "$pri_root/priconfig.xml")" \
  /of "$(wslpath -w "$pri_root/resources.pri")" /o >/dev/null
"$makepri_path" dump /if "$(wslpath -w "$pri_root/resources.pri")" \
  /of "$(wslpath -w "$pri_root/resources.xml")" /o /dt detailed >/dev/null
for asset in "$layout_dir"/Assets/*.png; do
  grep -qF "<Value>Assets\\$(basename "$asset")</Value>" "$pri_root/resources.xml" || {
    printf 'package-store: resources.pri is missing Assets/%s\n' "$(basename "$asset")" >&2
    exit 1
  }
done
cp "$pri_root/resources.pri" "$layout_dir/resources.pri"

rm -f "$package_path" "$package_path.sha256"
makeappx_log="$store_root/makeappx.log"
if ! "$makeappx_path" pack /d "$(wslpath -w "$layout_dir")" /p "$(wslpath -w "$package_path")" /o >"$makeappx_log" 2>&1; then
  cat "$makeappx_log" >&2
  exit 1
fi
tail -n 1 "$makeappx_log"
(
  cd "$release_dir"
  sha256sum "$(basename "$package_path")" > "$(basename "$package_path").sha256"
)

printf 'Created %s\n' "$package_path"
printf 'SHA-256: %s\n' "$(cut -d ' ' -f 1 "$package_path.sha256")"
if [[ "$CLIPASK_STORE_PUBLISHER" == *'OID.2.25.311729368913984317654407730594956997722=1'* ]]; then
  printf 'Unsigned Windows 11 local test package: install from elevated PowerShell with Add-AppxPackage -AllowUnsigned.\n'
else
  printf 'This MSIX is unsigned for Store submission and is not intended for direct sideloading.\n'
fi
