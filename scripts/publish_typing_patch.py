"""Assemble and publish the three-platform 1.4.2 plugin patch from verified builds.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import copy
import hashlib
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import tarfile
import zipfile

from prepare_plugin_release import typing_release_config

ROOT = pathlib.Path(__file__).resolve().parents[1]
TAG = 'plugin-synchronous-typing-v1.4.2'
VERSION = '1.4.2'
REPOSITORY = 'lilixiu708-ai/DictationAssistant_IntegratedTyping'
ISSUE = 'avalonia-mspl-agpl-compatibility'
MAC_HOST_COMMIT = '2c64325cbc14e2699aa0e3ea6f6d71599a25383e'
MAC_SOURCE = {
    'host_release_tag': 'v4.0.0-typing.8',
    'host_source_commit': MAC_HOST_COMMIT,
    'url': f'https://github.com/{REPOSITORY}/releases/download/v4.0.0-typing.8/DictationAssistant-4.0.0-typing.8-macos-source.zip',
    'sha256': '7d84de6c8efa322cda00e364b66e4d0a73874488f229e1e18389a1202dfc2e38',
    'bytes': 976157282,
}
MAC_NAMES = {rid: f'DictationAssistant-R8-typing-{VERSION}-macos-{suffix}.zip'
             for rid, suffix in [('osx-x64', 'x64'), ('osx-arm64', 'arm64')]}
WIN_NAME = f'synchronous-typing-{VERSION}-win-x64.zip'
SOURCE_NAME = f'synchronous-typing-{VERSION}-source.zip'
EXPECTED = {WIN_NAME, SOURCE_NAME, *MAC_NAMES.values(),
            'RELEASE_NOTES.md', 'RELEASE_REVIEW.json', 'SHA256SUMS'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def run(*args):
    return subprocess.check_output([str(arg) for arg in args], cwd=ROOT, text=True).strip()


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def document(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def safe_zip(archive):
    names = archive.namelist()
    require(len(names) == len(set(names)) and archive.testzip() is None, 'Duplicate or corrupt ZIP members')
    require(all(not pathlib.PurePosixPath(name).is_absolute() and '..' not in pathlib.PurePosixPath(name).parts
                and '\\' not in name for name in names), 'Unsafe ZIP member')


def verify_checksums(folder, expected):
    require({p.name for p in folder.iterdir() if p.is_file()} == expected, 'Unexpected release asset set')
    seen = set()
    for line in (folder / 'SHA256SUMS').read_text(encoding='ascii').splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([^/\\]+)', line)
        require(match is not None, 'Malformed checksum')
        value, name = match.groups()
        require(name in expected - {'SHA256SUMS'} and name not in seen, 'Unknown or repeated checksum')
        require(digest(folder / name) == value, 'Checksum mismatch: ' + name)
        seen.add(name)
    require(seen == expected - {'SHA256SUMS'}, 'Incomplete checksum coverage')


def verify_windows(part, commit):
    raw_name = f'synchronous-typing-{VERSION}.zip'
    verify_checksums(part, {raw_name, SOURCE_NAME, 'SHA256SUMS', 'RELEASE_NOTES.md', 'RELEASE_REVIEW.json'})
    review = document(part / 'RELEASE_REVIEW.json')
    config = typing_release_config(TAG)
    compat = review.get('host_compatibility', {})
    expected = {'plugin_source_commit': commit, 'plugin_version': VERSION,
                'host_release_tag': config['host_release_tag'], 'host_source_commit': config['host']['source_commit'],
                'plugin_api_version': 1, 'host_feature_level': 5, 'word_plugin_version': '1.2.0',
                'practice_and_configuration_loaded': True, 'main_window_opened_and_closed': True,
                'host_non_typing_file_count': 387, 'host_documentation_path_adaptations': []}
    require(review.get('source_commit') == commit and review.get('plugin_version') == VERSION
            and review.get('publication_scope') == 'standalone-plugin-update-existing-host'
            and review.get('legal_clearance_claimed') is False
            and all(compat.get(key) == value for key, value in expected.items()),
            'Windows compatibility evidence does not match this patch')
    blockers = review.get('host_release_review', {}).get('blocking_issues', [])
    require(blockers and all(item.get('id') == ISSUE for item in blockers), 'Unexpected Windows review state')
    with zipfile.ZipFile(part / raw_name) as archive:
        safe_zip(archive)
        prefix = 'SynchronousTyping/'
        plugin = archive.read(prefix + 'DictationAssistant.Practices.Typing.dll')
        manifest = json.loads(archive.read(prefix + 'plugin.json'))
        require(hashlib.sha256(plugin).hexdigest() == compat.get('plugin_dll_sha256')
                and f'{VERSION}+{commit}'.encode() in plugin
                and manifest.get('version') == VERSION and manifest.get('minimumHostFeatureLevel') == 5,
                'Windows plugin binary/manifest mismatch')
        inventory = json.loads(archive.read(prefix + 'PLUGIN_COMPONENTS.json'))
        for item in inventory['files']:
            require(hashlib.sha256(archive.read(prefix + item['path'])).hexdigest() == item['sha256'],
                    'Windows plugin notice/dependency inventory mismatch')
    return review


def assemble(args):
    import prepare_typing_macos_patch as mac
    commit = run('git', 'rev-parse', 'HEAD')
    require(args.tag == TAG and run('git', 'rev-parse', TAG + '^{commit}') == commit, 'Exact patch tag required')
    windows = args.parts_root / 'win-x64'
    win_review = verify_windows(windows, commit)
    platforms = {'win-x64': win_review}
    for rid in MAC_NAMES:
        platforms[rid] = mac.verify_part(args.parts_root / rid, rid, commit)
    folder = args.output_dir.resolve()
    folder.mkdir(parents=True, exist_ok=True)
    require(not any(folder.iterdir()), 'Use an empty final release directory')
    shutil.copyfile(windows / f'synchronous-typing-{VERSION}.zip', folder / WIN_NAME)
    for rid, name in MAC_NAMES.items():
        shutil.copyfile(args.parts_root / rid / name, folder / name)
    review = {'schema_version': 1, 'tag': TAG, 'plugin_id': 'synchronous-typing',
              'plugin_version': VERSION, 'source_commit': commit, 'platforms': platforms,
              'publication_scope': 'plugin-patch-three-platforms-existing-published-hosts',
              'host_executables_rebuilt': False, 'minimum_macos': '15.0',
              'acknowledged_issue_ids': [ISSUE], 'license_review_resolved': False,
              'apple_notarized': False, 'macos_signing': 'ad-hoc',
              'unchanged_mac_host_corresponding_source': MAC_SOURCE,
              'source_delivery': 'The patch source ZIP includes this complete Git revision and private dependency source. The unchanged Mac host and all its native source archives are available in the fixed original R8 source ZIP identified here.',
              'verification_limits': 'Native-runner plugin discovery, existing logic and main-window checks are recorded per platform. Linux temporary headless observations cover shuffled silent/audio practice. Full real-user practice, IME, physical speakers, online translation and credential migration are not claimed verified.'}
    notes = f'''# 背诵练习插件 1.4.2 · 随机词序补丁

准备页新增“打乱顺序”。静音背诵和播报听写都可使用；提示与答案成对重排，重复题目保留。
默认按原顺序练习，勾选后在开始本轮时打乱，一轮内保持固定。

- Windows：已有 R7，关闭程序后，用 Windows 补丁里的完整 SynchronousTyping 文件夹替换 Plugins 下的同名目录。
- Intel Mac：下载 macos-x64 整合包。
- M 系列 Mac：下载 macos-arm64 整合包。

Mac 更新包沿用 R8 本体并内置新插件，解压后从 payload 中将整个 DictationAssistant.app 替换到“应用程序”。
需要 macOS 15+；使用 ad-hoc 签名，尚未经过 Apple 公证。首次打开如被拦截，核对来源后从“系统设置 → 隐私与安全性”确认打开。
本次未重新编译本体，也未更新 Word / Excel 导入插件。建议更新前备份用户数据。

Windows、Intel Mac 与 M 系列 Mac 均核对新插件加载和主窗口开闭；随机配对、静音与播报顺序另经 Linux 临时界面检查。
这些检查不代替真实输入法、扬声器、在线翻译和完整人工练习验证。

补丁源码与构建脚本见 source.zip，未改动的 [R8 完整配套源码]({MAC_SOURCE['url']}) 保留原始固定版本。
R8 源码 SHA-256：{MAC_SOURCE['sha256']}。本次附件的 SHA-256 见 SHA256SUMS。
Avalonia/MS-PL 与 AGPL 的组合授权事项仍未解决，原始许可和具体声明随包保留。
对应补丁提交：{commit}。
'''
    write_json(folder / 'RELEASE_REVIEW.json', review)
    (folder / 'RELEASE_NOTES.md').write_text(notes, encoding='utf-8')
    source = folder / SOURCE_NAME
    # Retain all private dependency archives from the verified Windows plugin
    # source, then add this exact revision's complete Git export.
    with zipfile.ZipFile(windows / SOURCE_NAME) as original, zipfile.ZipFile(source, 'w', compression=zipfile.ZIP_DEFLATED) as target:
        safe_zip(original)
        for item in original.infolist():
            new = copy.copy(item)
            if item.filename == 'RELEASE_REVIEW.json':
                new.filename = 'platforms/win-x64/RELEASE_REVIEW.json'
            with original.open(item) as incoming, target.open(new, 'w') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
        existing = set(target.namelist())
        exported = subprocess.check_output(['git', 'archive', '--format=tar', commit], cwd=ROOT)
        with tarfile.open(fileobj=io.BytesIO(exported)) as tree:
            for item in tree:
                if not item.isfile():
                    continue
                data = tree.extractfile(item).read()
                if item.name in existing:
                    require(target.read(item.name) == data, 'Source differs from exact Git export: ' + item.name)
                else:
                    target.writestr(item.name, data)
                    existing.add(item.name)
        for rid in MAC_NAMES:
            for path in sorted((args.parts_root / rid).glob('*.json')):
                target.write(path, 'platforms/' + rid + '/' + path.name)
        target.write(folder / 'RELEASE_REVIEW.json', 'RELEASE_REVIEW.json')
        target.write(folder / 'RELEASE_NOTES.md', 'RELEASE_NOTES.md')
        target.writestr('HOST_SOURCE_REFERENCES.json', json.dumps(MAC_SOURCE, indent=2) + '\n')
    for path in folder.glob('*.zip'):
        with zipfile.ZipFile(path) as archive:
            safe_zip(archive)
    (folder / 'SHA256SUMS').write_text(''.join(f'{digest(path)}  {path.name}\n'
        for path in sorted(folder.iterdir()) if path.is_file()), encoding='ascii')
    verify_checksums(folder, EXPECTED)
    print('Prepared Windows plugin patch, two signed Mac app updates and matching patch source.')


def pages(endpoint):
    result = []
    for page in range(1, 100):
        data = json.loads(run('gh', 'api', endpoint + f'?per_page=100&page={page}'))
        result.extend(data)
        if len(data) < 100:
            return result
    raise ValueError('Pagination did not finish')


def remote_target(tag):
    values = dict(line.split()[::-1] for line in run('git', 'ls-remote', 'origin',
                  f'refs/tags/{tag}', f'refs/tags/{tag}^{{}}').splitlines())
    return values.get(f'refs/tags/{tag}^{{}}', values.get(f'refs/tags/{tag}'))


def publish(args):
    commit = run('git', 'rev-parse', 'HEAD')
    build_id = os.environ.get('PATCH_BUILD_RUN_ID', '')
    require(args.tag == TAG and os.environ.get('GH_REPO') == REPOSITORY
            and os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch'
            and os.environ.get('GITHUB_REF') == 'refs/tags/' + TAG
            and os.environ.get('GITHUB_SHA') == commit == run('git', 'rev-parse', TAG + '^{commit}')
            and re.fullmatch('[1-9][0-9]*', build_id) and build_id != os.environ.get('GITHUB_RUN_ID'),
            'An explicit exact-tag promotion of a prior verified build is required')
    built = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/actions/runs/{build_id}'))
    require(built.get('conclusion') == 'success' and built.get('status') == 'completed'
            and built.get('head_sha') == commit and built.get('event') == 'workflow_dispatch'
            and built.get('path') == '.github/workflows/release-typing-patch.yml'
            and built.get('repository', {}).get('full_name') == REPOSITORY
            and built.get('head_repository', {}).get('full_name') == REPOSITORY,
            'The original successful build does not match this tagged source')
    folder = args.output_dir.resolve()
    verify_checksums(folder, EXPECTED)
    review = document(folder / 'RELEASE_REVIEW.json')
    require(review.get('source_commit') == commit and review.get('tag') == TAG
            and review.get('plugin_version') == VERSION and review.get('license_review_resolved') is False
            and review.get('acknowledged_issue_ids') == [ISSUE]
            and review.get('unchanged_mac_host_corresponding_source') == MAC_SOURCE,
            'Patch disclosure mismatch')
    config = typing_release_config(TAG)
    require(remote_target(TAG) == commit and remote_target(config['host_release_tag']) == config['host']['source_commit']
            and remote_target('v4.0.0-typing.8') == MAC_HOST_COMMIT, 'A patch or published host tag changed')
    # The original full host source must remain publicly downloadable under its
    # exact published digest; this patch does not replace that asset.
    host = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/releases/tags/v4.0.0-typing.8'))
    original_source = next((a for a in host.get('assets', []) if a['browser_download_url'] == MAC_SOURCE['url']), {})
    require(not host.get('draft') and host.get('target_commitish') == MAC_HOST_COMMIT
            and original_source.get('state') == 'uploaded' and original_source.get('size') == MAC_SOURCE['bytes']
            and original_source.get('digest') == 'sha256:' + MAC_SOURCE['sha256'], 'Original R8 source asset changed')
    require(not any(r['tag_name'] == TAG for r in pages(f'repos/{REPOSITORY}/releases')),
            'Refusing to overwrite an existing release or draft')
    run('gh', 'release', 'create', TAG, '--draft', '--prerelease', '--latest=false', '--verify-tag',
        '--target', commit, '--title', '背诵练习插件 1.4.2 · 随机词序（Windows / Mac）',
        '--notes-file', folder / 'RELEASE_NOTES.md')
    run('gh', 'release', 'upload', TAG, *sorted(folder.iterdir()))
    matches = [r for r in pages(f'repos/{REPOSITORY}/releases') if r['tag_name'] == TAG]
    require(len(matches) == 1 and matches[0]['draft'], 'Expected exactly the newly created draft')
    release = matches[0]
    require(release.get('target_commitish') == commit and len(release['assets']) == len(EXPECTED)
            and {a['name'] for a in release['assets']} == EXPECTED, 'Draft metadata or asset set mismatch')
    for asset in release['assets']:
        path = folder / asset['name']
        require(asset.get('state') == 'uploaded' and asset['size'] == path.stat().st_size
                and asset.get('digest') == 'sha256:' + digest(path), 'Uploaded asset differs: ' + path.name)
    current = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/releases/{release["id"]}'))
    require(current.get('draft') and current.get('tag_name') == TAG
            and sorted((a['id'], a['name'], a['size'], a.get('digest'), a.get('state')) for a in current['assets'])
            == sorted((a['id'], a['name'], a['size'], a.get('digest'), a.get('state')) for a in release['assets'])
            and current.get('target_commitish') == commit and remote_target(TAG) == commit,
            'Draft or tag changed before publication')
    run('gh', 'release', 'edit', TAG, '--tag', TAG, '--target', commit,
        '--draft=false', '--prerelease', '--latest=false')
    print(f'https://github.com/{REPOSITORY}/releases/tag/{TAG}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--assemble', action='store_true')
    mode.add_argument('--publish', action='store_true')
    parser.add_argument('--tag', default=TAG)
    parser.add_argument('--parts-root', type=pathlib.Path)
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    arguments = parser.parse_args()
    try:
        if arguments.assemble:
            require(arguments.parts_root is not None, 'Parts directory is required')
            assemble(arguments)
        else:
            publish(arguments)
    except Exception as error:
        parser.exit(1, f'Typing patch release failed: {error}\n')
