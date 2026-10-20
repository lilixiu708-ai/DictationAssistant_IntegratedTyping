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


def package_plugin(plugin_id, project_name, folder, app_tag, head, catalog, host_review):
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
        'status': 'published_with_declared_host_combination_review' if host_review['blocking_issues'] else 'ready',
        'legal_clearance_claimed': False,
        'host_release_review': host_review,
        'scope_note': 'This plugin ZIP excludes the host, shared contracts, Avalonia DLLs and native graphics runtimes. It does not resolve the host application combined-distribution authorization review. Original third-party licenses and private-dependency source materials remain included.',
        'unverified': ['Windows user/machine-bound API credential persistence and cross-account rejection require real-machine validation.',
                       'Live DeepL translation requires a valid user key; keys are never bundled.']
                      if plugin_id == 'synchronous-typing' else [],
    }
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
                 'private_components': components, 'host_provided_dependencies': host_dependencies,
                 'companion_source_asset': f'{plugin_id}-{version}-source.zip',
                 'files': [{'path': path.relative_to(binary_root).as_posix(), 'sha256': digest(path)}
                           for path in sorted(binary_root.rglob('*')) if path.is_file()]}
    write_json(binary_root / 'PLUGIN_COMPONENTS.json', inventory)
    write_json(binary_root / 'PRIVATE_DEPENDENCY_SOURCES.json', source_index)
    build_notes = f'''# {manifest['name']} {version}

Release tag: `plugin-{plugin_id}-v{version}`
Source commit: `{head}`; companion host release: `{app_tag}`.

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
        summary = '新增预录译文、可隐藏翻译提示及 DeepL 提前翻译；独立 API 设置窗口支持 Windows 本机与当前账户绑定的加密保存。删除指定的统一评分提示文字。'
    else:
        summary = '首次提供独立更新包，DOCX 表格/正文提取及列选择功能保持不变；可独立替换导入插件，无需重装宿主。'
    details = '\n'.join('- ' + item['reason'] for item in host_review['blocking_issues'])
    notes = f'''# {manifest['name']} {version}

{summary}

- 独立标签：`plugin-{plugin_id}-v{version}`。
- 对应提交：`{head}`；配套本体：`{app_tag}`。
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
        notes += '\nWindows 本机/账户加密保存和跨机器拒绝读取仍需实机验证；真实 DeepL 翻译需用户自己的有效密钥，未在本发布中声称已验证。\n'
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
    subprocess.run(['git', 'archive', '--format=zip', f'--output={source}', head, '--', *source_paths],
                   cwd=ROOT, check=True)
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
    parser.add_argument('--app-release-tag', required=True)
    args = parser.parse_args()
    try:
        head = git('rev-parse', 'HEAD')
        if args.app_release_tag != 'v' + project_version(ROOT / 'src/DictationAssistant.App/DictationAssistant.App.csproj'):
            raise ValueError('App release tag must match the current application version')
        if git('rev-parse', '--verify', f'refs/tags/{args.app_release_tag}^{{commit}}') != head:
            raise ValueError('The exact application release tag must point to the current HEAD')
        subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=ROOT, check=True)
        if git('ls-files', '--others', '--exclude-standard'):
            raise ValueError('Commit tracked changes and new source files before plugin packaging')
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
        for arguments in PLUGINS:
            package_plugin(*arguments, args.app_release_tag, head, catalog, host_review)
    except (OSError, ValueError, KeyError, ET.ParseError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Plugin release preparation failed: {error}\n')


if __name__ == '__main__':
    main()
