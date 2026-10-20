"""Prepare independent sideloaded plugin releases from the verified Windows build.

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
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

from collect_third_party_sources import collect
from prepare_licenses import asset_path, copy_notices, normalize_archive, source_archives


ROOT = pathlib.Path(__file__).resolve().parents[1]
PLUGINS = (
    ('synchronous-typing', 'DictationAssistant.Practices.Typing', 'SynchronousTyping'),
    ('docx-vocabulary', 'DictationAssistant.Importers.Docx', 'DocxVocabulary'),
)
KNOWN_REVIEW_ISSUES = {'avalonia-mspl-agpl-compatibility'}
PROJECT_NOTICES = ('LICENSE.md', 'NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md')
HOST_RELEASE_TAG = 'v4.0.0-typing.6'
HOST_SOURCE_COMMIT = '0f008c72983b9d567612855498800886201572e6'
STANDALONE_PLUGIN_TAG = 'plugin-synchronous-typing-v1.3.1'
HOST_SUMS_SHA256 = '625f9eb7fd1d7c67b46db4a704d792c3d55d27130431efefb633fb050aa4800d'
HOST_ASSET_SHA256 = {
    'DictationAssistant-4.0.0-typing.6-source.zip': '60754fd733e2a02d07b95f9220637d1c7165f2ba299cabca61a6e47fc1cc5ddc',
    'DictationAssistant-4.0.0-typing.6-win-x64-portable.zip': '961c1e1f835c67e7381e98bd68781f06c87c24edc3f7a90ad78bbb7c8b6ef34c',
    'RELEASE_NOTES.md': '9ec80bf3a1e1e4ede5813905c1e03b41bb19111971132d25181a02a0e2bc13da',
    'RELEASE_REVIEW.json': '26873609737aa9915e322cdcc315fd07cd35bc2f286fa981dc2a17d0e5dc4aa9',
}
HOST_DOCUMENTATION_PATH_ADAPTATIONS = [{
    'original_archive_path': './??????????.md',
    'staged_path': 'R6_PLUGIN_GUIDE.md',
    'bytes': 14711,
    'archive_flag_bits': 8,
    'sha256': '1d83b102d4ba612ebd7e2c21837f259b137cd421509e4ccf537aebfc474b7e28',
}]


def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=ROOT, text=True).strip()


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def project_version(path):
    return ET.parse(path).findtext('.//Version')


def check_zip(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or archive.testzip() is not None:
            raise ValueError(f'Duplicate or corrupt ZIP members: {path.name}')


def verify_git_archive_names(path, head, source_paths):
    # Git writes ZIP names directly from UTF-8 tree paths. Detect any platform
    # encoding damage before publishing source; never guess a lost filename.
    raw = subprocess.check_output(['git', 'ls-tree', '-rz', '--name-only', head, '--', *source_paths], cwd=ROOT)
    expected = {name.decode('utf-8') for name in raw.split(b'\0') if name}
    with zipfile.ZipFile(path) as archive:
        actual = {item.filename for item in archive.infolist() if not item.is_dir()}
        if actual != expected:
            raise ValueError('The source ZIP filenames do not exactly match the UTF-8 Git tree paths')
        if any(not item.filename.isascii() and not item.flag_bits & 0x800 for item in archive.infolist()):
            raise ValueError('A non-ASCII source ZIP filename is missing the UTF-8 ZIP flag')


def host_cache():
    return ROOT / 'artifacts/host-releases' / HOST_RELEASE_TAG


def host_file_hashes(directory):
    result = {}
    for path in directory.rglob('*'):
        if path.is_symlink():
            raise ValueError('The compatibility host must not contain symlinks')
        relative = path.relative_to(directory).as_posix()
        if path.is_file() and not relative.startswith('Plugins/SynchronousTyping/'):
            result[relative] = digest(path)
    return result


def verified_host_metadata():
    cache = host_cache()
    sums = cache / 'SHA256SUMS'
    if digest(sums) != HOST_SUMS_SHA256:
        raise ValueError('The published R6 checksum-file pin does not match')
    recorded = {}
    for line in sums.read_text(encoding='ascii').splitlines():
        value, name = line.split('  ', 1)
        if name in recorded:
            raise ValueError('The R6 checksum file repeats an asset')
        recorded[name] = value
    if recorded != HOST_ASSET_SHA256:
        raise ValueError('The R6 checksum file does not cover the exact four pinned release assets')
    for name in ('RELEASE_REVIEW.json', 'DictationAssistant-4.0.0-typing.6-win-x64-portable.zip'):
        if digest(cache / name) != HOST_ASSET_SHA256[name]:
            raise ValueError(f'The published R6 host asset pin does not match: {name}')
    disclosure = json.loads((cache / 'RELEASE_REVIEW.json').read_text(encoding='utf-8'))
    review = disclosure.get('release_review', {})
    blockers = review.get('blocking_issues')
    if (disclosure.get('tag') != HOST_RELEASE_TAG or disclosure.get('source_commit') != HOST_SOURCE_COMMIT
            or disclosure.get('application_version') != '4.0.0-typing.6'
            or disclosure.get('publication_decision') != 'explicitly_requested_publication_with_declared_unresolved_license_review'
            or disclosure.get('license_review_resolved') is not False
            or set(disclosure.get('acknowledged_issue_ids', [])) != KNOWN_REVIEW_ISSUES
            or review.get('status') != 'blocked' or not isinstance(blockers, list) or not blockers
            or any(not isinstance(item, dict) or item.get('id') not in KNOWN_REVIEW_ISSUES
                   or not isinstance(item.get('reason'), str) or not item['reason'].strip() for item in blockers)):
        raise ValueError('The pinned R6 host disclosure or source commit is inconsistent')
    return disclosure


def prepare_host(tag):
    if tag != HOST_RELEASE_TAG:
        raise ValueError('Only the pinned published R6 host may be used for this standalone release')
    cache = host_cache()
    cache.mkdir(parents=True, exist_ok=True)
    wanted = {'SHA256SUMS': HOST_SUMS_SHA256,
              'RELEASE_REVIEW.json': HOST_ASSET_SHA256['RELEASE_REVIEW.json'],
              'DictationAssistant-4.0.0-typing.6-win-x64-portable.zip':
                  HOST_ASSET_SHA256['DictationAssistant-4.0.0-typing.6-win-x64-portable.zip']}
    for name, expected in wanted.items():
        destination = cache / name
        if destination.is_file() and digest(destination) == expected:
            continue
        temporary = destination.with_suffix(destination.suffix + '.part')
        url = f'https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/download/{tag}/{name}'
        try:
            request = urllib.request.Request(url, headers={'User-Agent': 'DictationAssistant-plugin-release/1'})
            with urllib.request.urlopen(request, timeout=120) as response, temporary.open('wb') as stream:
                if urllib.parse.urlsplit(response.geturl()).scheme != 'https':
                    raise ValueError('The R6 host download redirected outside HTTPS')
                shutil.copyfileobj(response, stream, 1024 * 1024)
            if digest(temporary) != expected:
                raise ValueError(f'The R6 host download failed its published SHA-256 pin: {name}')
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    disclosure = verified_host_metadata()
    archive = cache / 'DictationAssistant-4.0.0-typing.6-win-x64-portable.zip'
    check_zip(archive)
    destination = ROOT / 'artifacts/Windows-x64'
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    adaptations = []
    with zipfile.ZipFile(archive) as package:
        seen = set()
        for member in package.infolist():
            path = pathlib.PurePosixPath(member.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in member.filename:
                raise ValueError('Unsafe member path in the pinned R6 host archive')
            documentation = HOST_DOCUMENTATION_PATH_ADAPTATIONS[0]
            if member.filename == documentation['original_archive_path']:
                content = package.read(member)
                if (member.is_dir() or member.flag_bits != documentation['archive_flag_bits']
                        or len(content) != documentation['bytes']
                        or hashlib.sha256(content).hexdigest() != documentation['sha256']):
                    raise ValueError('The legacy R6 guide does not match its exact member metadata and byte pin')
                # This one already-published ZIP member has literal '?' bytes in
                # its filename. Adapt only its staging path for Windows; retain
                # its exact content and leave the original release untouched.
                path = pathlib.PurePosixPath(documentation['staged_path'])
                adaptations.append(dict(documentation))
            normalized = path.as_posix()
            if normalized == '.':
                continue
            if normalized.casefold() in seen or (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError('Duplicate or symlink member in the pinned R6 host archive')
            seen.add(normalized.casefold())
            target = destination.joinpath(*path.parts)
            if member.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with package.open(member) as source, target.open('wb') as output:
                    shutil.copyfileobj(source, output)
    if adaptations != HOST_DOCUMENTATION_PATH_ADAPTATIONS:
        raise ValueError('The pinned R6 archive did not contain exactly the declared legacy guide adaptation')
    embedded = json.loads((destination / 'RELEASE_REVIEW.json').read_text(encoding='utf-8'))
    if embedded != disclosure or b'4.0.0-typing.6+0f008c72983b9d567612855498800886201572e6' not in (destination / 'DictationAssistant.App.dll').read_bytes():
        raise ValueError('The extracted R6 application does not match the pinned release metadata')
    files = host_file_hashes(destination)
    if len(files) != 385:
        raise ValueError('The staged R6 host must preserve all 385 files outside the typing plugin')
    write_json(cache / 'HOST_BASELINE.json', {
        'host_release_tag': HOST_RELEASE_TAG, 'host_source_commit': HOST_SOURCE_COMMIT,
        'files_outside_typing_plugin': files,
        'documentation_path_adaptations': adaptations,
        'staging_note': 'Only the pinned legacy guide filename is adapted for Windows; its bytes and all other R6 files are unchanged. The published R6 archive is not modified.',
    })
    print(f'Prepared pinned published R6 host without rebuilding it: {destination}')


def standalone_context(args, head):
    if (args.host_release_tag != HOST_RELEASE_TAG or args.plugin_release_tag != STANDALONE_PLUGIN_TAG
            or args.plugin_id not in (None, ['synchronous-typing'])):
        raise ValueError('Standalone publication is restricted to typing 1.3.1 with the published R6 host')
    manifest = json.loads((ROOT / 'plugins/DictationAssistant.Practices.Typing/plugin.json').read_text(encoding='utf-8'))
    if (manifest.get('version') != '1.3.1' or manifest.get('apiVersion') != 1
            or manifest.get('minimumHostFeatureLevel') != 4):
        raise ValueError('The standalone typing manifest must keep plugin API 1 and R6 host feature level 4')
    if git('rev-parse', '--verify', f'refs/tags/{args.plugin_release_tag}^{{commit}}') != head:
        raise ValueError('The exact standalone plugin release tag must match the current HEAD')
    if git('rev-parse', '--verify', f'refs/tags/{HOST_RELEASE_TAG}^{{commit}}') != HOST_SOURCE_COMMIT:
        raise ValueError('The existing R6 host tag moved from its pinned commit')
    subprocess.run(['git', 'diff', '--quiet', HOST_SOURCE_COMMIT, head, '--',
                    'src/DictationAssistant.App', 'src/DictationAssistant.Plugin.Abstractions'], cwd=ROOT, check=True)
    disclosure = verified_host_metadata()
    baseline = json.loads((host_cache() / 'HOST_BASELINE.json').read_text(encoding='utf-8'))
    if (baseline.get('host_release_tag') != HOST_RELEASE_TAG
            or baseline.get('host_source_commit') != HOST_SOURCE_COMMIT
            or baseline.get('documentation_path_adaptations') != HOST_DOCUMENTATION_PATH_ADAPTATIONS
            or len(baseline.get('files_outside_typing_plugin', {})) != 385
            or baseline.get('files_outside_typing_plugin') != host_file_hashes(ROOT / 'artifacts/Windows-x64')):
        raise ValueError('Files outside the selected typing plugin changed in the published R6 host')
    compatibility = json.loads((host_cache() / 'PLUGIN_COMPATIBILITY.json').read_text(encoding='utf-8'))
    dll = ROOT / 'artifacts/Windows-x64/Plugins/SynchronousTyping/DictationAssistant.Practices.Typing.dll'
    expected = {'host_release_tag': HOST_RELEASE_TAG, 'host_source_commit': HOST_SOURCE_COMMIT,
                'plugin_source_commit': head, 'plugin_version': '1.3.1', 'plugin_api_version': 1,
                'host_feature_level': 4, 'word_plugin_version': '1.0.1',
                'practice_and_configuration_loaded': True, 'main_window_opened_and_closed': True,
                'host_documentation_path_adaptations': HOST_DOCUMENTATION_PATH_ADAPTATIONS,
                'host_non_typing_file_count': 385,
                'plugin_dll_sha256': digest(dll)}
    if any(compatibility.get(key) != value for key, value in expected.items()):
        raise ValueError('Compatibility validation does not match the current standalone plugin and pinned R6 host')
    return disclosure['release_review'], compatibility


def plugin_dependencies(directory, assembly, version, catalog):
    """Bind every shipped DLL to the project's actual .deps.json and notice catalog."""
    deps_path = directory / pathlib.Path(assembly).with_suffix('.deps.json')
    deps = json.loads(deps_path.read_text(encoding='utf-8-sig'))
    target = deps['targets'][deps['runtimeTarget']['name']]
    own_identity = f'{pathlib.Path(assembly).stem}/{version}'
    if own_identity not in target or deps['libraries'][own_identity].get('type') != 'project':
        raise ValueError('Published plugin dependency metadata does not match its version')
    by_identity = {(item['id'].lower(), item['version'].lower()): item
                   for item in catalog['packages']}
    owned = set()
    packages = []
    for identity, content in target.items():
        present = []
        for kind in ('runtime', 'native', 'resources', 'runtimeTargets'):
            for asset, metadata in content.get(kind, {}).items():
                actual = asset_path(directory, directory, asset, kind, metadata)
                if actual is None:
                    if kind != 'runtimeTargets' and pathlib.PurePosixPath(asset).name != '_._':
                        raise ValueError(f'Missing plugin dependency: {identity}: {asset}')
                    continue
                owned.add(actual.resolve())
                present.append(actual)
        if not present:
            continue
        if deps['libraries'][identity].get('type') == 'project':
            if identity != own_identity:
                raise ValueError(f'The plugin must reuse the host contract, not ship it: {identity}')
            continue
        package, package_version = identity.rsplit('/', 1)
        entry = by_identity.get((package.lower(), package_version.lower()))
        if entry is None or entry.get('release_blocked'):
            raise ValueError(f'Plugin private dependency lacks usable original notice/source evidence: {identity}')
        packages.append((entry, present))
    for path in directory.rglob('*'):
        if path.is_symlink():
            raise ValueError('Plugin release inputs must not contain symlinks')
        if not path.is_file():
            continue
        if 'runtimes' in path.relative_to(directory).parts:
            raise ValueError('Independent plugins must reuse the host native runtime assets')
        if path.suffix.lower() in ('.dll', '.exe', '.so', '.dylib') and path.resolve() not in owned:
            raise ValueError(f'Unindexed plugin binary: {path.name}')
    return packages


def package_plugin(plugin_id, project_name, folder, app_tag, head, catalog, host_review,
                   host_commit=None, compatibility=None):
    project_dir = ROOT / 'plugins' / project_name
    project = project_dir / f'{project_name}.csproj'
    manifest = json.loads((project_dir / 'plugin.json').read_text(encoding='utf-8'))
    version = project_version(project)
    if manifest.get('id') != plugin_id or manifest.get('version') != version:
        raise ValueError(f'Plugin manifest/project version mismatch: {plugin_id}')
    assembly = manifest['entryAssembly']
    if assembly != f'{project_name}.dll':
        raise ValueError(f'Unexpected entry assembly for {plugin_id}')
    published = ROOT / 'artifacts/Windows-x64/Plugins' / folder
    if not published.is_dir() or json.loads((published / 'plugin.json').read_text(encoding='utf-8')) != manifest:
        raise ValueError(f'The current plugin manifest is absent from the Windows output: {plugin_id}')
    if f'{version}+{head}'.encode('ascii') not in (published / assembly).read_bytes():
        raise ValueError(f'Plugin build metadata does not match version and release HEAD: {plugin_id}')
    dependencies = plugin_dependencies(published, assembly, version, catalog)
    output = ROOT / 'artifacts/plugin-releases' / plugin_id
    # This dedicated directory contains only generated assets for the current
    # version; stale prior-version ZIPs must not enter a later upload.
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)
    stage = ROOT / 'artifacts/plugin-release-work' / plugin_id
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    binary_root = stage / folder
    shutil.copytree(published, binary_root)
    for filename in PROJECT_NOTICES:
        shutil.copyfile(ROOT / filename, binary_root / filename)
    review = {
        'schema_version': 1,
        'plugin_id': plugin_id,
        'plugin_version': version,
        'source_commit': head,
        'app_release_tag': app_tag,
        'host_source_commit': host_commit or head,
        'status': 'published_with_declared_host_combination_review' if host_review['blocking_issues'] else 'ready',
        'legal_clearance_claimed': False,
        'host_release_review': host_review,
        'scope_note': 'This plugin ZIP excludes the host, shared contracts, Avalonia DLLs and native graphics runtimes. It does not resolve the host application combined-distribution authorization review. Original third-party licenses and private-dependency source materials remain included.',
        'unverified': ['Windows user/machine-bound encrypted translation profile persistence and cross-account rejection require real-machine validation.',
                       *(['Migration of an R5 single-binding credential file has not been validated on Windows.']
                         if version == '1.2.0' else []),
                       *(['Migration of existing R5/R6 credential files has not been validated on Windows.']
                         if version == '1.3.1' else []),
                       'Live provider translation requires valid user credentials; keys are never bundled.']
                      if plugin_id == 'synchronous-typing' else [],
    }
    if compatibility is not None:
        review['publication_scope'] = 'standalone-plugin-update-existing-host'
        review['host_compatibility'] = compatibility
    if plugin_id == 'docx-vocabulary' and version == '1.1.0':
        review['unverified'] = [
            'Full worksheet/value and GUI checks used the supplied XLSX on Linux; Windows Excel-import interaction requires separate validation.',
            'XLS, XLSB, XLSM and formula-cache behavior were not exercised by that XLSX sample.'
        ]
    write_json(output / 'RELEASE_REVIEW.json', review)
    write_json(binary_root / 'RELEASE_REVIEW.json', review)
    components = []
    archives = {}
    for entry, paths in dependencies:
        identity = f'{entry["id"]}/{entry["version"]}'
        components.append({
            'identity': identity, 'license_expression': entry['primary_spdx_expression'],
            'notice_files': copy_notices(entry, binary_root), 'repository': entry.get('repository'),
            'files': [{'path': path.relative_to(published).as_posix(), 'sha256': digest(path)} for path in paths],
        })
        for original in source_archives(entry):
            archive = normalize_archive(original)
            key = (archive['url'], archive['revision'])
            if key not in archives:
                archives[key] = {**archive, 'components': []}
            archives[key]['components'].append(identity)
    host_dependencies = [{'identity': 'DictationAssistant.Plugin.Abstractions', 'delivery': 'host-provided',
                          'api_version': manifest['apiVersion']}]
    for package in ET.parse(project).findall('.//PackageReference'):
        if 'runtime' not in package.attrib.get('ExcludeAssets', '').split(';'):
            continue
        identity = (package.attrib['Include'].lower(), package.attrib['Version'].lower())
        entry = next((item for item in catalog['packages']
                      if (item['id'].lower(), item['version'].lower()) == identity), None)
        if entry is None:
            raise ValueError(f'Host-provided reference lacks a notice catalog entry: {identity}')
        host_dependencies.append({'identity': f'{entry["id"]}/{entry["version"]}', 'delivery': 'host-provided',
                                  'notice_files': copy_notices(entry, binary_root),
                                  'source_references': source_archives(entry)})
    source_manifest = {'schema_version': 1, 'archives': list(archives.values()),
                       'scope': 'Private dependencies actually shipped in this plugin; shared host dependencies are identified separately.'}
    source_manifest_path = stage / 'PRIVATE_DEPENDENCY_SOURCES.json'
    write_json(source_manifest_path, source_manifest)
    source_cache = ROOT / 'artifacts/third-party-sources'
    source_index = collect(source_manifest_path, source_cache)
    inventory = {'schema_version': 1, 'plugin_id': plugin_id, 'version': version, 'source_commit': head,
                 'host_release_tag': app_tag, 'host_source_commit': host_commit or head,
                 'private_components': components, 'host_provided_dependencies': host_dependencies,
                 'companion_source_asset': f'{plugin_id}-{version}-source.zip',
                 'files': [{'path': path.relative_to(binary_root).as_posix(), 'sha256': digest(path)}
                           for path in sorted(binary_root.rglob('*')) if path.is_file()]}
    write_json(binary_root / 'PLUGIN_COMPONENTS.json', inventory)
    write_json(binary_root / 'PRIVATE_DEPENDENCY_SOURCES.json', source_index)
    build_notes = f'''# {manifest['name']} {version}

Release tag: `plugin-{plugin_id}-v{version}`
Plugin source commit: `{head}`; compatible host release: `{app_tag}`.
Host source commit: `{host_commit or head}`. The host is not included or rebuilt by this plugin package.

Close DictationAssistant, replace the complete `Plugins/{folder}` directory with
the `{folder}` directory in this ZIP, and restart. Do not copy just the DLL:
keep plugin.json, dependency metadata and all private dependencies together.
The host must support plugin API {manifest['apiVersion']} and feature level
{manifest.get('minimumHostFeatureLevel', 1)}, .NET 10 and the matching shared libraries.
User translations and protected API settings remain in the current user's
AppData directory, never in this package.

The source archive contains this plugin, its shared API source and original
project/third-party notices. From its root, use the SDK selected by global.json:

```sh
dotnet restore plugins/{project_name}/{project_name}.csproj -p:NuGetLockFilePath=obj/cloud.packages.lock.json
dotnet publish plugins/{project_name}/{project_name}.csproj -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o artifacts/{folder}
```

The host supplies shared Avalonia/contract/native runtime files. Private dependency
source archives in `third-party-sources/` are fixed and checked; original licenses
retain their own terms. See RELEASE_REVIEW.json for unresolved host licensing
review and real-machine validation limits. Publication and this declaration do
not establish a new permission or legal clearance for that combination.
'''
    (binary_root / 'INSTALL_AND_BUILD.md').write_text(build_notes, encoding='utf-8')
    if plugin_id == 'synchronous-typing':
        if version == '1.3.1':
            summary = ('新增微软/Bing、Google、百度、有道、腾讯、阿里和火山翻译 API，保留 DeepL；'
                       '在既有多配置、首选项和任务切换界面管理各服务凭据。仅更新插件，复用已发布 R6 本体；'
                       '插件 API 1、宿主功能级别 4 不变，不重新发布本体、Word 导入或原生资产。')
        else:
            summary = ('新增多个命名的 DeepL Free / Pro 翻译配置、首选配置及本次任务切换；'
                   '可从宿主插件管理中的“配置…”入口统一管理。要求宿主功能级别 4（R6）；'
                   'Windows 加密绑定整份配置，其他平台仅保留至程序退出；切换配置不触发翻译。') \
                if version == '1.2.0' else \
                '新增预录译文、可隐藏翻译提示及 DeepL 提前翻译；独立 API 设置窗口支持 Windows 本机与当前账户绑定的加密保存。删除指定的统一评分提示文字。'
    elif version == '1.1.0':
        summary = ('新增 Excel 工作簿读取，每张工作表独立预览、选列；保留既有 DOCX 导入。'
                   '使用固定 ExcelDataReader 3.9.0，插件 ID、程序集与 API 1 保持不变。'
                   '本次样本验证范围及未验证格式见对应源码中的 Excel 导入验证记录。')
    else:
        summary = '首次提供独立更新包，DOCX 表格/正文提取及列选择功能保持不变；可独立替换导入插件，无需重装宿主。'
    details = '\n'.join('- ' + item['reason'] for item in host_review['blocking_issues'])
    notes = f'''# {manifest['name']} {version}

{summary}

- 独立标签：`plugin-{plugin_id}-v{version}`。
- 插件对应提交：`{head}`；配套本体：`{app_tag}`，本体提交：`{host_commit or head}`。
- 关闭程序，将 ZIP 内完整 `{folder}` 文件夹放入 `Plugins/` 后重启。
- 提供独立插件、对应源码及 SHA-256 校验，密钥和个人词表不在包内。

## 许可声明

本插件包不携带宿主 Avalonia、共享契约或原生图形运行库；使用时仍依赖宿主。
本体的 Avalonia/MS-PL 与 AGPL 组合授权路径仍未解决。此次按维护者要求声明后发布，
不把收集声明、源码或本次发布写成已经取得额外授权，原始许可证继续适用。
详见 `RELEASE_REVIEW.json` 和包内原始声明。

{details}
'''
    if plugin_id == 'synchronous-typing':
        notes += '\nWindows 本机/账户加密保存和跨机器拒绝读取仍需实机验证；真实翻译需用户自己的有效服务凭据，未在本发布中声称已验证。\n'
        if version == '1.2.0':
            notes += 'R5 单绑定文件的 Windows 实机迁移尚未验证；1.2.0 需要 R6 或其他宿主功能级别 4 的版本，不能直接安装到 R5 本体。\n'
        if compatibility is not None:
            notes += '已将本插件装入固定哈希的已发布 R6 便携包，核对 API 1、功能级别 4、练习与配置能力加载以及生产主窗口正常开闭；R6 本体和 Word 1.0.1 文件保持原字节。此结果不代表真实在线翻译或 Windows 密钥迁移已验证。\n'
    (output / 'RELEASE_NOTES.md').write_text(notes, encoding='utf-8')
    binary = output / f'{plugin_id}-{version}.zip'
    with zipfile.ZipFile(binary, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(binary_root.rglob('*')):
            if path.is_file():
                archive.write(path, f'{folder}/{path.relative_to(binary_root).as_posix()}')
    source = output / f'{plugin_id}-{version}-source.zip'
    source_paths = [f'plugins/{project_name}', 'src/DictationAssistant.Plugin.Abstractions',
                    'Directory.Build.props', 'global.json', 'AGENTS.md', 'CONTRIBUTING.md',
                    *PROJECT_NOTICES, 'LICENSES', 'third-party/components.json',
                    'scripts/prepare_plugin_release.py', 'scripts/prepare_licenses.py',
                    'scripts/collect_third_party_sources.py', 'docs/同步打字插件使用说明.md']
    if compatibility is not None:
        source_paths.append('.github/workflows/release-typing-plugin.yml')
    if plugin_id == 'docx-vocabulary' and version == '1.1.0':
        source_paths.append('docs/Excel导入验证.md')
    subprocess.run(['git', 'archive', '--format=zip', f'--output={source}', head, '--', *source_paths],
                   cwd=ROOT, check=True)
    verify_git_archive_names(source, head, source_paths)
    with zipfile.ZipFile(source, 'a', compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('PLUGIN_BUILD.md', build_notes)
        archive.writestr('RELEASE_REVIEW.json', json.dumps(review, ensure_ascii=False, indent=2) + '\n')
        archive.writestr('PLUGIN_COMPONENTS.json', json.dumps(inventory, ensure_ascii=False, indent=2) + '\n')
        archive.write(source_cache / 'SOURCE_ARCHIVES.json', 'third-party-sources/SOURCE_ARCHIVES.json')
        for item in source_index['archives']:
            if item.get('archive_path'):
                archive.write(source_cache / item['archive_path'], 'third-party-sources/' + item['archive_path'],
                              compress_type=zipfile.ZIP_STORED)
    for path in (binary, source):
        check_zip(path)
    checksummed = (binary, source, output / 'RELEASE_NOTES.md', output / 'RELEASE_REVIEW.json')
    (output / 'SHA256SUMS').write_text(''.join(f'{digest(path)}  {path.name}\n' for path in checksummed), encoding='ascii')
    shutil.rmtree(stage)
    print(f'Plugin release: plugin-{plugin_id}-v{version}; assets: {output}; source commit: {head}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app-release-tag', help='Combined application/plugin release tag')
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--prepare-host', action='store_true', help='Download and extract the pinned public R6 host for compatibility checks')
    modes.add_argument('--standalone', action='store_true', help='Package only typing 1.3.1 against the unchanged published R6 host')
    parser.add_argument('--host-release-tag')
    parser.add_argument('--plugin-release-tag')
    parser.add_argument('--plugin-id', action='append', choices=[item[0] for item in PLUGINS],
                        help='Package only this plugin (repeat to select several); R5 defaults to both, R6 to typing only')
    args = parser.parse_args()
    try:
        if args.prepare_host:
            if args.app_release_tag or args.plugin_release_tag or args.plugin_id:
                raise ValueError('--prepare-host accepts only --host-release-tag')
            prepare_host(args.host_release_tag)
            return
        if args.standalone:
            if args.app_release_tag:
                raise ValueError('Standalone publication must not prepare a new application release')
        elif not args.app_release_tag or args.host_release_tag or args.plugin_release_tag:
            raise ValueError('Combined publication requires --app-release-tag; standalone publication requires --standalone and host/plugin tags')
        head = git('rev-parse', 'HEAD')
        subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=ROOT, check=True)
        if git('ls-files', '--others', '--exclude-standard'):
            raise ValueError('Commit tracked changes and new source files before plugin packaging')
        if args.standalone:
            host_review, compatibility = standalone_context(args, head)
            catalog = json.loads((ROOT / 'third-party/components.json').read_text(encoding='utf-8'))
            output_root = ROOT / 'artifacts/plugin-releases'
            if output_root.exists():
                shutil.rmtree(output_root)
            package_plugin(*PLUGINS[0], HOST_RELEASE_TAG, head, catalog, host_review,
                           host_commit=HOST_SOURCE_COMMIT, compatibility=compatibility)
            return
        selected = set(args.plugin_id or (
            ['synchronous-typing'] if args.app_release_tag == 'v4.0.0-typing.6'
            else [item[0] for item in PLUGINS]))
        if args.app_release_tag == 'v4.0.0-typing.6' and selected != {'synchronous-typing'}:
            raise ValueError('R6 publishes only the updated typing plugin; retain the existing Word importer release')
        if args.app_release_tag != 'v' + project_version(ROOT / 'src/DictationAssistant.App/DictationAssistant.App.csproj'):
            raise ValueError('App release tag must match the current application version')
        if git('rev-parse', '--verify', f'refs/tags/{args.app_release_tag}^{{commit}}') != head:
            raise ValueError('The exact application release tag must point to the current HEAD')
        app_output = ROOT / 'artifacts/Windows-x64'
        host_review = json.loads((app_output / 'THIRD_PARTY_SOURCES.json').read_text(encoding='utf-8'))['release_review']
        blockers = host_review['blocking_issues']
        if any(not isinstance(item, dict) or item.get('id') not in KNOWN_REVIEW_ISSUES for item in blockers):
            raise ValueError('An unexpected source/license issue prevents independent release packaging')
        app_review_path = ROOT / 'artifacts/release' / args.app_release_tag / 'RELEASE_REVIEW.json'
        if blockers:
            if not app_review_path.is_file():
                raise ValueError('Prepare the acknowledged application release assets before independent plugins')
            acknowledged = json.loads(app_review_path.read_text(encoding='utf-8'))
            if (acknowledged.get('source_commit') != head
                    or acknowledged.get('tag') != args.app_release_tag
                    or acknowledged.get('publication_decision') != 'explicitly_requested_publication_with_declared_unresolved_license_review'
                    or acknowledged.get('license_review_resolved') is not False
                    or set(acknowledged.get('acknowledged_issue_ids', [])) != {item['id'] for item in blockers}
                    or acknowledged.get('release_review') != host_review):
                raise ValueError('Application publication acknowledgement does not match this build and unresolved review')
        catalog = json.loads((ROOT / 'third-party/components.json').read_text(encoding='utf-8'))
        # A selected update must not upload another plugin's stale release files.
        output_root = ROOT / 'artifacts/plugin-releases'
        if output_root.exists():
            shutil.rmtree(output_root)
        for arguments in PLUGINS:
            if arguments[0] in selected:
                package_plugin(*arguments, args.app_release_tag, head, catalog, host_review)
    except (OSError, ValueError, KeyError, ET.ParseError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Plugin release preparation failed: {error}\n')


if __name__ == '__main__':
    main()
