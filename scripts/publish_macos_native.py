"""Upload reviewed macOS native assets to one existing, empty draft.

This helper never creates tags/releases, replaces assets, or publishes a draft.
SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile
import urllib.parse
import zipfile

from prepare_licenses import verify_native_manifest


ROOT = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = 'lilixiu708-ai/DictationAssistant_IntegratedTyping'
TAG = 'native-skia-2.88.9-no-dng-macos.1'
PRODUCER = '.github/workflows/build-skia-macos-without-dng.yml'
RIDS = ('osx-x64', 'osx-arm64')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def gh(*arguments):
    return subprocess.check_output(['gh', *map(str, arguments)], text=True)


def api(path):
    return json.loads(gh('api', f'repos/{REPOSITORY}/{path}'))


def relative(value):
    path = pathlib.PurePosixPath(value)
    require(not path.is_absolute() and '..' not in path.parts and '\\' not in value,
            f'Unsafe archive member: {value}')
    return path.as_posix()


def verify_zip(archive):
    names = archive.namelist()
    require(len(names) == len(set(names)), 'An archive contains duplicate member names')
    for name in names:
        relative(name)
    require(archive.testzip() is None, 'An archive contains a corrupt member')


def member_digest(archive, path):
    with archive.open(relative(path)) as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_source(entry, manifest, source_path):
    with zipfile.ZipFile(source_path) as source:
        verify_zip(source)
        expected = {key: value for key, value in manifest.items() if key != 'source_archive'}
        require(json.loads(source.read('BUILD_PROVENANCE.json')) == expected,
                'The source and native manifests describe different builds')
        records = manifest.get('build_records', [])
        require(records and len({record['path'] for record in records}) == len(records),
                'Build record hashes are missing or duplicated')
        for record in records:
            require(member_digest(source, 'build-records/' + relative(record['path'])) == record['sha256'],
                    f'A build record differs: {record["path"]}')
        inputs = json.loads(source.read('build-records/native-source-inputs.json'))
        require(len(inputs) == manifest.get('native_source_input_count') and len(inputs) > 0,
                'The actual native input closure is empty or incomplete')
        require(len({item['path'] for item in inputs}) == len(inputs), 'Compiled native inputs are duplicated')
        for item in inputs:
            path = 'sources/SkiaSharp/externals/skia/' + relative(item['path'])
            require(member_digest(source, path) == item['sha256'],
                    f'A compiled input differs from its paired source: {item["path"]}')
        for patch in manifest['patches']:
            require(member_digest(source, 'rebuild/' + relative(patch['path'])) == patch['sha256'],
                    f'A rebuild patch differs: {patch["path"]}')
        for notice in entry['native_notice_paths']:
            require(source.read('rebuild/' + notice) == (ROOT / notice).read_bytes(),
                    f'The source archive altered an original notice: {notice}')
        script = 'scripts/build_skia_macos_without_dng.py'
        original = subprocess.check_output(['git', 'show', f'{manifest["build_commit"]}:{script}'], cwd=ROOT)
        require(source.read('rebuild/' + script) == original,
                'The paired source does not contain its exact producer script')
        downloaded_sources = json.loads(source.read('build-records/source-downloads.json'))
        require(downloaded_sources[2:] == manifest['dependencies'], 'The fixed native dependency records differ')
        for expected_source in manifest['sources'].values():
            matching = [item for item in downloaded_sources[:2]
                        if item['repository'] == expected_source['repository'] and item['commit'] == expected_source['commit']]
            require(len(matching) == 1 and matching[0]['sha256'] == expected_source['archive_sha256'],
                    'An original wrapper or Skia source record differs')
        for downloaded in [*downloaded_sources, manifest['gn_source_archive']]:
            name = pathlib.PurePosixPath(downloaded['archive_path']).name
            require(member_digest(source, 'upstream-archives/' + name) == downloaded['sha256'],
                    f'An original dependency archive differs: {name}')
        old = json.loads(source.read('build-records/official-c-exports.json'))
        new = json.loads(source.read('build-records/rebuilt-c-exports.json'))
        require(old == new and len(old) >= 100, 'Recorded native ABI exports differ')
        versions = json.loads(source.read('build-records/native-versions.json'))
        require(versions == manifest['native_versions'] and versions['official'] == versions['rebuilt'],
                'Recorded native versions differ')
        utility = manifest['upstream_native_utility']
        require(member_digest(source, 'build-records/upstream-mini-output.png') == utility['output_png_sha256'],
                'The recorded upstream PNG output differs')
        require(utility['loaded_library_sha256'] == manifest['binary']['sha256'],
                'The upstream utility did not load the exact final native library')


def verify_pair(entry, folder):
    require(entry.get('status') == 'verified', 'The native catalog entry is not pinned')
    review = entry.get('distribution_review', {})
    require(review.get('status') == 'ready' and not review.get('blocking_issues'),
            'The independent native distribution review is not ready')
    require(entry.get('source_coverage', {}).get('status') == 'complete',
            'The independent native source closure review is not complete')
    assets = (entry['archive'], entry['sources_asset'])
    expected_files = {item['name'] for item in assets} | {'native-build.json', 'NOTICE-NATIVE.txt'}
    require({path.name for path in folder.iterdir()} == expected_files,
            f'The producer artifact has unexpected or missing files: {folder}')
    for asset in assets:
        path = folder / asset['name']
        require(path.is_file() and path.stat().st_size == asset['bytes'] and digest(path) == asset['sha256'],
                f'A native asset does not match its catalog pin: {asset["name"]}')
        expected_url = f'https://github.com/{REPOSITORY}/releases/download/{TAG}/{asset["name"]}'
        require(asset['url'] == expected_url, 'A native asset URL does not match the fixed release')
    with zipfile.ZipFile(folder / assets[0]['name']) as binary:
        verify_zip(binary)
        raw = binary.read(entry['manifest']['path'])
        manifest = verify_native_manifest(entry, raw, binary.read(entry['path']), False)
        require(raw == (folder / 'native-build.json').read_bytes(), 'The loose native manifest differs')
        for notice in entry['native_notice_paths']:
            require(binary.read(notice) == (ROOT / notice).read_bytes(),
                    f'The native ZIP altered an original notice: {notice}')
        notice = binary.read(entry['notice']['path'])
        require(notice == (folder / 'NOTICE-NATIVE.txt').read_bytes(), 'The loose product notice differs')
        require(all(item.encode('utf-8') in notice for item in entry['required_product_acknowledgments']),
                'A required native product acknowledgment is missing')
    verify_source(entry, manifest, folder / assets[1]['name'])
    return manifest, raw, [folder / item['name'] for item in assets]


def draft(release_id, commit, empty):
    release = api(f'releases/{release_id}')
    require(release.get('id') == release_id and release.get('tag_name') == TAG
            and release.get('target_commitish') == commit and release.get('draft') is True
            and release.get('prerelease') is True and isinstance(release.get('assets'), list),
            'The fixed draft identity, target or publication state changed')
    pages = json.loads(gh('api', '--paginate', '--slurp', f'repos/{REPOSITORY}/releases?per_page=100'))
    matches = [item for page in pages for item in page if item.get('tag_name') == TAG]
    require(len(matches) == 1 and matches[0]['id'] == release_id, 'The fixed tag has an ambiguous release')
    require(api(f'commits/{urllib.parse.quote(TAG, safe="")}')['sha'] == commit,
            'The fixed native tag does not point at the producer commit')
    if empty:
        require(not release['assets'], 'Refusing to upload to a nonempty native draft')
    return release


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-id', required=True, type=int)
    parser.add_argument('--release-id', required=True, type=int)
    parser.add_argument('--input-dir', required=True, type=pathlib.Path)
    parser.add_argument('--output-dir', required=True, type=pathlib.Path)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args()
    require(args.run_id > 0 and args.release_id > 0, 'The producer run and draft IDs must be positive')
    require(os.environ.get('GITHUB_REPOSITORY', REPOSITORY) == REPOSITORY, 'Unexpected repository')
    catalog = json.loads((ROOT / 'third-party/components.json').read_text(encoding='utf-8'))
    entries = {item['runtime_identifier']: item for item in catalog['native_overrides']
               if item['id'] in {'skiasharp-2.88.9-nodng-' + rid for rid in RIDS}}
    require(set(entries) == set(RIDS), 'Both macOS native catalog entries are required')
    prepared = [verify_pair(entries[rid], args.input_dir / rid) for rid in RIDS]
    commits = {manifest['build_commit'] for manifest, _, _ in prepared}
    require(len(commits) == 1, 'The two architectures were produced from different source commits')
    commit = commits.pop()
    require(re.fullmatch(r'[0-9a-f]{40}', commit), 'The producer commit is invalid')
    producer = api(f'actions/runs/{args.run_id}')
    require(producer.get('status') == 'completed' and producer.get('conclusion') == 'success'
            and producer.get('head_sha') == commit and producer.get('path') == PRODUCER
            and producer.get('repository', {}).get('full_name') == REPOSITORY,
            'The selected run is not the successful exact native producer')
    original = draft(args.release_id, commit, empty=True)
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    require(not list(output.iterdir()), 'The native upload directory must be empty')
    for rid, (_, raw, paths) in zip(RIDS, prepared):
        for path in paths:
            shutil.copyfile(path, output / path.name)
        (output / f'native-build-{rid}.json').write_bytes(raw)
    assets = sorted(output.iterdir())
    (output / 'SHA256SUMS').write_text(''.join(f'{digest(path)}  {path.name}\n' for path in assets), encoding='utf-8')
    assets = sorted(output.iterdir())
    require(len(assets) == 7, 'The native release requires exactly seven assets')
    if args.validate_only:
        print(json.dumps({'status': 'verified_not_uploaded', 'tag': TAG, 'producer_commit': commit,
                          'assets': [path.name for path in assets]}, indent=2))
        return
    # Recheck immediately before the only mutation. gh upload has no --clobber.
    draft(args.release_id, commit, empty=True)
    gh('release', 'upload', TAG, *assets, '--repo', REPOSITORY)
    uploaded = draft(args.release_id, commit, empty=False)
    for key in ('name', 'body'):
        require(uploaded.get(key) == original.get(key), 'The draft title or description changed during upload')
    expected = {path.name: path for path in assets}
    require({item['name']: item['size'] for item in uploaded['assets']}
            == {name: path.stat().st_size for name, path in expected.items()},
            'The uploaded native asset names or sizes differ')
    verified = {}
    for item in uploaded['assets']:
        require(item.get('state') == 'uploaded', 'A native upload is incomplete')
        wanted = 'sha256:' + digest(expected[item['name']])
        if item.get('digest'):
            require(item['digest'] == wanted, f'A server native asset digest differs: {item["name"]}')
        else:
            with tempfile.TemporaryDirectory() as temporary:
                downloaded = pathlib.Path(temporary) / 'asset'
                with downloaded.open('wb') as stream:
                    subprocess.run(['gh', 'api', f'repos/{REPOSITORY}/releases/assets/{item["id"]}',
                                    '-H', 'Accept: application/octet-stream'], stdout=stream, check=True)
                require(downloaded.stat().st_size == item['size'] and 'sha256:' + digest(downloaded) == wanted,
                        f'A re-downloaded native draft asset differs: {item["name"]}')
        verified[item['name']] = wanted
    print(json.dumps({'status': 'uploaded_and_verified_draft_not_published', 'tag': TAG,
                      'release_id': args.release_id, 'producer_commit': commit, 'asset_digests': verified}, indent=2))


if __name__ == '__main__':
    main()
