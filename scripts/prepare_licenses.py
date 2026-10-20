"""Prepare the notices and source index for the files in an actual publish directory.

Notice collection is offline. Explicit native installation uses pinned HTTPS
assets; a local review build remains blocked from public distribution.
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
import struct
import subprocess
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT_NOTICES = ('LICENSE.md', 'NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md')
BINARY_SUFFIXES = ('.dll', '.exe', '.so', '.dylib')
NATIVE_INDEX = 'NATIVE_OVERRIDES.json'
ORIGINAL_DNG_SKIA_SHA256 = '9a0d95e8caaa852c70d085af6a40a744242172ad9ea3fd6bc7599875a8a1dbcd'


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


def require_hash(value, description):
    if not isinstance(value, str) or len(value) != 64 or any(character not in '0123456789abcdef' for character in value):
        raise ValueError(f'Missing or invalid SHA-256 pin: {description}')
    return value


def download_native_asset(asset, filename, maximum_bytes):
    expected = require_hash(asset.get('sha256'), filename)
    url = asset.get('url', '')
    if urllib.parse.urlsplit(url).scheme != 'https' or '/releases/download/' not in url:
        raise ValueError('Native binary/source assets must use explicitly versioned HTTPS release URLs')
    directory = ROOT / 'artifacts/native-cache'
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / filename
    if not target.is_file() or digest(target) != expected:
        temporary = target.with_suffix('.part')
        try:
            with urllib.request.urlopen(url, timeout=120) as response, temporary.open('wb') as destination:
                if urllib.parse.urlsplit(response.geturl()).scheme != 'https':
                    raise ValueError('Native asset download redirected outside HTTPS')
                total = 0
                while block := response.read(1024 * 1024):
                    total += len(block)
                    if total > maximum_bytes:
                        raise ValueError('Native asset exceeds the configured download size limit')
                    destination.write(block)
            if digest(temporary) != expected:
                raise ValueError(f'Native asset checksum mismatch: {filename}')
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
    if asset.get('bytes') is not None and target.stat().st_size != asset['bytes']:
        raise ValueError(f'Native asset byte count mismatch: {filename}')
    return target


def verify_native_manifest(entry, raw, binary, review_only):
    """Check the producer contract before any replacement becomes usable."""
    manifest = json.loads(raw.decode('utf-8'))
    if manifest.get('schema_version') != 1 or manifest.get('status') != 'verified':
        raise ValueError('Native producer verification has not completed successfully')
    if manifest.get('artifact_id') != entry['id'] or manifest.get('runtime_identifier') != entry['runtime_identifier']:
        raise ValueError('Native build artifact identity/runtime does not match the catalog')
    binary_sha = hashlib.sha256(binary).hexdigest()
    details = manifest['binary']
    if details['path'] != entry['path'] or details['sha256'] != binary_sha or details['bytes'] != len(binary):
        raise ValueError('Native build manifest does not describe the actual replacement DLL')
    if binary_sha in (entry['supersedes_sha256'], ORIGINAL_DNG_SKIA_SHA256):
        raise ValueError('The original DNG-enabled DLL cannot be used as a replacement')
    if any(marker in binary for marker in (b'.?AVdng_', b'dng_host', b'dng_info')):
        raise ValueError('Native replacement still contains known Adobe DNG implementation markers')
    if len(binary) < 64 or binary[:2] != b'MZ':
        raise ValueError('Native replacement is not a Windows PE image')
    pe_offset = struct.unpack_from('<I', binary, 0x3c)[0]
    if pe_offset + 6 > len(binary) or binary[pe_offset:pe_offset + 4] != b'PE\0\0' or struct.unpack_from('<H', binary, pe_offset + 4)[0] != 0x8664:
        raise ValueError('Native replacement is not a Windows x64 PE image')
    provenance = entry['provenance']
    for name, expected in provenance['sources'].items():
        actual = manifest['sources'][name]
        if any(actual.get(key) != expected[key] for key in ('repository', 'commit', 'archive_sha256')):
            raise ValueError(f'Native source provenance mismatch: {name}')
    patches = provenance['patches']
    if not patches or len(patches) != len(manifest.get('patches', [])):
        raise ValueError('Native patch provenance is missing or incomplete')
    actual_patches = {item['path']: item['sha256'] for item in manifest['patches']}
    for patch in patches:
        path = checked_relative(patch['path'])
        actual = digest(ROOT / path)
        if actual_patches.get(path) != actual or (patch.get('sha256') and patch['sha256'] != actual):
            raise ValueError(f'Native patch does not match the tracked build inputs: {path}')
    if any(manifest.get('gn_args', {}).get(name) != value for name, value in provenance['gn_args'].items()):
        raise ValueError('Native GN arguments do not match the reviewed no-DNG build')
    verification = manifest.get('verification', {})
    gates = ('no_dng_sources_in_graph', 'no_dng_commands', 'no_dng_binary_symbols', 'exports_match_official')
    if any(verification.get(name) is not True for name in gates):
        raise ValueError('Native no-DNG graph, commands, binary and export verification must all pass')
    smoke = verification.get('smoke_tests')
    if not isinstance(smoke, dict) or not smoke or any(result is not True for result in smoke.values()):
        raise ValueError('Native codec smoke checks must all pass')
    if any(smoke.get(name) is not True for name in ('native_abi_version_and_load', 'upstream_png_encode')):
        raise ValueError('The existing upstream native load/version and PNG encoding checks must actually pass')
    commit = manifest.get('build_commit', '')
    if len(commit) != 40 or any(character not in '0123456789abcdef' for character in commit):
        raise ValueError('Native build must record its full build-script commit')
    source = manifest['source_archive']
    require_hash(source['sha256'], 'native corresponding-source archive')
    if any(character in source['name'] for character in ('/', '\\', ':')) or source.get('bytes', 0) <= 0:
        raise ValueError('Invalid native corresponding-source archive description')
    if source['name'] != entry['sources_asset']['name']:
        raise ValueError('Native corresponding-source archive name does not match the configured asset')
    if review_only:
        head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        if commit != head:
            raise ValueError('Local native review artifact must use the current build-script commit')
    else:
        if entry.get('status') != 'verified':
            raise ValueError('The native artifact has not been reviewed and pinned in the catalog')
        if binary_sha != require_hash(entry.get('binary_sha256'), 'replacement DLL'):
            raise ValueError('Replacement DLL does not match the catalog pin')
        if hashlib.sha256(raw).hexdigest() != require_hash(entry['manifest'].get('sha256'), 'native-build.json'):
            raise ValueError('Native build record does not match the catalog pin')
        if commit != provenance.get('build_commit'):
            raise ValueError('Native build-script commit does not match the catalog pin')
        expected_source = entry['sources_asset']
        require_hash(expected_source.get('sha256'), 'native corresponding-source asset')
        if urllib.parse.urlsplit(expected_source.get('url', '')).scheme != 'https' or '/releases/download/' not in expected_source['url']:
            raise ValueError('Native source must use an explicitly versioned HTTPS release-asset URL')
        if any(source.get(key) != expected_source.get(key) for key in ('name', 'sha256', 'bytes')):
            raise ValueError('Corresponding-source asset does not match the catalog pin')
        distribution = entry.get('distribution_review', {})
        if distribution.get('status') != 'ready' or distribution.get('blocking_issues'):
            raise ValueError('Native asset distribution review remains blocked')
    return manifest


def install_native_overrides(publish, catalog_path, local_review=None):
    catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
    entries = catalog.get('native_overrides', [])
    if local_review and len(entries) != 1:
        raise ValueError('A local native review archive requires exactly one configured override')
    records = []
    for entry in entries:
        review_only = local_review is not None
        if review_only:
            archive = local_review
        else:
            if entry.get('status') != 'verified':
                raise ValueError(f'Native replacement {entry["id"]} is pending. Run the no-DNG Windows build and review its actual DLL/source before pinning an asset; the original DLL will not be used.')
            archive = download_native_asset(entry['archive'], entry['id'] + '.zip', 256 * 1024 * 1024)
        with zipfile.ZipFile(archive) as zf:
            names = zf.namelist()
            manifest_name = entry['manifest']['path']
            if names.count(entry['path']) != 1 or names.count(manifest_name) != 1:
                raise ValueError('Native ZIP must contain one root DLL and one native-build.json')
            if zf.getinfo(entry['path']).file_size > 64 * 1024 * 1024 or zf.getinfo(manifest_name).file_size > 4 * 1024 * 1024:
                raise ValueError('Native ZIP DLL/build record is unexpectedly large')
            binary = zf.read(entry['path'])
            raw = zf.read(manifest_name)
            if not entry.get('native_notice_paths'):
                raise ValueError('The native artifact requires its complete reviewed original notice list')
            for relative in entry['native_notice_paths']:
                relative = checked_relative(relative)
                if not relative.startswith('LICENSES/') or names.count(relative) != 1:
                    raise ValueError(f'Native ZIP omits an original license/notice: {relative}')
                if hashlib.sha256(zf.read(relative)).hexdigest() != digest(ROOT / relative):
                    raise ValueError(f'Native ZIP altered an original license/notice: {relative}')
            notice = zf.read(entry.get('notice', {}).get('path', 'NOTICE-NATIVE.txt'))
            for acknowledgment in entry.get('required_product_acknowledgments', []):
                if acknowledgment.encode('utf-8') not in notice:
                    raise ValueError('Native ZIP omits a required product acknowledgment')
        manifest = verify_native_manifest(entry, raw, binary, review_only)
        if review_only:
            source = archive.parent / manifest['source_archive']['name']
            if not source.is_file() or source.stat().st_size != manifest['source_archive']['bytes'] or digest(source) != manifest['source_archive']['sha256']:
                raise ValueError('Local native review requires the exact matching source archive beside the native ZIP')
        else:
            source = download_native_asset(entry['sources_asset'], entry['id'] + '-sources.zip', 2 * 1024 * 1024 * 1024)
        if not zipfile.is_zipfile(source):
            raise ValueError('Native corresponding-source asset is not a source ZIP')
        relative_manifest = f'NATIVE_BUILDS/{entry["id"]}/native-build.json'
        record_path = publish / relative_manifest
        record_path.parent.mkdir(parents=True, exist_ok=True)
        record_path.write_bytes(raw)
        (record_path.parent / 'NOTICE.md').write_bytes(notice)
        target = publish / checked_relative(entry['path'])
        temporary = target.with_suffix(target.suffix + '.replacement')
        temporary.write_bytes(binary)
        temporary.replace(target)
        records.append({'id': entry['id'], 'path': entry['path'], 'review_only': review_only,
                        'archive_sha256': digest(archive), 'manifest_path': relative_manifest,
                        'manifest_sha256': hashlib.sha256(raw).hexdigest(),
                        'notice_path': f'NATIVE_BUILDS/{entry["id"]}/NOTICE.md',
                        'notice_sha256': hashlib.sha256(notice).hexdigest(),
                        'binary_sha256': manifest['binary']['sha256'],
                        'source_archive': manifest['source_archive'], 'source_asset_download_verified': True,
                        'build_commit': manifest['build_commit']})
    write_json(publish / NATIVE_INDEX, {'schema_version': 1, 'overrides': records})
    print(f'Installed {len(records)} verified native replacement(s)' + (' for local review only' if local_review else ''))


def installed_native_overrides(publish, catalog):
    index = publish / NATIVE_INDEX
    if not index.is_file():
        return {}
    document = json.loads(index.read_text(encoding='utf-8'))
    if document.get('schema_version') != 1:
        raise ValueError('Unsupported installed-native manifest version')
    entries = {entry['id']: entry for entry in catalog.get('native_overrides', [])}
    installed = {}
    for record in document['overrides']:
        entry = entries[record['id']]
        path = checked_relative(record['path'])
        if path != entry['path'] or path in installed:
            raise ValueError('Duplicate or unknown installed native replacement')
        raw = (publish / checked_relative(record['manifest_path'])).read_bytes()
        if hashlib.sha256(raw).hexdigest() != record['manifest_sha256']:
            raise ValueError('Installed native build record checksum mismatch')
        notice = publish / checked_relative(record['notice_path'])
        if digest(notice) != record['notice_sha256']:
            raise ValueError('Installed native copyright/source notice checksum mismatch')
        manifest = verify_native_manifest(entry, raw, (publish / path).read_bytes(), record['review_only'])
        if not record['review_only'] and record['archive_sha256'] != entry['archive']['sha256']:
            raise ValueError('Installed native ZIP does not match the catalog pin')
        if record['binary_sha256'] != manifest['binary']['sha256'] or record['source_archive'] != manifest['source_archive']:
            raise ValueError('Installed native source/binary records differ')
        if record.get('source_asset_download_verified') is not True:
            raise ValueError('Installed native corresponding-source asset was not actually acquired and checked')
        installed[path] = {'record': record, 'entry': entry, 'manifest': manifest}
    return installed


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
    native_overrides = installed_native_overrides(publish, catalog)
    removed_components = {identity for installed in native_overrides.values()
                          for identity in installed['entry'].get('removed_components', [])}
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
        if not isinstance(item, dict) or item.get('component') not in removed_components:
            blocking_issues.append(item if isinstance(item, dict) else {'id': f'catalog-review-{index + 1}', 'reason': str(item)})
    for entry in catalog.get('native_overrides', []):
        installed = native_overrides.get(entry['path'])
        if not installed:
            blocking_issues.append({'id': 'native-replacement-unavailable:' + entry['id'],
                                    'reason': 'The required no-DNG native replacement has not been built, verified and installed. The original DNG-enabled DLL does not satisfy this requirement.'})
        elif installed['record']['review_only']:
            blocking_issues.append({'id': 'unreviewed-native-artifact:' + entry['id'],
                                    'reason': 'This locally verified native build is for review only. A versioned binary/source asset, reviewed license/source coverage and catalog SHA-256 pins are required before public distribution.'})

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
        identity = f'{component["id"]}/{component["version"]}'
        if identity in removed_components:
            if not all(path in native_overrides for path in component['files']):
                raise ValueError(f'Native component removal is not backed by an installed replacement: {identity}')
            continue
        paths = [publish / checked_relative(path) for path in component.get('files', [])]
        present = [path for path in paths if path.is_file()]
        if not present:
            continue
        reviewed_hashes = component.get('binary_sha256', {})
        for path in present:
            relative = path.relative_to(publish).as_posix()
            if relative in reviewed_hashes and digest(path) != reviewed_hashes[relative] and relative not in native_overrides:
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
    native_records = []
    for installed in native_overrides.values():
        record, entry = installed['record'], installed['entry']
        notices = copy_notices({'id': entry['id'], 'committed_notice_paths': entry['native_notice_paths']}, publish)
        native_records.append({**record, 'configuration': entry, 'notice_files': notices})
        for component in components.values():
            if component['identity'] in entry.get('affected_components', []):
                component['native_runtime_replacement'] = entry['id']
        if not record['review_only']:
            source = entry['sources_asset']
            archive = normalize_archive({'name': source['name'], 'url': source['url'],
                                         'sha256': source['sha256'], 'revision': record['build_commit'],
                                         'components': entry.get('affected_components', []), 'delivery': 'bundled'})
            archives[(archive['url'], archive['revision'])] = archive
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
        'native_overrides': native_records,
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
        'native_overrides': native_records,
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
    native_mode = parser.add_mutually_exclusive_group()
    native_mode.add_argument('--install-native-overrides', action='store_true',
                             help='Install only reviewed, versioned and SHA-256-pinned native assets; perform no notice collection')
    native_mode.add_argument('--native-review-archive', type=pathlib.Path,
                             help='Install a locally verified native ZIP for review only; matching source ZIP must be beside it')
    args = parser.parse_args()
    try:
        if args.install_native_overrides or args.native_review_archive:
            install_native_overrides(args.publish_dir.resolve(), args.catalog.resolve(),
                                     args.native_review_archive.resolve() if args.native_review_archive else None)
        else:
            prepare(args.publish_dir.resolve(), args.catalog.resolve(), args.nuget_cache.resolve())
    except (OSError, ValueError, KeyError, ET.ParseError, zipfile.BadZipFile, struct.error, subprocess.CalledProcessError) as error:
        parser.exit(1, f'License preparation failed: {error}\n')


if __name__ == '__main__':
    main()
