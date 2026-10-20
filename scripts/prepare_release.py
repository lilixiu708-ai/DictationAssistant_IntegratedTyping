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
review_mode = parser.add_mutually_exclusive_group()
review_mode.add_argument('--draft', action='store_true',
                        help='Generate review assets under release-drafts while retaining unresolved source/license issues; never use these as public release assets')
review_mode.add_argument('--acknowledge-license-review', action='store_true',
                        help='Publish with a disclosure of the known Avalonia/MS-PL combination review; does not waive source, notice, binary or checksum verification')
args = parser.parse_args()
if args.acknowledge_license_review and args.tag not in {
        'v4.0.0-typing.5', 'v4.0.0-typing.6'}:
    parser.error('Disclosure publication is authorized only for the exact R5 and R6 release tags')

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
if not isinstance(blocking, list):
    raise SystemExit('The binary source manifest has no release review; rebuild with the current license preparation script')
unresolved = bool(blocking) or review.get('status') != 'ready'
acknowledged = False
if not args.draft and unresolved and args.acknowledge_license_review:
    # An explicit publication decision records this known unresolved review; it
    # must not silently approve a new issue, absent source or changed native DLL.
    allowed = {'avalonia-mspl-agpl-compatibility'}
    if review.get('status') != 'blocked' or not blocking or any(
            not isinstance(item, dict) or item.get('id') not in allowed
            or not isinstance(item.get('reason'), str) or not item['reason'].strip()
            for item in blocking):
        raise SystemExit('Only the explicitly acknowledged Avalonia/MS-PL review can be disclosed for publication; other unresolved license/source issues remain blocking')
    acknowledged = True
if not args.draft and unresolved and not acknowledged:
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

assets = [portable, source]
if acknowledged:
    disclosure = {
        'schema_version': 1,
        'tag': args.tag,
        'application_version': version,
        'source_commit': head,
        'publication_decision': 'explicitly_requested_publication_with_declared_unresolved_license_review',
        'license_review_resolved': False,
        'acknowledged_issue_ids': sorted({item['id'] for item in blocking}),
        'release_review': review,
        'notice': 'Publication does not establish legal clearance or resolve the recorded combination authorization issue. Original licenses, notices, fixed source archives and integrity checks remain applicable.',
    }
    review_asset = output / 'RELEASE_REVIEW.json'
    review_bytes = (json.dumps(disclosure, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    review_asset.write_bytes(review_bytes)
    changes = []
    in_version = False
    for line in (root / 'CHANGELOG.md').read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            if in_version:
                break
            in_version = line.startswith(f'## [{version}]')
            continue
        if in_version:
            changes.append(line)
    details = '\n'.join(f'- `{item["id"]}`：{item["reason"]}' for item in blocking)
    notes = (
        f'# DictationAssistant Typing {args.tag}\n\n'
        f'对应提交：`{head}`。Windows x64 便携包自带 .NET；插件也提供独立版本。\n\n'
        '## 更新\n\n' + ('\n'.join(changes).strip() or '详见同一提交的 CHANGELOG.md。') + '\n\n'
        '## 许可与来源声明\n\n'
        '本次按维护者明确要求，在保留原始许可证、归属声明、匹配源码及完整性检查的前提下发布。'
        '以下授权组合核查事项仍未解决；发布不等于已获得新的授权，也不构成法律合规结论。\n\n'
        + details + '\n\n'
        '具体核查状态保留在 `RELEASE_REVIEW.json` 和包内 `THIRD_PARTY_SOURCES.json`。'
        '第三方组件保留自身许可，第一方 AGPL 声明不替代其授权条件。\n\n'
        '请下载对应源码包及 `SHA256SUMS`；GitHub 自动生成的源码 ZIP 不包含本次额外收集的第三方源码归档。\n'
    )
    notes_asset = output / 'RELEASE_NOTES.md'
    notes_bytes = notes.encode('utf-8')
    notes_asset.write_bytes(notes_bytes)
    # Include the same disclosure with the actual artifacts, while retaining
    # the original unresolved review in the build manifests.
    with zipfile.ZipFile(portable, 'a', compression=zipfile.ZIP_DEFLATED) as zf:
        zf.writestr('RELEASE_REVIEW.json', review_bytes)
        zf.writestr('RELEASE_NOTES.md', notes_bytes)
    with zipfile.ZipFile(source, 'a', compression=zipfile.ZIP_DEFLATED) as zf:
        zf.writestr(prefix + 'RELEASE_REVIEW.json', review_bytes)
        zf.writestr(prefix + 'RELEASE_NOTES.md', notes_bytes)
    subprocess.run([sys.executable, str(root / 'scripts/verify_build.py'), '--archive', str(portable)], check=True)
    assets.extend((review_asset, notes_asset))

checksums = []
for asset in assets:
    digest = hashlib.sha256()
    with asset.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    checksums.append(f'{digest.hexdigest()}  {asset.name}\n')
(output / 'SHA256SUMS').write_bytes(''.join(checksums).encode('ascii'))
print(f'Release assets: {output}\nSource commit: {head}')
