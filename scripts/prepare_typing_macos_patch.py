"""Repackage the fixed R8 Mac apps with a verified typing plugin update.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-22
"""
import argparse
import copy
import hashlib
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import urllib.request
import zipfile

import prepare_macos_release as mac

ROOT = pathlib.Path(__file__).resolve().parents[1]
VERSION = '1.4.2'
DEFAULT_RELEASE_TAG = 'plugin-synchronous-typing-v1.4.2'
MACOS_143_RELEASE_TAG = 'plugin-synchronous-typing-v1.4.3-macos.1'
WINDOWS_143_FEATURE_COMMIT = 'fd8ac3c841270e4d20ddd64186663631feaa801d'
PATCH_RELEASES = {
    DEFAULT_RELEASE_TAG: {'version': VERSION, 'source_asset': 'synchronous-typing-1.4.2-source.zip'},
    MACOS_143_RELEASE_TAG: {'version': '1.4.3', 'source_asset': 'synchronous-typing-1.4.3-macos-source.zip',
                          'feature_source_commit': WINDOWS_143_FEATURE_COMMIT},
}
HOST_TAG = 'v4.0.0-typing.8'
HOST_COMMIT = '2c64325cbc14e2699aa0e3ea6f6d71599a25383e'
RELEASE_BASE = ('https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/'
                'releases/download/' + HOST_TAG + '/')
HOST_ASSETS = {
    'DictationAssistant-4.0.0-typing.8-macos-arm64.zip': 'd44ccb790f175c0695fa28c596eeaed07adeb2904df2ea4d3c4ef2f99587e64d',
    'DictationAssistant-4.0.0-typing.8-macos-x64.zip': '42c26fac9fb9127803acd069aa59a5bc5ac8224798f4168abb537f4232652df6',
    'DictationAssistant-4.0.0-typing.8-macos-source.zip': '7d84de6c8efa322cda00e364b66e4d0a73874488f229e1e18389a1202dfc2e38',
    'RELEASE_NOTES.md': '0df33e28ba912b913f3aa455527266c578a7c98813c3ffd12befdbd339229936',
    'RELEASE_REVIEW.json': 'f6f75108794e9db3e44337c25c2e2e50e8bf319fc3f08223bf1112e4f9b4f344',
}
# This is the published checksum file, not a checksum supplied by the network.
HOST_SUMS_SHA256 = 'e19ffb27d14b68da8c67b1b9fc1ce6809f1e31ad1ef6ce020809c3e2b2f47d9f'
APP = 'DictationAssistant.app'
RUNTIME = APP + '/Contents/Resources/runtime'
PLUGIN = 'Contents/Resources/runtime/Plugins/SynchronousTyping/'
LAUNCHER = 'Contents/MacOS/DictationAssistant.App'
SEAL = 'Contents/_CodeSignature/CodeResources'
PLUGIN_FILES = {'DictationAssistant.Practices.Typing.dll', 'plugin.json',
                'DictationAssistant.Practices.Typing.deps.json',
                'DictationAssistant.Practices.Typing.runtimeconfig.json',
                'System.Security.Cryptography.ProtectedData.dll'}
MANIFESTS = ('THIRD_PARTY_COMPONENTS.json', 'THIRD_PARTY_SOURCES.json')
MACOS_143_CONFIGURATION_CHECKS = (
    'real_macos_runner', 'real_native_architecture_runner',
    'actual_published_plugin_matches_source', 'session_storage_only',
    'initial_deleted_load_is_empty', 'save_load_round_trip',
    'configuration_display_redacts_fields', 'http_endpoint_rejected',
    'invalid_save_preserves_previous', 'cancelled_save_rejected',
    'cancelled_save_preserves_previous', 'delete_removes_configuration',
    'no_persistent_configuration_file_created', 'new_process_does_not_retain_session',
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def patch_config(plugin_release_tag=DEFAULT_RELEASE_TAG):
    require(plugin_release_tag in PATCH_RELEASES, 'Unknown Mac typing patch release tag')
    return {'release_tag': plugin_release_tag, **PATCH_RELEASES[plugin_release_tag]}


def document(path):
    return json.loads(path.read_text(encoding='utf-8'))


def verify_answer_model_observation(observation, rid, commit, plugin_checksum):
    require(isinstance(observation, dict)
            and observation.get('status') == 'passed'
            and observation.get('plugin_version') == '1.4.3'
            and observation.get('runtime_identifier') == rid
            and observation.get('source_commit') == commit
            and observation.get('plugin_dll_sha256') == plugin_checksum
            and observation.get('network_requests_performed') is False
            and observation.get('live_provider_credentials_tested') is False
            and isinstance(observation.get('checks'), dict)
            and all(observation['checks'].get(key) is True for key in MACOS_143_CONFIGURATION_CHECKS),
            'The Mac model-configuration observation does not match the actual plugin and native runner')


def native_runner(rid):
    require(sys.platform == 'darwin', 'The Mac patch requires a real macOS runner')
    require(os.uname().machine == ('x86_64' if rid == 'osx-x64' else 'arm64'),
            'Use the native runner matching the patch architecture')


def fetch(name, checksum, cache):
    path = cache / name
    if path.is_file() and mac.digest(path) == checksum:
        return path
    temporary = path.with_name(path.name + '.download')
    request = urllib.request.Request(RELEASE_BASE + name, headers={'User-Agent': 'DictationAssistant-patch'})
    with urllib.request.urlopen(request, timeout=180) as source, temporary.open('wb') as target:
        shutil.copyfileobj(source, target, 1024 * 1024)
    require(mac.digest(temporary) == checksum, 'The fixed published R8 asset checksum failed: ' + name)
    temporary.replace(path)
    return path


def check_archive(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and archive.testzip() is None,
                'The fixed R8 archive is corrupt or has duplicate paths')
        for name in names:
            parts = pathlib.PurePosixPath(name)
            require(not parts.is_absolute() and '..' not in parts.parts and '\\' not in name,
                    'The R8 archive contains an unsafe path')
            require(parts.parts and parts.parts[0] == 'payload', 'Unexpected R8 archive root')
            item = archive.getinfo(name)
            require(stat.S_IFMT(item.external_attr >> 16) != stat.S_IFLNK,
                    'The pinned R8 app unexpectedly contains a symbolic link')
        inventory = json.loads(archive.read('payload/BUNDLE_FILES.json'))
        actual = {name.removeprefix('payload/' + APP + '/') for name in names
                  if name.startswith('payload/' + APP + '/') and not name.endswith('/')}
        require(len(inventory) == 337 and actual == set(inventory), 'Unexpected R8 app file inventory')
        for name, record in inventory.items():
            require(set(record) == {'sha256', 'bytes'}, 'Unexpected R8 inventory record')
            data = archive.read('payload/' + APP + '/' + name)
            require(len(data) == record['bytes'] and hashlib.sha256(data).hexdigest() == record['sha256'],
                    'The pinned R8 app file differs from its inventory: ' + name)
    return inventory


def source_scope(head, plugin_release_tag=DEFAULT_RELEASE_TAG):
    config = patch_config(plugin_release_tag)
    require(re.fullmatch('[0-9a-f]{40}', head), 'The patch source must be an exact Git commit')
    require(mac.git('rev-parse', HOST_TAG + '^{commit}') == HOST_COMMIT, 'The published R8 tag moved')
    # The plugin is built from this commit; the existing application is not.
    mac.run('git', 'diff', '--quiet', HOST_COMMIT, head, '--',
            'src/DictationAssistant.App', 'src/DictationAssistant.Plugin.Abstractions',
            'plugins/DictationAssistant.Importers.Docx', 'deploy/macos', cwd=ROOT)
    require(not mac.git('status', '--porcelain'), 'Commit the patch source before building')
    if config.get('feature_source_commit'):
        mac.run('git', 'merge-base', '--is-ancestor', config['feature_source_commit'], head, cwd=ROOT)


def source_references(head, plugin_release_tag=DEFAULT_RELEASE_TAG):
    config = patch_config(plugin_release_tag)
    version = config['version']
    name = 'DictationAssistant-4.0.0-typing.8-macos-source.zip'
    references = {'schema_version': 1, 'host_release_tag': HOST_TAG, 'host_source_commit': HOST_COMMIT,
            'host_corresponding_source': {'name': name, 'url': RELEASE_BASE + name, 'sha256': HOST_ASSETS[name]},
            'plugin_source_commit': head, 'plugin_source_asset': config['source_asset'],
            'source_delivery': f'The unchanged R8 host source and this update plugin source together provide the corresponding source. The R8 archive is not relabeled as typing {version}.',
            'build_steps': [
                'Obtain the fixed R8 source archive above and verify its SHA-256. It includes the host, native corresponding-source archives, original notices and exact R8 build records.',
                f'Obtain the companion typing {version} source asset from this plugin release; its plugin sources and release script correspond to plugin_source_commit.',
                'For a source checkout, use the exact plugin Git tag, retain the fixed R8 public binary as the host, and run this patch script --build then --validate on the matching Mac architecture. This script publishes only the plugin with its committed RID lock file and does not rebuild the host.',
                'For source-only plugin compilation use the companion PLUGIN_BUILD.md instructions. Original R8 host and native source build instructions remain in the fixed R8 source archive.'],
            'apple_notarized': False, 'unresolved_license_issue': mac.KNOWN_ISSUE}
    if plugin_release_tag != DEFAULT_RELEASE_TAG:
        references.update({'plugin_release_tag': plugin_release_tag,
                           'feature_source_commit': config['feature_source_commit']})
        references['build_steps'][2] = references['build_steps'][2].replace(
            '--build then --validate on the matching Mac architecture.',
            f'--build then --validate on the matching Mac architecture, passing --plugin-release-tag {plugin_release_tag} to both commands.')
    return references


def build(args):
    plugin_release_tag = getattr(args, 'plugin_release_tag', DEFAULT_RELEASE_TAG)
    config = patch_config(plugin_release_tag)
    version = config['version']
    native_runner(args.rid)
    head = mac.git('rev-parse', 'HEAD')
    source_scope(head, plugin_release_tag)
    output = args.output_dir.resolve()
    require(output != ROOT / 'artifacts' and output.is_relative_to(ROOT / 'artifacts'),
            'Patch output must be a dedicated subdirectory inside artifacts/')
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    cache = ROOT / 'artifacts/macos-typing-patch-cache'
    cache.mkdir(parents=True, exist_ok=True)
    sums = fetch('SHA256SUMS', HOST_SUMS_SHA256, cache)
    entries = dict((line.split('  ', 1)[1], line.split('  ', 1)[0])
                   for line in sums.read_text(encoding='ascii').splitlines())
    require(entries == HOST_ASSETS, 'The fixed R8 checksum file has unexpected contents')
    name = f'DictationAssistant-4.0.0-typing.8-macos-{args.rid.removeprefix("osx-")}.zip'
    original = fetch(name, HOST_ASSETS[name], cache)
    inventory = check_archive(original)
    mac.run('ditto', '-x', '-k', original, output)
    payload = output / 'payload'
    app = payload / APP
    runtime = payload / RUNTIME
    mac.verify_files(app, inventory)
    signature_log = output / 'codesign-diagnostics.log'
    mac.run('codesign', '--verify', '--deep', '--strict', '--verbose=4', app,
            diagnostic_log=signature_log, stderr=subprocess.STDOUT)
    info = document(payload / 'BUNDLE_INFO.json')
    components = document(payload / MANIFESTS[0])
    sources = document(payload / MANIFESTS[1])
    require(info.get('source_commit') == HOST_COMMIT and info.get('runtime_identifier') == args.rid
            and info.get('application_version') == '4.0.0-typing.8'
            and info.get('binary_inventory_base') == RUNTIME and not info.get('review_only')
            and info.get('bundled_plugins') == {'synchronous-typing': '1.4.1', 'docx-vocabulary': '1.2.0'},
            'The downloaded app is not the expected complete R8 host')
    mac.check_review(sources)
    require(components.get('catalog_sha256') == mac.digest(ROOT / 'third-party/components.json'),
            'The patch unexpectedly changes the host third-party component catalog')
    expected_mark = ('4.0.0-typing.8+' + HOST_COMMIT).encode()
    require(expected_mark in (runtime / 'DictationAssistant.App.dll').read_bytes(), 'The R8 host build marker differs')
    original_plugin = runtime / 'Plugins/SynchronousTyping'
    require({p.name for p in original_plugin.iterdir()} == PLUGIN_FILES, 'Unexpected original plugin files')
    require(document(original_plugin / 'plugin.json').get('version') == '1.4.1'
            and document(runtime / 'Plugins/DocxVocabulary/plugin.json').get('version') == '1.2.0',
            'The original plugin versions differ')
    records = payload / 'HOST_R8_RECORDS'
    records.mkdir()
    for filename in ('BUNDLE_INFO.json', 'BUNDLE_FILES.json', *MANIFESTS, 'RELEASE_REVIEW.json',
                     'RELEASE_NOTES.md', 'README-macOS.txt'):
        shutil.copyfile(payload / filename, records / filename)

    project = ROOT / 'plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj'
    new_plugin = output / 'published-plugin'
    common = (f'-p:RuntimeIdentifier={args.rid}', '-p:SelfContained=false')
    restore = mac.run('dotnet', 'restore', project, '-r', args.rid, '--locked-mode', *common,
                      '-p:RestorePackagesWithLockFile=true', f'-p:NuGetLockFilePath=packages.{args.rid}.lock.json', cwd=ROOT)
    publish = mac.run('dotnet', 'publish', project, '-c', 'Release', '-r', args.rid, '--self-contained', 'false',
                      '--no-restore', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', new_plugin, cwd=ROOT)
    (output / 'plugin-build.log').write_text(restore + publish, encoding='utf-8')
    require({p.name for p in new_plugin.iterdir()} == PLUGIN_FILES and all(p.is_file() and not p.is_symlink() for p in new_plugin.iterdir()),
            'The new plugin must contain exactly its existing five files')
    manifest = document(new_plugin / 'plugin.json')
    require(manifest.get('id') == 'synchronous-typing' and manifest.get('version') == version
            and manifest.get('apiVersion') == 1 and manifest.get('minimumHostFeatureLevel') == 5,
            'The new plugin version or host contract differs')
    require((version + '+' + head).encode() in (new_plugin / 'DictationAssistant.Practices.Typing.dll').read_bytes(),
            'The plugin build does not match its exact source commit')
    protected = 'System.Security.Cryptography.ProtectedData.dll'
    require(mac.digest(new_plugin / protected) == mac.digest(original_plugin / protected),
            'The plugin patch unexpectedly changes its private ProtectedData dependency')
    from prepare_plugin_release import plugin_dependencies
    plugin_dependencies(new_plugin, 'DictationAssistant.Practices.Typing.dll', version,
                        document(ROOT / 'third-party/components.json'))
    for filename in PLUGIN_FILES:
        shutil.copyfile(new_plugin / filename, original_plugin / filename)

    mac.run('codesign', '--force', '--sign', '-', '--options', 'runtime', '--verbose=4', '--entitlements',
            ROOT / 'deploy/macos/entitlements.plist', app, diagnostic_log=signature_log, stderr=subprocess.STDOUT)
    mac.run('codesign', '--verify', '--deep', '--strict', '--verbose=4', app,
            diagnostic_log=signature_log, stderr=subprocess.STDOUT)
    final = mac.file_inventory(app)
    require(set(final) == set(inventory), 'The patch added or removed an application file')
    allowed = {PLUGIN + filename for filename in PLUGIN_FILES - {protected}} | {LAUNCHER, SEAL}
    immutable = {path: record['sha256'] for path, record in inventory.items() if path not in allowed}
    require(all(final[path] == inventory[path] for path in immutable), 'A host, importer, native or original notice file changed')
    changes = [{'path': path, 'original_sha256': inventory[path]['sha256'], 'sha256': final[path]['sha256'],
                'reason': 'Updated typing plugin' if path.startswith(PLUGIN) else 'Outer app ad-hoc resource signature'}
               for path in sorted(final) if final[path] != inventory[path]]
    require(all(change['path'] in allowed for change in changes), 'An unapproved application file changed')
    native_paths = [path for path in inventory if path.endswith('.dylib') or path == 'Contents/Resources/runtime/DictationAssistant.App']
    require(all(path in immutable and final[path] == inventory[path] for path in native_paths),
            'The runtime apphost or a native dependency changed')
    baseline = {'schema_version': 1, 'runtime_identifier': args.rid, 'host_release_tag': HOST_TAG,
                'host_source_commit': HOST_COMMIT, 'host_asset': {'name': name, 'url': RELEASE_BASE + name, 'sha256': HOST_ASSETS[name]},
                'plugin_source_commit': head, 'plugin_version': version, 'original_app_file_count': len(inventory),
                'immutable_files': immutable, 'immutable_file_count': len(immutable), 'allowed_change_paths': sorted(allowed),
                'changes': changes, 'native_and_runtime_apphost_bytes_unchanged': True,
                'native_and_runtime_apphost_paths': native_paths, 'host_not_rebuilt': True,
                'baseline_signature_verified': True, 'final_deep_strict_signature_verified': True}
    old_identity = 'DictationAssistant.Practices.Typing/1.4.1'
    new_identity = 'DictationAssistant.Practices.Typing/' + version
    require(components['project_components'].count(old_identity) == 1, 'Missing original project identity')
    components['project_components'] = [new_identity if identity == old_identity else identity for identity in components['project_components']]
    own = [item for item in components['files'] if item['path'] == 'Plugins/SynchronousTyping/DictationAssistant.Practices.Typing.dll']
    require(len(own) == 1 and own[0].get('components') == [old_identity], 'Missing exact original plugin DLL component')
    own[0].update({'sha256': mac.digest(original_plugin / 'DictationAssistant.Practices.Typing.dll'), 'components': [new_identity]})
    for item in components['files']:
        actual = mac.digest(runtime / item['path'])
        require(actual == item['sha256'], 'A component file differs from the patched inventory: ' + item['path'])
    info['host_release_tag'] = HOST_TAG
    info['host_source_commit'] = HOST_COMMIT
    info['plugin_source_commit'] = head
    info['patch_source_commit'] = head
    info['bundled_plugins']['synchronous-typing'] = version
    info['host_not_rebuilt'] = True
    info['host_original_signing'] = copy.deepcopy(info['signing'])
    info['signing'] = {'type': 'ad-hoc', 'apple_notarized': False, 'deep_strict_verification_passed': True,
                       'native_dependency_bytes_unchanged': True, 'other_native_dependency_bytes_unchanged': True,
                       'native_signing_adjustments': [], 'bundle_signature_change_paths': [LAUNCHER, SEAL]}
    info['launcher']['sha256'] = mac.digest(app / LAUNCHER)
    lock = project.parent / f'packages.{args.rid}.lock.json'
    info['plugin_nuget'] = {'locked_mode': True, 'runtime_identifier': args.rid,
                            'lock_file': {'path': lock.relative_to(ROOT).as_posix(), 'sha256': mac.digest(lock)},
                            'source_commit': head}
    references = source_references(head, plugin_release_tag)
    patch_review = {'schema_version': 1, 'publication_scope': 'updated-plugin-fixed-published-macos-host',
                    'host_release_tag': HOST_TAG, 'host_source_commit': HOST_COMMIT,
                    'plugin_id': 'synchronous-typing', 'plugin_version': version, 'plugin_source_commit': head,
                    'host_not_rebuilt': True, 'legal_clearance_claimed': False,
                    'host_release_review': sources['release_review'], 'signing': info['signing'],
                    'source_references': references}
    if plugin_release_tag != DEFAULT_RELEASE_TAG:
        for record in (baseline, info, patch_review):
            record.update({'plugin_release_tag': plugin_release_tag,
                           'feature_source_commit': config['feature_source_commit']})
    for filename in mac.PROJECT_NOTICES:
        target = payload / 'PLUGIN_PATCH_NOTICES' / filename
        target.parent.mkdir(exist_ok=True)
        shutil.copyfile(ROOT / filename, target)
    features = ('准备页勾选“打乱顺序”，即可随机排列整对提示与答案；静音和播报模式都可使用。\n'
                if version == '1.4.2' else
                '练习前可手动补充中文同义答案，或生成候选后逐条确认保存；练习和结算在本地判分。\n'
                '模型配置在“插件管理 → 背诵练习 → 配置”中填写，只用于本次运行，退出后不保留。\n'
                '已确认的答案会在本地保存；英文和化学式保留原规则，随机顺序与中途结算继续可用。\n')
    (payload / 'README-macOS.txt').write_text(
        f'背诵练习 {version} · macOS 整合补丁\n\n'
        '本包使用原 R8 本体，只更新背诵练习插件；Word / Excel 导入仍为 1.2.0。\n'
        + features +
        '需要 macOS 15+，Intel 选择 x64，M 系列选择 arm64。退出程序后，用本包的\n'
        'DictationAssistant.app 替换“应用程序”中的旧版，不要单独修改应用包内的插件。\n'
        '采用 ad-hoc 签名，未经过 Apple 公证；首次打开被拦截时，核对来源后从\n'
        '“系统设置 → 隐私与安全性”允许打开。原有用户数据保留在用户目录。\n'
        'R8 原始记录保存在 HOST_R8_RECORDS；本补丁的改动与完整源码入口见\n'
        'PATCH_BASELINE.json、HOST_SOURCE_REFERENCES.json 和配套插件源码。\n'
        'Avalonia/MS-PL 与 AGPL 的组合授权事项仍未解决，原始许可和声明随包保留。\n', encoding='utf-8')
    notes_features = ('准备页新增“打乱顺序”，静音背诵与播报后作答都可随机排列，提示与答案保持配对。\n'
                      if version == '1.4.2' else
                      '中文答案可提前补充同义写法，或通过模型生成候选，逐条确认保存后离线判分。\n'
                      '模型接口和密钥在本机配置，仅本次运行使用；保存配置和练习结算不会请求服务。\n'
                      '英文、化学式和未确认的候选不会被自动放宽，随机顺序与中途结算继续可用。\n')
    (payload / 'RELEASE_NOTES.md').write_text(
        f'# 背诵练习 {version} · macOS 整合补丁\n\n'
        + notes_features +
        '使用固定的 R8 本体，Word / Excel 导入仍为 1.2.0；宿主和原生库没有重新构建。\n'
        '退出程序后替换整个应用包。源码入口与校验信息见 HOST_SOURCE_REFERENCES.json，'
        '改动清单见 PATCH_BASELINE.json。\n\n'
        '本版采用 ad-hoc 签名，未经过 Apple 公证。Avalonia 组合授权事项继续披露，详见 RELEASE_REVIEW.json。\n',
        encoding='utf-8')
    for filename, value in (('BUNDLE_INFO.json', info), ('BUNDLE_FILES.json', final),
                            (MANIFESTS[0], components), ('PATCH_BASELINE.json', baseline),
                            ('HOST_SOURCE_REFERENCES.json', references), ('RELEASE_REVIEW.json', patch_review)):
        mac.write_json(payload / filename, value)
    for filename in ('BUNDLE_INFO.json', *MANIFESTS, 'PATCH_BASELINE.json', 'HOST_SOURCE_REFERENCES.json'):
        shutil.copyfile(payload / filename, output / filename)
    print('Patched only typing files and the outer app signature; immutable host, importer and native bytes verified.')


def validate(args):
    plugin_release_tag = getattr(args, 'plugin_release_tag', DEFAULT_RELEASE_TAG)
    config = patch_config(plugin_release_tag)
    version = config['version']
    native_runner(args.rid)
    output = args.output_dir.resolve()
    baseline = document(output / 'PATCH_BASELINE.json')
    info = document(output / 'BUNDLE_INFO.json')
    head = mac.git('rev-parse', 'HEAD')
    require(baseline.get('plugin_source_commit') == head == info.get('plugin_source_commit')
            and baseline.get('host_source_commit') == HOST_COMMIT == info.get('host_source_commit')
            and baseline.get('runtime_identifier') == args.rid == info.get('runtime_identifier')
            and baseline.get('plugin_version') == version
            and info.get('bundled_plugins', {}).get('synchronous-typing') == version,
            'The patch validation source does not match its host and plugin records')
    previous = mac.PLUGINS
    try:
        mac.PLUGINS = {'SynchronousTyping': ('synchronous-typing', version),
                       'DocxVocabulary': ('docx-vocabulary', '1.2.0')}
        mac.validate(args)
    finally:
        mac.PLUGINS = previous
    validation = document(output / 'BUILD_VALIDATION.json')
    validation.update({'host_release_tag': HOST_TAG, 'host_source_commit': HOST_COMMIT,
                       'plugin_source_commit': head, 'plugin_version': version,
                       'host_not_rebuilt': True, 'immutable_host_files_verified': True,
                       'native_and_runtime_apphost_bytes_unchanged': True})
    app = output / 'payload' / APP
    if plugin_release_tag != DEFAULT_RELEASE_TAG:
        observation_path = output / 'ANSWER_MODEL_OBSERVATION.json'
        observation = document(observation_path)
        verify_answer_model_observation(observation, args.rid, head,
                                        mac.digest(app / PLUGIN / 'DictationAssistant.Practices.Typing.dll'))
        require(baseline.get('plugin_release_tag') == plugin_release_tag == info.get('plugin_release_tag')
                and baseline.get('feature_source_commit') == config['feature_source_commit']
                == info.get('feature_source_commit'), 'The Mac patch release target differs')
        validation.update({'plugin_release_tag': plugin_release_tag,
                           'feature_source_commit': config['feature_source_commit'],
                           'answer_model_configuration_observation': observation})
        shutil.copyfile(observation_path, output / 'payload/ANSWER_MODEL_OBSERVATION.json')
    final = mac.file_inventory(app)
    require(all(final.get(path, {}).get('sha256') == checksum for path, checksum in baseline['immutable_files'].items()),
            'An immutable original host file changed during runtime validation')
    mac.write_json(output / 'BUILD_VALIDATION.json', validation)
    mac.write_json(output / 'payload/BUILD_VALIDATION.json', validation)
    archive = output / f'DictationAssistant-R8-typing-{version}-macos-{args.rid.removeprefix("osx-")}.zip'
    mac.run('ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', output / 'payload', archive)
    with zipfile.ZipFile(archive) as package:
        require(package.testzip() is None and len(package.namelist()) == len(set(package.namelist())),
                'The final patch archive is corrupt or has duplicate paths')
        for path, record in final.items():
            data = package.read('payload/' + APP + '/' + path)
            require(hashlib.sha256(data).hexdigest() == record['sha256'], 'ZIP changed a sealed app file: ' + path)
        for path in (LAUNCHER, 'Contents/Resources/runtime/DictationAssistant.App'):
            require(package.getinfo('payload/' + APP + '/' + path).external_attr >> 16 & stat.S_IXUSR,
                    'The Mac patch lost an executable mode')
    print(archive)


def verify_part(part, rid, commit, plugin_release_tag=DEFAULT_RELEASE_TAG):
    """Offline integrity checks; signature execution is evidenced by Mac records."""
    config = patch_config(plugin_release_tag)
    version = config['version']
    part = pathlib.Path(part)
    info = document(part / 'BUNDLE_INFO.json')
    baseline = document(part / 'PATCH_BASELINE.json')
    validation = document(part / 'BUILD_VALIDATION.json')
    references = document(part / 'HOST_SOURCE_REFERENCES.json')
    expected_plugins = {'synchronous-typing': version, 'docx-vocabulary': '1.2.0'}
    require(rid in ('osx-x64', 'osx-arm64') and re.fullmatch('[0-9a-f]{40}', commit),
            'Invalid Mac patch verification target')
    require(info.get('source_commit') == HOST_COMMIT == info.get('host_source_commit')
            and info.get('host_release_tag') == HOST_TAG and info.get('plugin_source_commit') == commit
            and info.get('patch_source_commit') == commit and info.get('runtime_identifier') == rid
            and info.get('application_version') == '4.0.0-typing.8' and info.get('host_not_rebuilt') is True
            and info.get('bundled_plugins') == expected_plugins and info.get('binary_inventory_base') == RUNTIME,
            'Mac patch application/host/plugin source metadata differs')
    require(info.get('signing', {}).get('deep_strict_verification_passed') is True
            and info['signing'].get('type') == 'ad-hoc' and info['signing'].get('apple_notarized') is False
            and info['signing'].get('native_dependency_bytes_unchanged') is True,
            'Mac patch signature review is incomplete')
    required_validation = ('plugin_discovery', 'existing_typing_checks', 'main_window_opened',
                           'main_window_closed_normally', 'mac_say_synthesis', 'sdl_dummy_playback',
                           'host_not_rebuilt', 'immutable_host_files_verified',
                           'native_and_runtime_apphost_bytes_unchanged')
    require(validation.get('source_commit') == commit == validation.get('plugin_source_commit')
            and validation.get('runtime_identifier') == rid and validation.get('host_source_commit') == HOST_COMMIT
            and validation.get('plugin_version') == version
            and all(validation.get(key) is True for key in required_validation),
            'The Mac patch lacks its matching real-runner validations')
    require(references == source_references(commit, plugin_release_tag), 'The Mac patch corresponding-source references differ')
    host_name = f'DictationAssistant-4.0.0-typing.8-macos-{rid.removeprefix("osx-")}.zip'
    require(baseline.get('host_asset') == {'name': host_name, 'url': RELEASE_BASE + host_name, 'sha256': HOST_ASSETS[host_name]}
            and baseline.get('host_source_commit') == HOST_COMMIT and baseline.get('host_release_tag') == HOST_TAG
            and baseline.get('plugin_source_commit') == commit and baseline.get('plugin_version') == version
            and baseline.get('runtime_identifier') == rid and baseline.get('host_not_rebuilt') is True
            and baseline.get('baseline_signature_verified') is True
            and baseline.get('final_deep_strict_signature_verified') is True
            and baseline.get('native_and_runtime_apphost_bytes_unchanged') is True,
            'The Mac patch baseline is not the fixed R8 app')
    binary = part / f'DictationAssistant-R8-typing-{version}-macos-{rid.removeprefix("osx-")}.zip'
    with zipfile.ZipFile(binary) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and archive.testzip() is None, 'Mac patch ZIP is corrupt or duplicated')
        for name in names:
            path = pathlib.PurePosixPath(name)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in name
                    and path.parts and path.parts[0] == 'payload', 'Unsafe Mac patch ZIP path')
            require(stat.S_IFMT(archive.getinfo(name).external_attr >> 16) != stat.S_IFLNK,
                    'Unexpected symbolic link in the Mac patch')
        prefix = 'payload/' + APP + '/'
        final = json.loads(archive.read('payload/BUNDLE_FILES.json'))
        original = json.loads(archive.read('payload/HOST_R8_RECORDS/BUNDLE_FILES.json'))
        require(len(final) == len(original) == baseline.get('original_app_file_count') == 337
                and set(final) == set(original)
                and {name.removeprefix(prefix) for name in names if name.startswith(prefix) and not name.endswith('/')} == set(final),
                'The Mac patch has missing or unlisted app files')
        for path, record in final.items():
            require(set(record) == {'sha256', 'bytes'}, 'Unexpected Mac patch file inventory record')
            data = archive.read(prefix + path)
            require(len(data) == record['bytes'] and hashlib.sha256(data).hexdigest() == record['sha256'],
                    'The Mac patch ZIP differs from its sealed inventory: ' + path)
        allowed = {PLUGIN + filename for filename in PLUGIN_FILES - {'System.Security.Cryptography.ProtectedData.dll'}} | {LAUNCHER, SEAL}
        immutable = {path: record['sha256'] for path, record in original.items() if path not in allowed}
        require(baseline.get('allowed_change_paths') == sorted(allowed)
                and baseline.get('immutable_files') == immutable
                and baseline.get('immutable_file_count') == len(immutable)
                and all(final[path] == original[path] for path in immutable),
                'The Mac patch changed immutable host/native/importer/notice bytes')
        expected_changes = [{'path': path, 'original_sha256': original[path]['sha256'], 'sha256': final[path]['sha256'],
                             'reason': 'Updated typing plugin' if path.startswith(PLUGIN) else 'Outer app ad-hoc resource signature'}
                            for path in sorted(final) if final[path] != original[path]]
        require(baseline.get('changes') == expected_changes and all(item['path'] in allowed for item in expected_changes),
                'The Mac patch difference record does not describe the actual changed files')
        expected_native = [path for path in original if path.endswith('.dylib')
                           or path == 'Contents/Resources/runtime/DictationAssistant.App']
        require(baseline.get('native_and_runtime_apphost_paths') == expected_native
                and all(path in immutable for path in expected_native),
                'The native/apphost unchanged-byte record is incomplete')
        for path in (LAUNCHER, 'Contents/Resources/runtime/DictationAssistant.App'):
            require(archive.getinfo(prefix + path).external_attr >> 16 & stat.S_IXUSR,
                    'The Mac patch ZIP lost an executable mode')
        for filename in ('BUNDLE_INFO.json', *MANIFESTS, 'PATCH_BASELINE.json',
                         'HOST_SOURCE_REFERENCES.json', 'BUILD_VALIDATION.json'):
            require(archive.read('payload/' + filename) == (part / filename).read_bytes(),
                    'External and packaged Mac patch records differ: ' + filename)
        if plugin_release_tag != DEFAULT_RELEASE_TAG:
            observation_path = part / 'ANSWER_MODEL_OBSERVATION.json'
            observation = document(observation_path)
            verify_answer_model_observation(observation, rid, commit,
                hashlib.sha256(archive.read(prefix + PLUGIN + 'DictationAssistant.Practices.Typing.dll')).hexdigest())
            require(archive.read('payload/ANSWER_MODEL_OBSERVATION.json') == observation_path.read_bytes()
                    and validation.get('answer_model_configuration_observation') == observation,
                    'External, packaged and validation Mac model-configuration observations differ')
            for record in (info, baseline, validation):
                require(record.get('plugin_release_tag') == plugin_release_tag
                        and record.get('feature_source_commit') == config['feature_source_commit'],
                        'A Mac patch record refers to another release target')
        components = document(part / MANIFESTS[0])
        sources = document(part / MANIFESTS[1])
        mac.check_review(sources)
        require(archive.read('payload/' + MANIFESTS[1]) == archive.read('payload/HOST_R8_RECORDS/' + MANIFESTS[1]),
                'The unchanged host third-party source record was altered')
        review = json.loads(archive.read('payload/RELEASE_REVIEW.json'))
        require(review.get('publication_scope') == 'updated-plugin-fixed-published-macos-host'
                and review.get('host_source_commit') == HOST_COMMIT and review.get('host_release_tag') == HOST_TAG
                and review.get('plugin_id') == 'synchronous-typing' and review.get('plugin_version') == version
                and review.get('plugin_source_commit') == commit and review.get('host_not_rebuilt') is True
                and review.get('legal_clearance_claimed') is False
                and review.get('host_release_review') == sources.get('release_review')
                and review.get('source_references') == references,
                'The Mac patch top-level disclosure retains an old or incorrect release scope')
        if plugin_release_tag != DEFAULT_RELEASE_TAG:
            require(review.get('plugin_release_tag') == plugin_release_tag
                    and review.get('feature_source_commit') == config['feature_source_commit'],
                    'The Mac patch top-level review refers to another release target')
        require(('背诵练习 ' + version).encode() in archive.read('payload/RELEASE_NOTES.md'),
                'The Mac patch top-level notes retain an old plugin version')
        new_identity = 'DictationAssistant.Practices.Typing/' + version
        require(new_identity in components.get('project_components', [])
                and 'DictationAssistant.Practices.Typing/1.4.1' not in components['project_components'],
                'The Mac patch component inventory retains an old plugin version')
        expected_components = json.loads(archive.read('payload/HOST_R8_RECORDS/' + MANIFESTS[0]))
        old_identity = 'DictationAssistant.Practices.Typing/1.4.1'
        expected_components['project_components'] = [new_identity if item == old_identity else item
                                                     for item in expected_components['project_components']]
        for item in expected_components['files']:
            if item['path'] == 'Plugins/SynchronousTyping/DictationAssistant.Practices.Typing.dll':
                item.update({'sha256': final[PLUGIN + 'DictationAssistant.Practices.Typing.dll']['sha256'],
                             'components': [new_identity]})
        require(components == expected_components, 'Unexpected host third-party component metadata change')
        for item in components['files']:
            require(hashlib.sha256(archive.read('payload/' + RUNTIME + '/' + item['path'])).hexdigest() == item['sha256'],
                    'The Mac patch binary differs from its component inventory: ' + item['path'])
        manifest = json.loads(archive.read(prefix + PLUGIN + 'plugin.json'))
        require(manifest.get('id') == 'synchronous-typing' and manifest.get('version') == version
                and manifest.get('apiVersion') == 1 and manifest.get('minimumHostFeatureLevel') == 5,
                'The Mac patch contains an incompatible plugin manifest')
        require((version + '+' + commit).encode() in archive.read(prefix + PLUGIN + 'DictationAssistant.Practices.Typing.dll')
                and ('4.0.0-typing.8+' + HOST_COMMIT).encode() in archive.read('payload/' + RUNTIME + '/DictationAssistant.App.dll'),
                'The Mac patch DLL source markers differ')
        require(info.get('launcher', {}).get('sha256') == final[LAUNCHER]['sha256'], 'The Mac launcher hash differs')
    result = {'runtime_identifier': rid, 'host_release_tag': HOST_TAG, 'host_source_commit': HOST_COMMIT,
            'plugin_source_commit': commit, 'plugin_version': version, 'binary_asset': binary.name,
            'binary_sha256': mac.digest(binary), 'host_not_rebuilt': True,
            'immutable_host_files_verified': True, 'immutable_file_count': len(immutable),
            'native_and_runtime_apphost_bytes_unchanged': True,
            'recorded_real_macos_validation_verified': True, 'validation': validation,
            'host_source_references': references, 'changes': expected_changes}
    if plugin_release_tag != DEFAULT_RELEASE_TAG:
        result.update({'plugin_release_tag': plugin_release_tag,
                       'feature_source_commit': config['feature_source_commit'],
                       'answer_model_configuration_observation': observation})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--build', action='store_true')
    mode.add_argument('--validate', action='store_true')
    parser.add_argument('--rid', required=True, choices=('osx-x64', 'osx-arm64'))
    parser.add_argument('--output-dir', required=True, type=pathlib.Path)
    parser.add_argument('--plugin-release-tag', default=DEFAULT_RELEASE_TAG, choices=tuple(PATCH_RELEASES))
    args = parser.parse_args()
    try:
        (build if args.build else validate)(args)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print('macOS typing patch failed: ' + str(error), file=sys.stderr)
        raise SystemExit(1)


if __name__ == '__main__':
    main()
