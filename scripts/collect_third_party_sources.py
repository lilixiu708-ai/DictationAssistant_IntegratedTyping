"""Collect fixed upstream source archives for the release's actual dependency set.

The index retains native submodule/DEPS provenance. A GitHub archive does not
automatically include submodules; additional archives must be recorded explicitly.
SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import re
import tarfile
import urllib.parse
import urllib.request
import zipfile


def file_digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def archive_filename(source):
    name = source.get('name') or urllib.parse.urlsplit(source.get('repository', source['url'])).path.rstrip('/').split('/')[-1]
    name = re.sub(r'[^A-Za-z0-9._-]', '-', name)
    revision = re.sub(r'[^A-Za-z0-9._-]', '-', source['revision'])
    suffix = '.zip'
    for extension in ('.tar.gz', '.tar.xz', '.tar.bz2', '.tgz', '.zip'):
        if name.endswith(extension) or urllib.parse.urlsplit(source['url']).path.endswith(extension):
            name = name.removesuffix(extension)
            suffix = extension
            break
    if '/tar.gz/' in source['url']:
        suffix = '.tar.gz'
    return f'{name}-{revision[:16]}{suffix}'


def validate_archive(path, expected_tree=None):
    if zipfile.is_zipfile(path):
        if expected_tree:
            raise ValueError('The canonical tar-member tree check cannot be applied to a ZIP')
        with zipfile.ZipFile(path) as archive:
            if not archive.infolist():
                raise ValueError(f'Upstream source archive is empty: {path.name}')
            # Check the complete response while avoiding extraction of untrusted
            # paths and symlinks into the checkout.
            if archive.testzip() is not None:
                raise ValueError(f'Corrupted upstream source archive: {path.name}')
    elif tarfile.is_tarfile(path):
        with tarfile.open(path) as archive:
            found = False
            tree = []
            for member in archive:
                found = True
                content = None
                if member.isfile():
                    content_hash = hashlib.sha256()
                    with archive.extractfile(member) as stream:
                        while block := stream.read(1024 * 1024):
                            content_hash.update(block)
                    content = content_hash.hexdigest()
                if expected_tree:
                    # Gitiles generates per-request mtimes/PAX mtime headers.
                    # Preserve source paths, types, permissions, sizes, links
                    # and every file byte while excluding those timestamps.
                    tree.append({'path': member.name, 'type': member.type.decode('ascii'),
                                 'mode': member.mode, 'size': member.size,
                                 'linkname': member.linkname, 'content_sha256': content})
            if not found:
                raise ValueError(f'Upstream source archive is empty: {path.name}')
            if expected_tree:
                tree.sort(key=lambda item: item['path'])
                encoded = json.dumps(tree, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
                actual = hashlib.sha256(encoded).hexdigest()
                if actual != expected_tree:
                    raise ValueError(f'Upstream source content-tree checksum mismatch: {path.name}')
    else:
        raise ValueError(f'Upstream URL did not return a source archive: {path.name}')


def fetch(source, output, limit):
    url = source['url']
    if urllib.parse.urlsplit(url).scheme != 'https':
        raise ValueError(f'Upstream archives require HTTPS: {url}')
    revision = source.get('revision')
    if not revision or revision.lower() in ('head', 'main', 'master', 'latest'):
        raise ValueError(f'Upstream archive has no fixed revision: {url}')
    filename = archive_filename(source)
    path = output / filename
    expected = source.get('sha256')
    expected_tree = source.get('content_tree_sha256')
    if expected_tree and source.get('content_tree_schema') != 'tar-members-v1':
        raise ValueError(f'Unsupported source content-tree schema: {url}')
    # An interrupted prior download is never accepted as a valid cached archive.
    sidecar = output / (filename + '.download.json')
    cached = False
    if path.is_file() and sidecar.is_file():
        metadata = json.loads(sidecar.read_text(encoding='utf-8'))
        cached = metadata.get('url') == url and metadata.get('sha256') == file_digest(path)
    if not cached:
        temporary = output / (filename + '.part')
        try:
            request = urllib.request.Request(url, headers={'User-Agent': 'DictationAssistant-license-source-collector/1'})
            # Use the normal TLS trust store and verified redirects. Never turn
            # off certificate checks to obtain release source materials.
            with urllib.request.urlopen(request, timeout=120) as response, temporary.open('wb') as destination:
                if urllib.parse.urlsplit(response.geturl()).scheme != 'https':
                    raise ValueError(f'Source archive redirected outside HTTPS: {url}')
                content_length = response.headers.get('Content-Length')
                if content_length and int(content_length) > limit:
                    raise ValueError(f'Source archive exceeds the configured size limit: {url}')
                total = 0
                while block := response.read(1024 * 1024):
                    total += len(block)
                    if total > limit:
                        raise ValueError(f'Source archive exceeds the configured size limit: {url}')
                    destination.write(block)
                if content_length and total != int(content_length):
                    raise ValueError(f'Incomplete upstream source response: {url}')
            validate_archive(temporary, expected_tree)
            actual = file_digest(temporary)
            if expected and actual.lower() != expected.lower():
                raise ValueError(f'Upstream source checksum mismatch: {url}')
            temporary.replace(path)
            sidecar.write_text(json.dumps({'url': url, 'sha256': actual}, indent=2) + '\n', encoding='utf-8')
        finally:
            temporary.unlink(missing_ok=True)
    actual = file_digest(path)
    if expected and actual.lower() != expected.lower():
        raise ValueError(f'Cached upstream source checksum mismatch: {url}')
    if cached and expected_tree:
        validate_archive(path, expected_tree)
    return {**source, 'archive_path': filename, 'sha256': actual, 'bytes': path.stat().st_size}


def collect(manifest_path, output, workers=4, maximum_archive_mib=1024):
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    output.mkdir(parents=True, exist_ok=True)
    sources = manifest.get('archives', [])
    bundled = [source for source in sources if source.get('delivery', 'bundled') == 'bundled']
    filenames = [archive_filename(source) for source in bundled]
    if len(filenames) != len(set(filenames)):
        raise ValueError('Different upstream sources would overwrite the same archive filename')
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        results = list(executor.map(lambda source: fetch(source, output, maximum_archive_mib * 1024 * 1024), bundled))
    index = {
        **manifest,
        'archives': results + [source for source in sources if source.get('delivery', 'bundled') != 'bundled'],
        'download_verification': 'HTTPS with normal certificate validation; each downloaded archive is recorded with its actual SHA-256. Fixed source revisions are listed explicitly. Reviewed expected archive hashes are enforced where specified. Gitiles responses with variable timestamp metadata additionally use an explicitly recorded canonical tar-members-v1 content-tree hash, preserving every source file byte, path, type, permission, size and link target.',
    }
    destination = output / 'SOURCE_ARCHIVES.json'
    destination.write_text(json.dumps(index, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Collected {len(results)} fixed source archives ({sum(item["bytes"] for item in results):,} bytes): {destination}')
    return index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=pathlib.Path, required=True)
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    parser.add_argument('--workers', type=int, default=4, choices=range(1, 9))
    parser.add_argument('--maximum-archive-mib', type=int, default=1024)
    args = parser.parse_args()
    if args.maximum_archive_mib < 1:
        parser.error('--maximum-archive-mib must be positive')
    try:
        collect(args.manifest.resolve(), args.output_dir.resolve(), args.workers, args.maximum_archive_mib)
    except Exception as error:
        parser.exit(1, f'Upstream source collection failed: {error}\n')


if __name__ == '__main__':
    main()
