#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
DOTNET="$ROOT/.devin/tools/dotnet-win/dotnet.exe"
export DOTNET_ROOT="$ROOT/.devin/tools/dotnet-win"
export DOTNET_CLI_HOME="$ROOT/.devin/state/dotnet-cli"
export NUGET_PACKAGES="$ROOT/.devin/state/nuget"
export CLIPASK_CODEX_PATH="$ROOT/.devin/tools/codex-win/codex.exe"
export WSLENV="DOTNET_ROOT/p:DOTNET_CLI_HOME/p:NUGET_PACKAGES/p:CLIPASK_CODEX_PATH/p${WSLENV:+:$WSLENV}"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$ROOT/.devin/evidence/ui" "$ROOT/.devin/evidence/test-results"
case "${1:-}" in
  run)
    shift
    exec "$DOTNET" run -c Release --project "$(wslpath -w "$ROOT/src/ClipAsk.Desktop/ClipAsk.Desktop.csproj")" -- "$@"
    ;;
  verify)
    "$DOTNET" build "$(wslpath -w "$ROOT/ClipAsk.slnx")" -c Release --nologo
    "$DOTNET" test "$(wslpath -w "$ROOT/tests/ClipAsk.Core.Tests/ClipAsk.Core.Tests.csproj")" -c Release --no-build --logger trx --results-directory "$(wslpath -w "$ROOT/.devin/evidence/test-results")"
    "$DOTNET" "$(wslpath -w "$ROOT/src/ClipAsk.Desktop/bin/Release/net10.0-windows/ClipAsk.dll")" --smoke-test "$(wslpath -w "$ROOT/.devin/evidence/ui")"
    "$DOTNET" "$(wslpath -w "$ROOT/src/ClipAsk.Desktop/bin/Release/net10.0-windows/ClipAsk.dll")" --check-provider "$(wslpath -w "$ROOT/.devin/evidence/provider-probe.json")"
    ;;
  *)
    printf 'usage: bash scripts/dev.sh {run|verify} [args...]\n' >&2
    exit 2
    ;;
esac
