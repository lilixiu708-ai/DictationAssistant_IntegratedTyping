#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Prepare the existing checkout for Linux development; do not create a worktree.
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
case "$repository_root" in
  /workspace|/workspace/*) tools_root=/workspace/.tools ;;
  *) tools_root="$(dirname -- "$repository_root")/.tools" ;;
esac

if [[ $# -gt 1 || ( $# -eq 1 && "$1" != --print-env ) ]]; then
  printf 'Usage: bash .codex/setup.sh [--print-env]\n' >&2
  exit 2
fi

command -v python3 >/dev/null || { printf 'Python 3.11+ is required.\n' >&2; exit 1; }
sdk_version="$(python3 - "$repository_root/global.json" <<'PY'
import json
import re
import sys

if sys.version_info < (3, 11):
    raise SystemExit("Python 3.11+ is required.")
with open(sys.argv[1], encoding="utf-8") as stream:
    version = json.load(stream)["sdk"]["version"]
if not isinstance(version, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
    raise SystemExit("global.json must specify a stable .NET SDK version.")
print(version)
PY
)"

export DOTNET_CLI_HOME="$tools_root/dotnet-home"
export NUGET_PACKAGES="$tools_root/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

has_required_sdk() {
  "$1" --list-sdks 2>/dev/null |
    awk -v required="$sdk_version" '$1 == required { found = 1 } END { exit !found }'
}

export DOTNET_ROOT="$tools_root/dotnet"
sdk_available=false
if [[ -x "$DOTNET_ROOT/dotnet" ]] && has_required_sdk "$DOTNET_ROOT/dotnet"; then
  sdk_available=true
elif system_dotnet="$(type -P dotnet)" && has_required_sdk "$system_dotnet"; then
  DOTNET_ROOT="$(dirname -- "$(readlink -f -- "$system_dotnet")")"
  export DOTNET_ROOT
  sdk_available=true
fi

print_activation() {
  # Shell-quote paths before printing executable shell instructions.
  printf 'export DOTNET_ROOT=%q\n' "$DOTNET_ROOT"
  printf 'export PATH=%q:"$PATH"\n' "$DOTNET_ROOT"
  printf 'export DOTNET_CLI_HOME=%q\n' "$DOTNET_CLI_HOME"
  printf 'export NUGET_PACKAGES=%q\n' "$NUGET_PACKAGES"
  printf 'export DOTNET_CLI_TELEMETRY_OPTOUT=1\n'
}

if [[ ${1:-} == --print-env ]]; then
  print_activation
  exit 0
fi

mkdir -p "$tools_root" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
if [[ "$sdk_available" != true ]]; then
  command -v curl >/dev/null || { printf 'curl is required to install .NET.\n' >&2; exit 1; }
  installer_path="$(mktemp "${TMPDIR:-/tmp}/dictation-dotnet-install.XXXXXX.sh")"
  trap 'rm -f -- "$installer_path"' EXIT
  curl --proto '=https' --tlsv1.2 -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer_path"
  bash "$installer_path" --version "$sdk_version" --install-dir "$DOTNET_ROOT" --no-path
fi
export PATH="$DOTNET_ROOT:$PATH"

cd -- "$repository_root"
printf 'Repository: %s\nSDK required by global.json: %s\n' "$repository_root" "$sdk_version"
dotnet --version

# Keep Linux dependency resolution away from the committed Windows RID lock.
dotnet restore DictationAssistant.slnx -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet build DictationAssistant.slnx -c Release --no-restore -m:2

application_output="$repository_root/src/DictationAssistant.App/bin/Release/net10.0"
dotnet publish plugins/DictationAssistant.Importers.Docx/DictationAssistant.Importers.Docx.csproj \
  -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false \
  -o "$application_output/Plugins/DocxVocabulary"
dotnet publish plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj \
  -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false \
  -o "$application_output/Plugins/SynchronousTyping"

dotnet run --project tests/TypingPlugin.SmokeTests/TypingPlugin.SmokeTests.csproj \
  -c Release --no-build --no-restore

report_directory="$repository_root/artifacts/cloud-validation"
mkdir -p "$report_directory"
dotnet "$application_output/DictationAssistant.App.dll" --list-plugins > "$report_directory/plugins.json"
cat "$report_directory/plugins.json"
python3 - "$report_directory/plugins.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8-sig") as stream:
    report = json.load(stream)
required = {"docx-vocabulary", "synchronous-typing"}
loaded = {
    item["Manifest"]["Id"]
    for item in report["Plugins"]
    if item.get("Manifest") and item.get("IsLoaded") and not item.get("Error")
}
missing = required - loaded
if missing:
    raise SystemExit("Required plugin(s) failed to load: " + ", ".join(sorted(missing)))
print("Required plugins loaded: " + ", ".join(sorted(required)))
PY

printf '\nLinux build, existing typing checks and required plugin loading completed.\n'
printf 'Activate tools in a new shell with: eval "$(bash .codex/setup.sh --print-env)"\n'
