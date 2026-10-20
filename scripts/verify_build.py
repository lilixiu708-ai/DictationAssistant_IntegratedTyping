"""Validate the portable Windows package, including its exact license inventory.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import hashlib
import json
import pathlib
import urllib.parse
import zipfile
from prepare_licenses import ORIGINAL_DNG_SKIA_SHA256, verify_native_manifest

ROOT = pathlib.Path(__file__).resolve().parents[1]
REQUIRED = {
    'DictationAssistant.App.exe', 'DictationAssistant.App.dll',
    'DictationAssistant.App.runtimeconfig.json', 'DictationAssistant.App.deps.json',
    'DictationAssistant.Plugin.Abstractions.dll', 'coreclr.dll', 'hostfxr.dll',
    'System.Private.CoreLib.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll',
    'av_libglesv2.dll', 'SDL2.dll',
    'Plugins/DocxVocabulary/plugin.json',
    'Plugins/DocxVocabulary/DictationAssistant.Importers.Docx.dll',
    'Plugins/SynchronousTyping/plugin.json',
    'Plugins/SynchronousTyping/DictationAssistant.Practices.Typing.dll',
    'LICENSE.md', 'NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md',
    'THIRD_PARTY_COMPONENTS.json', 'THIRD_PARTY_SOURCES.json', 'EULA.txt',
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def relative(value):
    path = pathlib.PurePosixPath(value)
    require('\\' not in value and not path.is_absolute() and '..' not in path.parts,
            f'Unsafe archive path: {value}')
    return path.as_posix()


def verify(archive):
    require(archive.is_file(), f'Missing Windows package: {archive}')
    with zipfile.ZipFile(archive) as zf:
        members = {}
        folded = set()
        for info in zf.infolist():
            name = relative(info.filename)
            if info.is_dir() or name == '.':
                continue
            require(name.casefold() not in folded, f'Duplicate Windows archive path: {name}')
            folded.add(name.casefold())
            members[name] = info
        names = set(members)
        require(not REQUIRED - names, f'Missing from archive: {sorted(REQUIRED - names)}')
        require(zf.testzip() is None, 'Corrupted zip member')
        plugin_runtimes = sorted(name for name in names if name.startswith('Plugins/') and '/runtimes/' in name)
        require(not plugin_runtimes, f'Plugins must share the host graphics libraries: {plugin_runtimes}')
        binaries = {name for name in names if name.lower().endswith(('.dll', '.exe', '.so', '.dylib'))}
        bass = sorted(name for name in binaries if pathlib.PurePosixPath(name).name.lower().startswith(('bass', 'libbass', 'managedbass')))
        require(not bass, f'Unresolved BASS binaries must not be released: {bass}')

        def document(name):
            return json.loads(zf.read(members[name]).decode('utf-8-sig'))

        hashes = {}

        def checksum(name):
            require(name in members, f'Indexed file is absent from archive: {name}')
            if name not in hashes:
                with zf.open(members[name]) as stream:
                    hashes[name] = hashlib.file_digest(stream, 'sha256').hexdigest()
            return hashes[name]

        require(checksum('libSkiaSharp.dll') != ORIGINAL_DNG_SKIA_SHA256,
                'The original DNG-enabled Skia DLL must be replaced by an actual verified no-DNG build')

        require(b'GNU AFFERO GENERAL PUBLIC LICENSE' in zf.read(members['LICENSE.md']),
                'The complete project AGPL license must accompany the binary package')
        for name in ('NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md'):
            require(bool(zf.read(members[name]).strip()), f'Empty project notice: {name}')
        runtime = document('DictationAssistant.App.runtimeconfig.json')
        frameworks = runtime['runtimeOptions'].get('includedFrameworks')
        require(bool(frameworks), 'Windows package must include its .NET runtime')
        docx = document('Plugins/DocxVocabulary/plugin.json')
        typing = document('Plugins/SynchronousTyping/plugin.json')
        require(docx.get('kind', 'document-importer') == 'document-importer' and '.docx' in docx['extensions'],
                'Invalid Word importer manifest')
        require(typing['kind'] == 'practice' and typing['id'] == 'synchronous-typing',
                'Invalid synchronous typing manifest')

        inventory = document('THIRD_PARTY_COMPONENTS.json')
        sources = document('THIRD_PARTY_SOURCES.json')
        catalog_path = ROOT / 'third-party/components.json'
        catalog_bytes = catalog_path.read_bytes()
        require(hashlib.sha256(catalog_bytes).hexdigest() == inventory.get('catalog_sha256'),
                'Binary inventory does not match the tracked license/native catalog; rebuild the package')
        catalog = json.loads(catalog_bytes)
        configured_native = {entry['id']: entry for entry in catalog.get('native_overrides', [])}
        require(inventory.get('schema_version') == 1 and sources.get('schema_version') == 1,
                'Unsupported component/source manifest version')
        review = sources.get('release_review', {})
        issues = review.get('blocking_issues')
        require(isinstance(issues, list) and all(isinstance(issue, dict) and issue.get('id') and issue.get('reason') for issue in issues),
                'Missing or invalid release review blocking-issue records')
        require(review.get('status') == ('blocked' if issues else 'ready'), 'Release review status does not match its unresolved issues')
        components = {item['identity']: item for item in inventory['components']}
        require(len(components) == len(inventory['components']), 'Duplicate component identities')
        owners = set(components) | set(inventory['project_components'])
        native_records = inventory.get('native_overrides', [])
        require(native_records and native_records == sources.get('native_overrides'),
                'A matching native replacement/provenance record must accompany the no-DNG DLL')
        native_paths = set()
        for record in native_records:
            name = relative(record['path'])
            require(name not in native_paths, f'Duplicate native replacement path: {name}')
            native_paths.add(name)
            require(checksum(name) == record['binary_sha256'], f'Native replacement checksum mismatch: {name}')
            manifest_path = relative(record['manifest_path'])
            require(checksum(manifest_path) == record['manifest_sha256'], 'Native build record checksum mismatch')
            notice_path = relative(record['notice_path'])
            require(checksum(notice_path) == record['notice_sha256'], 'Native copyright/source notice checksum mismatch')
            require(record['id'] in configured_native and record['configuration'] == configured_native[record['id']],
                    'Embedded native configuration differs from the tracked catalog')
            configuration = configured_native[record['id']]
            for acknowledgment in configuration.get('required_product_acknowledgments', []):
                require(acknowledgment.encode('utf-8') in zf.read(members[notice_path]), 'Native product acknowledgment is absent')
            required_notices = set(configuration['native_notice_paths'])
            require({item['path'] for item in record.get('notice_files', [])} == required_notices,
                    'Native original license/notice inventory is incomplete')
            for notice in record['notice_files']:
                require(checksum(relative(notice['path'])) == notice['sha256'], 'Native original license/notice checksum mismatch')
            manifest = verify_native_manifest(configuration, zf.read(members[manifest_path]), zf.read(members[name]), record['review_only'])
            require(record['source_archive'] == manifest['source_archive'], 'Native source archive description mismatch')
            require(record.get('source_asset_download_verified') is True, 'Native source asset has not been acquired and checked')
            require(not (set(configuration.get('removed_components', [])) & set(components)),
                    'Removed DNG component is still recorded as shipped with the replacement')
            if record['review_only']:
                require(review['status'] == 'blocked' and any(issue['id'] == 'unreviewed-native-artifact:' + record['id'] for issue in issues),
                        'A local native review build cannot be marked ready for public release')
            else:
                require(record['archive_sha256'] == configuration['archive']['sha256'], 'Native ZIP pin mismatch')
        require('libSkiaSharp.dll' in native_paths, 'Skia must use the verified no-DNG native replacement')
        for identity, item in components.items():
            require(item.get('notice_files'), f'No original license/notice recorded for {identity}')
            require(item.get('license_expression'), f'No reviewed license expression for {identity}')
            for notice in item['notice_files']:
                name = relative(notice['path'])
                require(name.startswith('LICENSES/'), f'License notice has an invalid location: {name}')
                require(checksum(name) == notice['sha256'], f'License notice checksum mismatch: {name}')

        recorded = {}
        for item in inventory['files']:
            name = relative(item['path'])
            require(name not in recorded, f'Duplicate component file entry: {name}')
            require(item.get('components') and set(item['components']) <= owners,
                    f'Unknown component owner for {name}')
            require(checksum(name) == item['sha256'], f'Component file checksum mismatch: {name}')
            recorded[name] = item
        require(not binaries - recorded.keys(),
                f'Unaccounted-for distributed binaries: {sorted(binaries - recorded.keys())}')
        require(any(item['id'].lower() == 'sdl2' and item['kind'] == 'native' for item in components.values()),
                'The SDL2 native library needs its own license entry')

        source_owners = set()
        for source in sources['archives']:
            require(urllib.parse.urlsplit(source['url']).scheme == 'https', 'Source archive must use HTTPS')
            require(source.get('revision', '').lower() not in ('', 'head', 'main', 'master', 'latest'),
                    f'Source archive needs a fixed revision: {source["url"]}')
            source_owners.update(source.get('components', []))
        for source in sources.get('inline_source_files', []):
            require(relative(source['path']).startswith('third-party/sources/'), 'Invalid inline source path')
            require(len(source.get('sha256', '')) == 64, f'Inline source needs a SHA-256: {source["path"]}')
            source_owners.update(source.get('components', []))
        platform = {key for key, item in components.items() if item['kind'] == 'platform-runtime'}
        missing_sources = set(components) - source_owners - platform
        require(not missing_sources, f'No fixed source delivery recorded for: {sorted(missing_sources)}')

        def asset_name(base, asset, kind, metadata):
            name = pathlib.PurePosixPath(asset).name
            if name == '_._':
                return None
            if kind == 'resources':
                locale = metadata.get('locale') or pathlib.PurePosixPath(asset).parent.name
                candidates = [f'{base}{locale}/{name}', f'{locale}/{name}']
            else:
                candidates = [f'{base}{name}', f'{base}{asset}', name, asset]
            return next((name for name in candidates if name in members), None)

        runtime_components = set()
        for name in sorted(names):
            if not name.endswith('.deps.json'):
                continue
            data = document(name)
            base = name.rpartition('/')[0]
            base = base + '/' if base else ''
            for identity, content in data['targets'][data['runtimeTarget']['name']].items():
                actual = []
                for kind in ('runtime', 'native', 'resources', 'runtimeTargets'):
                    for asset, metadata in content.get(kind, {}).items():
                        match = asset_name(base, asset, kind, metadata)
                        if match:
                            actual.append(match)
                        elif kind != 'runtimeTargets' and pathlib.PurePosixPath(asset).name != '_._':
                            raise ValueError(f'Missing published dependency asset: {identity}: {asset}')
                if not actual:
                    continue
                require(identity in owners, f'Published dependency has no component/license entry: {identity}')
                for filename in actual:
                    require(filename in recorded and identity in recorded[filename]['components'],
                            f'Dependency/component mapping mismatch: {identity}: {filename}')
                if identity.startswith('runtimepack.'):
                    runtime_components.add(identity)
                    version = identity.rsplit('/', 1)[1]
                    require(any(item.get('version') == version for item in frameworks),
                            f'Runtime manifest and framework versions differ: {identity}')
                    require(components[identity]['kind'] == 'platform-runtime', f'Invalid runtime classification: {identity}')
        require(bool(runtime_components), 'No actual .NET runtime pack recorded')
    print(f'Validated {len(names)} portable package files and {len(components)} licensed components: {archive}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=pathlib.Path,
                        default=ROOT / 'artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip')
    args = parser.parse_args()
    try:
        verify(args.archive.resolve())
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(1, f'Package validation failed: {error}\n')


if __name__ == '__main__':
    main()
