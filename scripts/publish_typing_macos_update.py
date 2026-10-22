"""Publish complete Mac app updates containing the verified typing 1.4.3 plugin.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-22
"""
import argparse
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

import prepare_typing_macos_patch as mac
from collect_third_party_sources import collect
from prepare_licenses import normalize_archive, source_archives
from publish_typing_patch import (MAC_SOURCE, digest, document, pages, remote_target,
                                 require, run, safe_zip, verify_checksums, write_json)

ROOT = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = 'lilixiu708-ai/DictationAssistant_IntegratedTyping'
TAG = 'plugin-synchronous-typing-v1.4.3-macos.1'
VERSION = '1.4.3'
FEATURE_SOURCE = 'fd8ac3c841270e4d20ddd64186663631feaa801d'
WORKFLOW = '.github/workflows/release-typing-macos-update.yml'
ISSUE = 'avalonia-mspl-agpl-compatibility'
MAC_NAMES = {rid: f'DictationAssistant-R8-typing-{VERSION}-macos-{suffix}.zip'
             for rid, suffix in (('osx-x64', 'x64'), ('osx-arm64', 'arm64'))}
SOURCE_NAME = 'synchronous-typing-1.4.3-macos-source.zip'
EXPECTED = {*MAC_NAMES.values(), SOURCE_NAME, 'RELEASE_NOTES.md', 'RELEASE_REVIEW.json', 'SHA256SUMS'}
RECORDS = ('BUNDLE_INFO.json', 'PATCH_BASELINE.json', 'BUILD_VALIDATION.json',
           'HOST_SOURCE_REFERENCES.json', 'THIRD_PARTY_COMPONENTS.json',
           'THIRD_PARTY_SOURCES.json', 'ANSWER_MODEL_OBSERVATION.json', 'PLUGIN_DISCOVERY.json')
RUNTIME_SOURCE = {
    'url': 'https://codeload.github.com/dotnet/runtime/tar.gz/60629d14374c56f1cb51819049ad1fa529307f8d',
    'revision': '60629d14374c56f1cb51819049ad1fa529307f8d',
    'sha256': '4fae24371e108a046d7bfd30785e9a2f4400552b165b70300a72f855370da3de',
    'bytes': 95073609,
}


def source_tree(commit):
    exported = subprocess.check_output(['git', 'archive', '--format=tar', commit], cwd=ROOT)
    result = {}
    with tarfile.open(fileobj=io.BytesIO(exported)) as tree:
        for item in tree:
            if item.isdir():
                continue
            require(item.isfile(), 'Unexpected non-file in the source tree')
            result[item.name] = tree.extractfile(item).read()
    return result


def exact_source(args):
    commit = run('git', 'rev-parse', 'HEAD')
    require(args.tag == TAG and run('git', 'rev-parse', TAG + '^{commit}') == commit,
            'Use the exact Mac update tag')
    require(not run('git', 'status', '--porcelain'), 'Commit source changes before preparing an update')
    run('git', 'diff', '--quiet', FEATURE_SOURCE, commit, '--',
        'plugins/DictationAssistant.Practices.Typing', 'src/DictationAssistant.Plugin.Abstractions')
    mac.source_scope(commit, TAG)
    return commit


def platform_reviews(parts_root, commit):
    result = {}
    for rid in MAC_NAMES:
        part = parts_root / rid
        result[rid] = mac.verify_part(part, rid, commit, plugin_release_tag=TAG)
        discovery = document(part / 'PLUGIN_DISCOVERY.json')
        plugins = {item['Manifest']['Id']: item for item in discovery.get('Plugins', []) if item.get('Manifest')}
        require(discovery.get('ApiVersion') == 1 and discovery.get('HostFeatureLevel') == 5
                and discovery.get('LoadedCount') == 2 and set(plugins) == {'synchronous-typing', 'docx-vocabulary'},
                'The native application discovery report differs')
        for identity, version in (('synchronous-typing', VERSION), ('docx-vocabulary', '1.2.0')):
            item = plugins[identity]
            require(item.get('IsLoaded') is True and item.get('Error') is None
                    and item['Manifest'].get('Version') == version,
                    'The native app did not load the expected bundled plugin')
    return result


def private_source_index():
    catalog = document(ROOT / 'third-party/components.json')
    entry = next(item for item in catalog['packages']
                 if item['id'] == 'System.Security.Cryptography.ProtectedData' and item['version'] == '10.0.0')
    archives = source_archives(entry)
    require(len(archives) == 1, 'The existing private dependency source scope changed')
    source = normalize_archive(archives[0])
    require(source['url'] == RUNTIME_SOURCE['url'] and source['revision'] == RUNTIME_SOURCE['revision'],
            'The private dependency no longer uses its fixed reviewed runtime source')
    manifest = {'schema_version': 1,
                'scope': 'Existing ProtectedData 10.0.0 shipped by the unchanged R8 runtime; other host and native sources remain in the fixed R8 source asset.',
                'archives': [{**source, 'sha256': RUNTIME_SOURCE['sha256'],
                              'components': ['System.Security.Cryptography.ProtectedData/10.0.0']}]}
    path = ROOT / 'artifacts/macos-143-private-source-manifest.json'
    write_json(path, manifest)
    cache = ROOT / 'artifacts/third-party-sources'
    index = collect(path, cache)
    require(len(index['archives']) == 1
            and all(index['archives'][0].get(key) == value for key, value in RUNTIME_SOURCE.items()),
            'The private source archive does not match its fixed bytes')
    return index, cache


def plugin_build_notes(commit):
    return f'''# Mac 1.4.3 整合更新源码

本包包含提交 `{commit}` 的完整 Git 导出、私有依赖源码和两种架构的验证记录。
未改动的 R8 本体与原生库对应源码见 HOST_SOURCE_REFERENCES.json；其固定源码包与本包共同提供整合更新的对应源码。

只编译插件时，在匹配架构的 Mac 安装 global.json 指定的 .NET SDK，然后运行：

```bash
dotnet restore plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj -r osx-arm64 --locked-mode -p:SelfContained=false -p:NuGetLockFilePath=packages.osx-arm64.lock.json
dotnet publish plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj -c Release -r osx-arm64 --self-contained false --no-restore -p:SourceRevisionId={commit} -o artifacts/SynchronousTyping
```

Intel 将两条命令中的 `osx-arm64` 改为 `osx-x64`。这只生成插件，不能直接覆盖已签名应用内部的文件。
完整应用的组装、会话配置观察、重新签名与原生运行验证，按 `.github/workflows/release-typing-macos-update.yml` 的步骤执行。
使用干净的 Git checkout 和 `{TAG}` 标签；调用 `prepare_typing_macos_patch.py` 时传入 `--plugin-release-tag {TAG}`。
工作流先 --build，再执行原生会话观察，最后 --validate；不需要真实模型或翻译服务凭据。

模型和翻译配置在 Mac 仅保留到当前进程退出；已确认的中文答案单独保存在用户数据目录。
原始许可保留；Avalonia/MS-PL 与 AGPL 组合授权事项继续披露，并未因本次构建解决。
'''


def notes(commit):
    return f'''# Mac 整合更新 · 背诵练习 1.4.3

本包已装好最新背诵练习插件和 Word / Excel 导入插件，无需再单独安装补丁。
Intel Mac 下载 macos-x64，M 系列下载 macos-arm64。需要 macOS 15 或更新版本。
退出程序，解压 ZIP，将 payload 中的整个 DictationAssistant.app 替换到“应用程序”。

中文答案可以手动补充同义说法，也可以在练习前生成候选、逐条确认保存后离线判分。
模型设置位于“插件管理 → 背诵练习 → 配置 → 同义答案模型”；没有模型配置也能手动填写。
模型和翻译凭据只在当前运行中使用，退出后需重新输入；已确认答案会保存在本地。
英文拼写和化学式保留原规则，也可关闭同义答案选项。随机词序、中途结算和提示继续可用。

沿用固定 R8 本体与 Word / Excel 导入 1.2.0，只重新构建背诵练习插件并重新签名。
采用 ad-hoc 签名，未经过 Apple 公证；首次打开被拦截时，核对来源后可从“系统设置 → 隐私与安全性”确认打开。
Windows 1.4.3 发行和已有 Mac 下载保持原样，本次两包已包含完整应用及运行环境。

两种架构分别核对固定来源、严格签名、双插件加载、15 项既有逻辑检查、模型配置仅会话保存、系统语音、SDL dummy 播放和主窗口正常开闭。
这些检查不代替真实输入法、扬声器、在线模型或完整人工背诵验证。

本次源码：{commit}。补丁源码与私有依赖见 {SOURCE_NAME}；未改动的 [R8 对应源码]({MAC_SOURCE['url']}) SHA-256 为 {MAC_SOURCE['sha256']}。
全部附件校验见 SHA256SUMS。Avalonia/MS-PL 与 AGPL 的组合授权事项仍未解决，原始许可和具体声明随包保留。
'''


def assemble(args):
    commit = exact_source(args)
    platforms = platform_reviews(args.parts_root, commit)
    folder = args.output_dir.resolve()
    folder.mkdir(parents=True, exist_ok=True)
    require(not any(folder.iterdir()), 'Use an empty final release directory')
    for rid, name in MAC_NAMES.items():
        shutil.copyfile(args.parts_root / rid / name, folder / name)
    review = {'schema_version': 1, 'tag': TAG, 'plugin_id': 'synchronous-typing',
              'plugin_version': VERSION, 'source_commit': commit, 'feature_source_commit': FEATURE_SOURCE,
              'platforms': platforms, 'publication_scope': 'complete-macos-app-updates-existing-r8-host',
              'host_executables_rebuilt': False, 'minimum_macos': '15.0',
              'bundled_plugins': {'synchronous-typing': VERSION, 'docx-vocabulary': '1.2.0'},
              'apple_notarized': False, 'macos_signing': 'ad-hoc',
              'acknowledged_issue_ids': [ISSUE], 'license_review_resolved': False,
              'unchanged_mac_host_corresponding_source': MAC_SOURCE,
              'model_configuration_storage': 'process-session-only-no-persistent-credentials',
              'accepted_answers_storage': 'local-user-data-directory',
              'source_delivery': 'Complete Git export and the fixed private dependency archive accompany this update; unchanged host and native source remain available in the fixed R8 source asset.',
              'verification_limits': 'Real native Mac runners verify package provenance, signatures, plugin discovery, existing logic, model configuration session storage, synthetic speech, SDL dummy playback and main-window open/close. Full human practice, IME, physical speakers, live model/translation accounts and credential migration are not claimed verified.'}
    write_json(folder / 'RELEASE_REVIEW.json', review)
    (folder / 'RELEASE_NOTES.md').write_text(notes(commit), encoding='utf-8')
    index, cache = private_source_index()
    tree = source_tree(commit)
    with zipfile.ZipFile(folder / SOURCE_NAME, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in tree.items():
            archive.writestr(name, data)
        for rid in MAC_NAMES:
            for name in RECORDS:
                archive.write(args.parts_root / rid / name, 'platforms/' + rid + '/' + name)
        for name in ('RELEASE_REVIEW.json', 'RELEASE_NOTES.md'):
            archive.write(folder / name, name)
        archive.writestr('PLUGIN_BUILD.md', plugin_build_notes(commit))
        archive.writestr('HOST_SOURCE_REFERENCES.json', json.dumps(MAC_SOURCE, indent=2) + '\n')
        archive.writestr('third-party-sources/SOURCE_ARCHIVES.json', json.dumps(index, ensure_ascii=False, indent=2) + '\n')
        for item in index['archives']:
            archive.write(cache / item['archive_path'], 'third-party-sources/' + item['archive_path'])
    (folder / 'SHA256SUMS').write_text(''.join(f'{digest(path)}  {path.name}\n'
        for path in sorted(folder.iterdir()) if path.is_file()), encoding='ascii')
    verify_output(args, commit, platforms)
    print('Prepared two complete signed Mac app updates, source and verification records.')


def verify_output(args, commit, platforms=None):
    folder = args.output_dir.resolve()
    require({path.name for path in folder.iterdir()} == EXPECTED
            and all(path.is_file() and not path.is_symlink() for path in folder.iterdir()),
            'Only the six regular release assets may be published')
    verify_checksums(folder, EXPECTED)
    platforms = platforms or platform_reviews(args.parts_root, commit)
    review = document(folder / 'RELEASE_REVIEW.json')
    require(review.get('source_commit') == commit and review.get('tag') == TAG
            and review.get('plugin_version') == VERSION and review.get('feature_source_commit') == FEATURE_SOURCE
            and review.get('publication_scope') == 'complete-macos-app-updates-existing-r8-host'
            and review.get('host_executables_rebuilt') is False and review.get('platforms') == platforms
            and review.get('license_review_resolved') is False
            and review.get('acknowledged_issue_ids') == [ISSUE]
            and review.get('unchanged_mac_host_corresponding_source') == MAC_SOURCE
            and review.get('model_configuration_storage') == 'process-session-only-no-persistent-credentials',
            'Mac update disclosure differs from the original verified platforms')
    for rid, name in MAC_NAMES.items():
        require(digest(folder / name) == platforms[rid]['binary_sha256'],
                'An assembled Mac application differs from its original validation')
    require((folder / 'RELEASE_NOTES.md').read_text(encoding='utf-8') == notes(commit),
            'The release notes differ from the source-controlled update scope')
    tree = source_tree(commit)
    with zipfile.ZipFile(folder / SOURCE_NAME) as archive:
        safe_zip(archive)
        for name, data in tree.items():
            require(archive.read(name) == data, 'Source differs from exact Git export: ' + name)
        index = json.loads(archive.read('third-party-sources/SOURCE_ARCHIVES.json'))
        require(len(index.get('archives', [])) == 1
                and all(index['archives'][0].get(key) == value for key, value in RUNTIME_SOURCE.items()),
                'The private dependency source index differs')
        generated = {'RELEASE_REVIEW.json', 'RELEASE_NOTES.md', 'HOST_SOURCE_REFERENCES.json',
                     'PLUGIN_BUILD.md', 'third-party-sources/SOURCE_ARCHIVES.json'}
        for item in index['archives']:
            name = 'third-party-sources/' + item['archive_path']
            require(archive.getinfo(name).file_size == item['bytes']
                    and hashlib.sha256(archive.read(name)).hexdigest() == item['sha256'],
                    'The private source archive bytes differ')
            generated.add(name)
        for rid in MAC_NAMES:
            for name in RECORDS:
                target = 'platforms/' + rid + '/' + name
                require(archive.read(target) == (args.parts_root / rid / name).read_bytes(),
                        'Packaged and original Mac validation records differ')
                generated.add(target)
        require(set(archive.namelist()) == set(tree) | generated,
                'The update source contains missing, duplicate or unlisted files')
        for name in ('RELEASE_REVIEW.json', 'RELEASE_NOTES.md'):
            require(archive.read(name) == (folder / name).read_bytes(), 'Embedded release record differs')
        require(json.loads(archive.read('HOST_SOURCE_REFERENCES.json')) == MAC_SOURCE
                and archive.read('PLUGIN_BUILD.md').decode() == plugin_build_notes(commit),
                'Source build instructions or fixed host source differ')
    return review


def publish(args):
    commit = exact_source(args)
    build_id = os.environ.get('PATCH_BUILD_RUN_ID', '')
    require(os.environ.get('GH_REPO') == REPOSITORY
            and os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch'
            and os.environ.get('GITHUB_REF') == 'refs/tags/' + TAG
            and os.environ.get('GITHUB_SHA') == commit
            and re.fullmatch('[1-9][0-9]*', build_id) and build_id != os.environ.get('GITHUB_RUN_ID'),
            'An explicit exact-tag promotion of a prior verified build is required')
    built = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/actions/runs/{build_id}'))
    require(built.get('conclusion') == 'success' and built.get('status') == 'completed'
            and built.get('head_sha') == commit and built.get('head_branch') == 'main'
            and built.get('event') == 'workflow_dispatch' and built.get('path') == WORKFLOW
            and built.get('repository', {}).get('full_name') == REPOSITORY
            and built.get('head_repository', {}).get('full_name') == REPOSITORY,
            'The original successful Mac build does not match this tagged source')
    verify_output(args, commit)
    require(remote_target(TAG) == commit and remote_target(mac.HOST_TAG) == mac.HOST_COMMIT
            and remote_target('plugin-synchronous-typing-v1.4.3') == FEATURE_SOURCE,
            'A patch, Windows feature or published R8 tag changed')
    host = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/releases/tags/{mac.HOST_TAG}'))
    original = next((item for item in host.get('assets', [])
                     if item.get('browser_download_url') == MAC_SOURCE['url']), {})
    require(host.get('draft') is False and host.get('target_commitish') == mac.HOST_COMMIT
            and original.get('state') == 'uploaded' and original.get('size') == MAC_SOURCE['bytes']
            and original.get('digest') == 'sha256:' + MAC_SOURCE['sha256'],
            'The original fixed R8 corresponding-source asset changed')
    require(not any(item['tag_name'] == TAG for item in pages(f'repos/{REPOSITORY}/releases')),
            'Refusing to overwrite an existing release or draft')
    folder = args.output_dir.resolve()
    run('gh', 'release', 'create', TAG, '--draft', '--prerelease', '--latest=false', '--verify-tag',
        '--target', commit, '--title', 'Mac 整合更新 · 背诵练习 1.4.3（Intel / M 系列）',
        '--notes-file', folder / 'RELEASE_NOTES.md')
    run('gh', 'release', 'upload', TAG, *sorted(folder.iterdir()))
    matches = [item for item in pages(f'repos/{REPOSITORY}/releases') if item['tag_name'] == TAG]
    require(len(matches) == 1 and matches[0].get('draft') is True, 'Expected the new update draft')
    release = matches[0]
    require(release.get('target_commitish') == commit and len(release['assets']) == len(EXPECTED)
            and {item['name'] for item in release['assets']} == EXPECTED, 'Draft asset set or target differs')
    for asset in release['assets']:
        path = folder / asset['name']
        require(asset.get('state') == 'uploaded' and asset['size'] == path.stat().st_size
                and asset.get('digest') == 'sha256:' + digest(path), 'Uploaded bytes differ: ' + path.name)
    current = json.loads(run('gh', 'api', f'repos/{REPOSITORY}/releases/{release["id"]}'))
    require(current.get('draft') is True and current.get('tag_name') == TAG
            and current.get('target_commitish') == commit and remote_target(TAG) == commit
            and sorted((item['id'], item['name'], item['size'], item.get('digest'), item.get('state'))
                       for item in current['assets'])
            == sorted((item['id'], item['name'], item['size'], item.get('digest'), item.get('state'))
                      for item in release['assets']), 'The draft or tag changed before publication')
    run('gh', 'release', 'edit', TAG, '--tag', TAG, '--target', commit,
        '--draft=false', '--prerelease', '--latest=false')
    print(f'https://github.com/{REPOSITORY}/releases/tag/{TAG}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--assemble', action='store_true')
    mode.add_argument('--publish', action='store_true')
    parser.add_argument('--tag', default=TAG)
    parser.add_argument('--parts-root', type=pathlib.Path, required=True)
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    arguments = parser.parse_args()
    try:
        (assemble if arguments.assemble else publish)(arguments)
    except Exception as error:
        parser.exit(1, f'Mac update release failed: {error}\n')
