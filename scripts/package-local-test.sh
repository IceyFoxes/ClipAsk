#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
VERSION="${CLIPASK_STORE_PACKAGE_VERSION:-1.0.9.0}"
SDK_BIN='/mnt/c/Program Files (x86)/Windows Kits/10/bin'
SIGNTOOL_PATH="${SIGNTOOL:-}"
if [[ -z "$SIGNTOOL_PATH" && -d "$SDK_BIN" ]]; then
  SIGNTOOL_PATH="$(find "$SDK_BIN" -type f -ipath '*/x64/signtool.exe' -print | sort -V | tail -1)"
fi
[[ -x "$SIGNTOOL_PATH" ]] || { printf 'package-local-test: SignTool.exe was not found\n' >&2; exit 1; }
command -v openssl >/dev/null || { printf 'package-local-test: OpenSSL was not found\n' >&2; exit 1; }

CLIPASK_STORE_IDENTITY_NAME='ClipAsk.LocalTaskbarTest' \
CLIPASK_STORE_PUBLISHER='CN=ClipAskLocalTest' \
CLIPASK_STORE_PUBLISHER_DISPLAY_NAME='ClipAsk Local Test' \
CLIPASK_STORE_PACKAGE_VERSION="$VERSION" \
ALLOW_DIRTY=1 bash "$ROOT/scripts/package-store.sh"

unsigned="$ROOT/artifacts/local-store-releases/ClipAsk-$VERSION-win-x64.msix"
if [[ ! -f "$unsigned" ]]; then
  unsigned="$ROOT/artifacts/store-releases/ClipAsk-$VERSION-win-x64.msix"
fi
[[ -f "$unsigned" ]] || { printf 'package-local-test: unsigned MSIX was not created\n' >&2; exit 1; }
output_dir="$(dirname "$unsigned")"
signed="$output_dir/ClipAsk-$VERSION-win-x64-signed-local.msix"
certificate="$output_dir/ClipAsk-LocalTaskbarTest.cer"
private_dir="$(mktemp -d)"
trap 'rm -rf "$private_dir"' EXIT
password="$(openssl rand -hex 24)"

openssl req -x509 -newkey rsa:3072 -noenc -sha256 -days 30 \
  -keyout "$private_dir/test.key" -out "$private_dir/test.pem" \
  -subj '/CN=ClipAskLocalTest' \
  -addext 'keyUsage=critical,digitalSignature' \
  -addext 'extendedKeyUsage=codeSigning' \
  -addext 'basicConstraints=critical,CA:FALSE' >/dev/null 2>&1
openssl pkcs12 -export -inkey "$private_dir/test.key" -in "$private_dir/test.pem" \
  -out "$private_dir/test.pfx" -passout "pass:$password"
openssl x509 -in "$private_dir/test.pem" -outform DER -out "$certificate"

cp "$unsigned" "$signed"
"$SIGNTOOL_PATH" sign /fd SHA256 /f "$(wslpath -w "$private_dir/test.pfx")" \
  /p "$password" "$(wslpath -w "$signed")"
rm -f "$unsigned" "$unsigned.sha256"
printf 'Signed local test MSIX: %s\n' "$signed"
printf 'Public test certificate: %s\n' "$certificate"
sha256sum "$signed"
