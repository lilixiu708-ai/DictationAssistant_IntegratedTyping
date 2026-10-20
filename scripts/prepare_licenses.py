"""Prepare the notices and source index for the files in an actual publish directory.

No network access is needed. Unknown packages, missing original notices and
unaccounted-for binaries stop packaging rather than silently dropping notices.
SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import datetime
import hashlib
import json
import os
import pathlib
import shutil
import urllib.parse
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT_NOTICES = ('LICENSE.md', 'NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md')
BINARY_SUFFIXES = ('.dll', '.exe', '.so', '.dylib')


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def checked_relative(path):
    value = pathlib.PurePosixPath(str(path).replace('\\', '/'))
    if value.is_absolute() or '..' in value.parts or not value.parts:
        raise ValueError(f'Unsafe relative path: {path}')
    return value.as_posix()


def copy_notices(component, publish):
    paths = component.get('committed_notice_paths') or component.get('notice_paths') or []
    if not paths:
        raise ValueError(f'No original license/notice files recorded for {component.get("id")}')
    copied = []
    for item in paths:
        relative = checked_relative(item['path'] if isinstance(item, dict) else item)
        source = ROOT / relative
        if not relative.startswith('LICENSES/') or not source.is_file():
            raise ValueError(f'Missing committed license/notice: {relative}')
        actual = digest(source)
        if isinstance(item, dict) and item.get('sha256') and actual != item['sha256']:
            raise ValueError(f'License evidence changed without updating the catalog: {relative}')
        destination = publish / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        copied.append({'path': relative, 'sha256': actual})
    return copied


def source_archives(component):
    """Explicit archives also describe native subprojects omitted by GitHub archives."""
    if 'source_archives' in component:
        return component['source_archives']
    repository = component.get('repository') or {}
    url = component.get('source_repository') or repository.get('url')
    revision = component.get('source_ref') or repository.get('commit')
    explicit = component.get('source_archive_url')
    if url:
        url = url.rstrip('/').removesuffix('.git')
    if not explicit and url and revision and url.startswith('https://github.com/'):
        explicit = f'{url}/archive/{urllib.parse.quote(revision, safe="")}.zip'
    if not explicit or not revision:
        raise ValueError(f'No fixed source archive recorded for {component["id"]} {component.get("version", "")}')
    return [{
        'name': component.get('source_name') or url.split('/')[-1],
        'repository': url,
        'revision': revision,
        'url': explicit,
        'delivery': component.get('source_delivery', 'bundled'),
        **({'sha256': component['source_archive_sha256']} if component.get('source_archive_sha256') else {}),
    }]


def normalize_archive(archive):
    url = archive.get('url') or archive.get('archive_url')
    revision = archive.get('revision') or archive.get('ref')
    if not url or urllib.parse.urlsplit(url).scheme != 'https':
        raise ValueError(f'Source archives must use HTTPS: {url}')
    if not revision or revision.lower() in ('head', 'main', 'master', 'latest'):
        raise ValueError(f'Source archive needs a fixed revision: {url}')
    return {**archive, 'url': url, 'revision': revision,
            'delivery': archive.get('delivery', 'bundled')}


def asset_path(publish, base, asset, kind, metadata):
    name = pathlib.PurePosixPath(asset).name
    if name == '_._':
        return None
    if kind == 'resources':
        locale = metadata.get('locale') or pathlib.PurePosixPath(asset).parent.name
        candidates = [base / locale / name, publish / locale / name]
    else:
        candidates = [base / name, base / asset, publish / name, publish / asset]
    return next((path for path in candidates if path.is_file()), None)


def runtime_component(identity, metadata, publish, cache):
    package, version = identity.rsplit('/', 1)
    package = package.removeprefix('runtimepack.')
    directory = cache / package.lower() / version.lower()
    if not directory.is_dir():
        raise ValueError(f'The actual runtime pack {package}/{version} is absent from NuGet cache {cache}')
    notices = []
    for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
        source = directory / name
        if not source.is_file():
            raise ValueError(f'The actual runtime pack has no {name}: {directory}')
        relative = f'LICENSES/runtime/{package}/{version}/{name}'
        target = publish / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        notices.append({'path': relative, 'sha256': digest(source)})
    nuspec = next(directory.glob('*.nuspec'))
    document = ET.parse(nuspec)
    repository = next((element.attrib for element in document.iter()
                       if element.tag.rsplit('}', 1)[-1] == 'repository'), {})
    return {
        'id': package, 'version': version, 'kind': 'platform-runtime',
        'license_expression': 'MIT (with the original bundled third-party notices)',
        'notice_files': notices, 'repository': repository,
        'source_delivery': 'system-library-reference',
        'source_note': '.NET platform runtime; original runtime source and third-party notices are identified separately from application source.',
        'source_url': f'https://github.com/dotnet/runtime/tree/v{version}',
    }


def prepare(publish, catalog_path, cache):
    if not publish.is_dir():
        raise ValueError(f'Publish directory is absent: {publish}')
    catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
    packages = catalog.get('packages', catalog.get('components', []))
    by_identity = {(package['id'].lower(), package['version'].lower()): package for package in packages}
    if len(by_identity) != len(packages):
        raise ValueError('The license catalog contains duplicate package/version identities')

    for filename in PROJECT_NOTICES:
        if not (ROOT / filename).is_file():
            raise ValueError(f'Missing project notice: {filename}')
        shutil.copyfile(ROOT / filename, publish / filename)
    shutil.copyfile(ROOT / 'deploy/shared/EULA.txt', publish / 'EULA.txt')

    components = {}
    files = {}
    archives = {}
    vendored_sources = {}
    projects = set()
    blocking_issues = []
    for index, item in enumerate(catalog.get('release_review', {}).get('blocking_issues', [])):
        blocking_issues.append(item if isinstance(item, dict) else {'id': f'catalog-review-{index + 1}', 'reason': str(item)})

    def review_fields(evidence, identity, native=False):
        fields = {name: evidence[name] for name in (
            'copyright', 'authors', 'selected_alternative', 'embedded_license_note',
            'license_note', 'source_provenance', 'source_coverage',
        ) if name in evidence}
        native = native or '.nativeassets.' in identity.lower() or 'angle.windows.natives' in identity.lower()
        if native:
            coverage = evidence.get('source_coverage', {})
            if coverage.get('status') != 'complete' or not coverage.get('evidence'):
                blocking_issues.append({
                    'id': 'native-corresponding-source:' + identity,
                    'component': identity,
                    'reason': 'The complete corresponding native source, fixed submodule/DEPS inputs and applicable build instructions have not been verified. An upstream archive/index alone does not establish complete corresponding source.',
                })
        return fields

    def add_archives(component, owner):
        for value in component.get('vendored_source_paths', []):
            relative = checked_relative(value)
            if not relative.startswith('third-party/sources/'):
                raise ValueError(f'Inline third-party sources must be under third-party/sources/: {relative}')
            original = ROOT / relative
            candidates = sorted(original.rglob('*')) if original.is_dir() else [original]
            if not original.exists():
                raise ValueError(f'Inline third-party source is missing: {relative}')
            for path in candidates:
                if path.is_symlink():
                    raise ValueError(f'Inline source must be a regular file, not a link: {path}')
                if path.is_file():
                    key = path.relative_to(ROOT).as_posix()
                    entry = vendored_sources.setdefault(key, {'path': key, 'sha256': digest(path), 'components': []})
                    if owner not in entry['components']:
                        entry['components'].append(owner)
        if component.get('source_archives') == [] and not component.get('vendored_source_paths'):
            raise ValueError(f'Empty source archive list without inline source: {owner}')
        for original in source_archives(component):
            archive = normalize_archive(original)
            key = (archive['url'], archive['revision'])
            if key not in archives:
                archives[key] = {**archive, 'components': []}
            if owner not in archives[key]['components']:
                archives[key]['components'].append(owner)

    def record_file(path, owner):
        relative = path.relative_to(publish).as_posix()
        entry = files.setdefault(relative, {'path': relative, 'sha256': digest(path), 'components': []})
        if owner not in entry['components']:
            entry['components'].append(owner)

    deps_files = sorted(publish.rglob('*.deps.json'))
    if not deps_files:
        raise ValueError('No published .deps.json files were found')
    for deps in deps_files:
        data = json.loads(deps.read_text(encoding='utf-8-sig'))
        target_name = data['runtimeTarget']['name']
        target = data['targets'][target_name]
        for identity, content in target.items():
            actual_assets = []
            for kind in ('runtime', 'native', 'resources', 'runtimeTargets'):
                for asset, asset_metadata in content.get(kind, {}).items():
                    path = asset_path(publish, deps.parent, asset, kind, asset_metadata)
                    if path is not None:
                        actual_assets.append(path)
                    elif kind != 'runtimeTargets' and pathlib.PurePosixPath(asset).name != '_._':
                        raise ValueError(f'Published dependency asset is missing: {identity}: {asset}')
            if not actual_assets:
                continue
            package, version = identity.rsplit('/', 1)
            metadata = data['libraries'][identity]
            if metadata.get('type') == 'project':
                projects.add(identity)
                for path in actual_assets:
                    record_file(path, identity)
                continue
            if package.lower() == 'managedbass' or package.lower().startswith('bass'):
                raise ValueError(f'Unresolved BASS dependency must not be released: {identity}')
            if identity not in components:
                if package.startswith('runtimepack.'):
                    component = runtime_component(identity, metadata, publish, cache)
                else:
                    evidence = by_identity.get((package.lower(), version.lower()))
                    if evidence is None:
                        raise ValueError(f'Package has no reviewed license catalog entry: {identity}')
                    if evidence.get('release_blocked'):
                        raise ValueError(f'Package is blocked by the license review: {identity}')
                    component = {
                        'id': package, 'version': version, 'kind': 'nuget',
                        'license_expression': evidence.get('primary_spdx_expression') or evidence.get('license_expression') or evidence.get('license_value'),
                        'notice_files': copy_notices(evidence, publish),
                        'repository': evidence.get('repository'),
                        **review_fields(evidence, identity),
                    }
                    add_archives(evidence, identity)
                component['identity'] = identity
                components[identity] = component
            for path in actual_assets:
                record_file(path, identity)

    for component in catalog.get('native_components', []):
        paths = [publish / checked_relative(path) for path in component.get('files', [])]
        present = [path for path in paths if path.is_file()]
        if not present:
            continue
        identity = f'{component["id"]}/{component["version"]}'
        reviewed_hashes = component.get('binary_sha256', {})
        for path in present:
            relative = path.relative_to(publish).as_posix()
            if relative in reviewed_hashes and digest(path) != reviewed_hashes[relative]:
                raise ValueError(f'Native binary changed without a new license review: {relative}')
            record_file(path, identity)
        components[identity] = {
            'identity': identity,
            'id': component['id'], 'version': component['version'], 'kind': 'native',
            'license_expression': component.get('primary_spdx_expression') or component.get('license_expression'),
            'notice_files': copy_notices(component, publish),
            'repository': component.get('repository'),
            **review_fields(component, identity, native=True),
        }
        add_archives(component, identity)
    for archive in catalog.get('source_archives', []):
        item = normalize_archive(archive)
        archives.setdefault((item['url'], item['revision']), {**item, 'components': item.get('components', [])})

    # The apphost is generated from the platform SDK, rather than listed as an
    # ordinary runtime asset. It is covered by the platform runtime notices.
    apphost = publish / 'DictationAssistant.App.exe'
    runtime_ids = [identity for identity in components if identity.startswith('runtimepack.')]
    if apphost.is_file():
        if not runtime_ids:
            raise ValueError('Self-contained apphost has no recorded platform runtime')
        record_file(apphost, runtime_ids[0])
    for path in publish.rglob('*'):
        if not path.is_file() or not path.name.lower().endswith(BINARY_SUFFIXES):
            continue
        relative = path.relative_to(publish).as_posix()
        if path.name.lower().startswith(('bass', 'libbass')) or path.name.lower() == 'managedbass.dll':
            raise ValueError(f'Unresolved BASS binary must not be released: {relative}')
        if relative not in files:
            raise ValueError(f'Published binary is not accounted for by dependency/native manifests: {relative}')
    if (publish / 'SDL2.dll').is_file() and not any(value['id'].lower() == 'sdl2' for value in components.values()):
        raise ValueError('The SDL2 native library needs its own zlib license entry, separate from the managed wrapper')

    stamp = datetime.datetime.now(datetime.timezone.utc).isoformat()
    manifest = {
        'schema_version': 1, 'generated_at_utc': stamp,
        'catalog_sha256': digest(catalog_path),
        'components': [components[key] for key in sorted(components, key=str.lower)],
        'project_components': sorted(projects),
        'files': [files[key] for key in sorted(files)],
    }
    write_json(publish / 'THIRD_PARTY_COMPONENTS.json', manifest)
    write_json(publish / 'THIRD_PARTY_SOURCES.json', {
        'schema_version': 1, 'generated_at_utc': stamp,
        'archives': [archives[key] for key in sorted(archives)],
        'inline_source_files': [vendored_sources[key] for key in sorted(vendored_sources)],
        'platform_runtime_sources': [value for value in components.values() if value['kind'] == 'platform-runtime'],
        'native_dependency_provenance': catalog.get('native_source_provenance', {}),
        'release_review': {
            'status': 'blocked' if blocking_issues else 'ready',
            'public_release_ready': not blocking_issues,
            'source_bundle_scope': 'Fixed source archives, original notices and recorded source references; complete native corresponding-source coverage requires separate review.',
            'blocking_issues': blocking_issues,
        },
        'scope_note': 'Fixed upstream source archives and source references accompany this build. An upstream archive can omit git submodules or DEPS downloads; retain and follow the recorded upstream build/source instructions and additional archive entries.',
    })
    print(f'Prepared notices for {len(components)} components and {len(files)} published binaries/resources: {publish}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish-dir', type=pathlib.Path, default=ROOT / 'artifacts/Windows-x64')
    parser.add_argument('--catalog', type=pathlib.Path, default=ROOT / 'third-party/components.json')
    parser.add_argument('--nuget-cache', type=pathlib.Path,
                        default=pathlib.Path(os.environ.get('NUGET_PACKAGES', pathlib.Path.home() / '.nuget/packages')))
    args = parser.parse_args()
    try:
        prepare(args.publish_dir.resolve(), args.catalog.resolve(), args.nuget_cache.resolve())
    except (OSError, ValueError, KeyError, ET.ParseError) as error:
        parser.exit(1, f'License preparation failed: {error}\n')


if __name__ == '__main__':
    main()
