"""Prepare validated release assets, or review drafts from the current clean HEAD.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from collect_third_party_sources import collect

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('tag', help='Release tag, for example v4.0.0-typing.1')
parser.add_argument('--draft', action='store_true',
                    help='Generate review assets under release-drafts while retaining unresolved source/license issues; never use these as public release assets')
args = parser.parse_args()

project = root / 'src/DictationAssistant.App/DictationAssistant.App.csproj'
version = ET.parse(project).findtext('.//Version')
if args.tag != f'v{version}':
    raise SystemExit('Release tag must match the application Version prefixed with v')

def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=root, text=True).strip()

head = git('rev-parse', 'HEAD')
subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=root, check=True)
if git('ls-files', '--others', '--exclude-standard'):
    raise SystemExit('Commit or remove untracked files before preparing release assets')
subprocess.run([sys.executable, str(root / 'scripts/verify_build.py')], check=True)

archive = root / 'artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip'
with zipfile.ZipFile(archive) as zf:
    app = next(name for name in zf.namelist() if name.removeprefix('./') == 'DictationAssistant.App.dll')
    if f'{version}+{head}'.encode() not in zf.read(app):
        raise SystemExit('Application build metadata does not match the release version and commit; rebuild it')
    members = {name.removeprefix('./'): name for name in zf.namelist()}
    source_manifest_bytes = zf.read(members['THIRD_PARTY_SOURCES.json'])
    component_manifest_bytes = zf.read(members['THIRD_PARTY_COMPONENTS.json'])
    runtime_notices = {name: zf.read(original) for name, original in members.items()
                       if name.startswith('LICENSES/runtime/') and not name.endswith('/')}

review = json.loads(source_manifest_bytes).get('release_review', {})
blocking = review.get('blocking_issues')
if blocking is None:
    raise SystemExit('The binary source manifest has no release review; rebuild with the current license preparation script')
if not args.draft and (blocking or review.get('status') != 'ready'):
    details = '\n'.join('- ' + (item.get('reason', item.get('id', 'Unresolved issue')) if isinstance(item, dict) else str(item))
                        for item in blocking)
    raise SystemExit('Public release preparation is blocked by unresolved license/source review:\n' + (details or '- Release review is not marked ready'))
if not args.draft and git('rev-parse', '--verify', f'refs/tags/{args.tag}^{{commit}}') != head:
    raise SystemExit('Build HEAD must match the release tag')

output = root / 'artifacts' / ('release-drafts' if args.draft else 'release') / args.tag
output.mkdir(parents=True, exist_ok=True)
portable = output / f'DictationAssistant-{version}-win-x64-portable.zip'
source = output / f'DictationAssistant-{version}-source.zip'
shutil.copyfile(archive, portable)
subprocess.run([
    'git', 'archive', '--format=zip', f'--prefix=DictationAssistant-{version}-source/',
    f'--output={source}', head if args.draft else args.tag,
], cwd=root, check=True)

# Preserve the matching dependency-source directions beside the application
# source, and include fixed source archives rather than just NuGet binaries.
source_cache = root / 'artifacts/third-party-sources'
source_cache.mkdir(parents=True, exist_ok=True)
source_manifest = output / 'THIRD_PARTY_SOURCES.json'
source_manifest.write_bytes(source_manifest_bytes)
source_index = collect(source_manifest, source_cache)
prefix = f'DictationAssistant-{version}-source/'
with zipfile.ZipFile(source, 'a', compression=zipfile.ZIP_DEFLATED) as zf:
    for item in source_index.get('inline_source_files', []):
        if hashlib.sha256(zf.read(prefix + item['path'])).hexdigest() != item['sha256']:
            raise SystemExit(f'Inline dependency source does not match the binary source index: {item["path"]}')
    zf.writestr(prefix + 'THIRD_PARTY_COMPONENTS.json', component_manifest_bytes)
    zf.writestr(prefix + 'THIRD_PARTY_SOURCES.json', source_manifest_bytes)
    zf.write(source_cache / 'SOURCE_ARCHIVES.json', prefix + 'third-party-sources/SOURCE_ARCHIVES.json')
    for item in source_index['archives']:
        if item.get('archive_path'):
            # Upstream archives are already compressed; avoid expensive nested
            # recompression and preserve their checked bytes exactly.
            zf.write(source_cache / item['archive_path'], prefix + 'third-party-sources/' + item['archive_path'],
                     compress_type=zipfile.ZIP_STORED)
    for name, content in runtime_notices.items():
        zf.writestr(prefix + name, content)
    if args.draft:
        zf.writestr(prefix + 'DRAFT-REVIEW.json', json.dumps({'public_release_ready': False, 'blocking_issues': blocking},
                                                          ensure_ascii=False, indent=2) + '\n')
source_manifest.unlink()

checksums = []
for asset in (portable, source):
    digest = hashlib.sha256()
    with asset.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    checksums.append(f'{digest.hexdigest()}  {asset.name}\n')
(output / 'SHA256SUMS').write_bytes(''.join(checksums).encode('ascii'))
print(f'Release assets: {output}\nSource commit: {head}')
