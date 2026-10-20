#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Build one native, self-contained macOS app with both current plugins.
set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$TASK_ROOT"
RID=""
NATIVE_REVIEW=""
NATIVE_REVIEW_COMMIT=""
while (($#)); do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --native-review-archive) NATIVE_REVIEW="$2"; shift 2 ;;
    --native-review-build-commit) NATIVE_REVIEW_COMMIT="$2"; shift 2 ;;
    *) echo "Usage: bash build-macos-typing.sh --rid osx-x64|osx-arm64 [--native-review-archive ZIP --native-review-build-commit FULL40SHA]" >&2; exit 2 ;;
  esac
done
if [[ -n "$NATIVE_REVIEW" || -n "$NATIVE_REVIEW_COMMIT" ]]; then
  [[ -n "$NATIVE_REVIEW" && "$NATIVE_REVIEW_COMMIT" =~ ^[0-9a-f]{40}$ ]] || {
    echo "A native review ZIP must be paired with its exact producer commit (40 lowercase hexadecimal characters)." >&2
    exit 2
  }
fi
[[ "$(uname -s)" == Darwin ]] || { echo "This build requires a real macOS runner." >&2; exit 1; }
case "$RID:$(uname -m)" in
  osx-x64:x86_64|osx-arm64:arm64) ;;
  *) echo "Build on the matching Intel or Apple Silicon runner; do not substitute a cross-build check." >&2; exit 1 ;;
esac
python3 -c 'import sys; assert sys.version_info >= (3, 11)'
dotnet --version

OUTPUT="$TASK_ROOT/artifacts/macos/$RID"
PUBLISH="$OUTPUT/payload/DictationAssistant.app/Contents/MacOS"
rm -rf "$OUTPUT"
mkdir -p "$PUBLISH"
dotnet restore src/DictationAssistant.App/DictationAssistant.App.csproj -r "$RID" \
  --locked-mode -p:RuntimeIdentifier="$RID" -p:RuntimeFrameworkVersion=10.0.0 -p:SelfContained=true \
  -p:RestorePackagesWithLockFile=true -p:NuGetLockFilePath="packages.$RID.lock.json"
dotnet publish src/DictationAssistant.App/DictationAssistant.App.csproj -c Release -r "$RID" \
  --self-contained true --no-restore -p:RuntimeFrameworkVersion=10.0.0 -p:PublishTrimmed=false \
  -p:DebugType=None -p:DebugSymbols=false -o "$PUBLISH"

for PLUGIN in DictationAssistant.Importers.Docx DictationAssistant.Practices.Typing; do
  FOLDER=DocxVocabulary
  [[ "$PLUGIN" != DictationAssistant.Practices.Typing ]] || FOLDER=SynchronousTyping
  dotnet restore "plugins/$PLUGIN/$PLUGIN.csproj" -r "$RID" \
    --locked-mode -p:RuntimeIdentifier="$RID" -p:SelfContained=false -p:RestorePackagesWithLockFile=true \
    -p:NuGetLockFilePath="packages.$RID.lock.json"
  dotnet publish "plugins/$PLUGIN/$PLUGIN.csproj" -c Release -r "$RID" --self-contained false \
    --no-restore -p:DebugType=None -p:DebugSymbols=false -o "$PUBLISH/Plugins/$FOLDER"
done
if [[ -n "$NATIVE_REVIEW" ]]; then
  python3 scripts/prepare_licenses.py --publish-dir "$PUBLISH" --runtime-identifier "$RID" \
    --native-review-archive "$NATIVE_REVIEW" --native-review-build-commit "$NATIVE_REVIEW_COMMIT"
else
  python3 scripts/prepare_licenses.py --publish-dir "$PUBLISH" --runtime-identifier "$RID" \
    --install-native-overrides
fi
python3 scripts/prepare_licenses.py --publish-dir "$PUBLISH" --runtime-identifier "$RID"
cp docs/同步打字插件使用说明.md "$PUBLISH/USER_GUIDE.md"
ARGS=(--bundle --rid "$RID" --output-dir "$OUTPUT")
[[ -z "$NATIVE_REVIEW" ]] || ARGS+=(--review-only)
python3 scripts/prepare_macos_release.py "${ARGS[@]}"
