"""Build the pinned SkiaSharp 2.88.9 Windows x64 library without RAW/DNG.

This is a native build producer, not a license approval. Distribution is gated
separately by third-party/components.json. No App dependency upgrades are made.
SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import concurrent.futures
import ctypes
import json
import os
import pathlib
import re
import shutil
import struct
import subprocess
import sys
import tarfile
import time
import urllib.request
import zipfile

from collect_third_party_sources import fetch, file_digest


ROOT = pathlib.Path(__file__).resolve().parents[1]
LOCK_PATH = ROOT / 'third-party/skia-no-dng-build.json'
PATCHES = (
    'third-party/patches/skiasharp-2.88.9-windows-disable-raw.patch',
    'third-party/patches/mono-skia-4bed-skip-dng-piex-downloads.patch',
)
BINARY_NAME = 'SkiaSharp-2.88.9-no-dng-win-x64.zip'
SOURCE_NAME = 'SkiaSharp-2.88.9-no-dng-win-x64-sources.zip'
FORBIDDEN = re.compile(r'(?:dng_sdk|(?:[/\\]|::)piex(?:[/\\:]|$)|SkRawCodec|SK_CODEC_DECODES_RAW)', re.I)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def run(command, cwd, log, capture=False):
    """Preserve subprocess output without printing credentials or the environment."""
    command = [str(value) for value in command]
    print('Running: ' + subprocess.list2cmdline(command), flush=True)
    log.parent.mkdir(parents=True, exist_ok=True)
    if capture:
        result = subprocess.run(command, cwd=cwd, capture_output=True, encoding='utf-8', errors='replace')
        log.write_text(result.stdout, encoding='utf-8')
        if result.stderr:
            log.with_suffix(log.suffix + '.stderr').write_text(result.stderr, encoding='utf-8')
    else:
        with log.open('w', encoding='utf-8') as stream:
            result = subprocess.run(command, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        tail = log.read_text(encoding='utf-8', errors='replace')[-10000:]
        raise RuntimeError(f'Command failed ({result.returncode}); see {log}\n{tail}')
    return result.stdout if capture else ''


def extract_source(archive_path, destination, strip_root):
    """Python's data filter rejects traversals and escaping links before extraction."""
    if destination.exists():
        raise ValueError(f'Refusing to merge a previous source checkout: {destination}')
    temporary = destination.with_name(destination.name + '.extract')
    temporary.mkdir(parents=True)
    with tarfile.open(archive_path) as archive:
        archive.extractall(temporary, filter='data')
    if strip_root:
        entries = list(temporary.iterdir())
        if len(entries) != 1 or not entries[0].is_dir():
            raise ValueError(f'Archive does not have one expected root directory: {archive_path}')
        entries[0].replace(destination)
        temporary.rmdir()
    else:
        temporary.replace(destination)


def download_tool(tool, name, cache):
    path = cache / name
    if path.exists() and file_digest(path) == tool['sha256']:
        return path
    temporary = path.with_suffix(path.suffix + '.part')
    last_error = None
    for attempt in range(3):
        try:
            request = urllib.request.Request(tool['url'], headers={'User-Agent': 'DictationAssistant-native-builder/1'})
            with urllib.request.urlopen(request, timeout=120) as response, temporary.open('wb') as target:
                if not response.geturl().startswith('https://'):
                    raise ValueError('Build tool download redirected outside HTTPS')
                total = 0
                while block := response.read(1024 * 1024):
                    total += len(block)
                    if total > tool['bytes']:
                        raise ValueError('Build tool exceeded its pinned byte count')
                    target.write(block)
            if temporary.stat().st_size != tool['bytes'] or file_digest(temporary) != tool['sha256']:
                raise ValueError(f'Build tool checksum mismatch: {name}')
            temporary.replace(path)
            return path
        except Exception as error:
            last_error = error
            temporary.unlink(missing_ok=True)
            if attempt < 2:
                time.sleep(2 * (attempt + 1))
    raise RuntimeError(f'Cannot fetch the pinned official build tool {name}: {last_error}')


def fetch_source(source, cache):
    last_error = None
    for attempt in range(3):
        try:
            # Gitiles gzip metadata varies; the lock instead pins every tar
            # member, file byte, mode and symlink with tar-members-v1.
            return fetch(source, cache, 512 * 1024 * 1024)
        except Exception as error:
            last_error = error
            if attempt < 2:
                time.sleep(2 * (attempt + 1))
    raise RuntimeError(f'Cannot obtain required native source {source["repository"]}@{source["commit"]}: {last_error}')


def prepare_sources(lock, work, cache, logs):
    sources = list(lock['sources'].values()) + lock['dependencies']
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        downloaded = list(executor.map(lambda source: fetch_source(source, cache), sources))
    by_commit = {item['commit']: item for item in downloaded}
    wrapper = work / 'SkiaSharp'
    extract_source(cache / by_commit[lock['sources']['wrapper']['commit']]['archive_path'], wrapper, True)
    skia = wrapper / 'externals/skia'
    # GitHub's parent archive contains an empty gitlink directory.
    if skia.exists():
        skia.rmdir()
    extract_source(cache / by_commit[lock['sources']['skia']['commit']]['archive_path'], skia, True)
    patches = []
    for relative, directory in zip(PATCHES, (wrapper, skia)):
        patch = ROOT / relative
        run(['git', 'apply', '--check', patch], directory, logs / (patch.stem + '-check.log'))
        run(['git', 'apply', patch], directory, logs / (patch.stem + '-apply.log'))
        patches.append({'path': relative, 'sha256': file_digest(patch)})
    windows_script = (wrapper / 'native/windows/build.cake').read_text(encoding='utf-8')
    active_deps = '\n'.join(line for line in (skia / 'DEPS').read_text(encoding='utf-8').splitlines()
                            if not line.lstrip().startswith('#'))
    if ('skia_use_dng_sdk=true' in windows_script or 'skia_use_piex=true' in windows_script
            or 'skia_use_dng_sdk=false' not in windows_script or 'skia_use_piex=false' not in windows_script
            or '"third_party/externals/dng_sdk"' in active_deps or '"third_party/externals/piex"' in active_deps):
        raise ValueError('The reviewed patches did not actually disable RAW/DNG in the prepared source files')
    for source in lock['dependencies']:
        archive = by_commit[source['commit']]
        destination = skia / source['path']
        if destination.exists():
            destination.rmdir()
        extract_source(cache / archive['archive_path'], destination, source.get('strip_root', False))
    for name in ('dng_sdk', 'piex'):
        if (skia / 'third_party/externals' / name).exists():
            raise ValueError(f'Unused restricted source was unexpectedly fetched: {name}')
    # The upstream GN file silently omits GIF when this file is missing.
    if not (skia / 'third_party/externals/libgifcodec/libgifcodec.gni').is_file():
        raise ValueError('Required GIF codec build description is absent')
    write_json(logs / 'source-downloads.json', downloaded)
    return wrapper, skia, patches, downloaded


def pe_exports(path):
    data = path.read_bytes()
    offset = struct.unpack_from('<I', data, 0x3c)[0]
    if data[:2] != b'MZ' or data[offset:offset + 4] != b'PE\0\0':
        raise ValueError(f'Not a PE library: {path}')
    machine, sections = struct.unpack_from('<HH', data, offset + 4)
    optional_size = struct.unpack_from('<H', data, offset + 20)[0]
    optional = offset + 24
    if machine != 0x8664 or struct.unpack_from('<H', data, optional)[0] != 0x20b:
        raise ValueError(f'Expected an x64 PE32+ library: {path}')
    section_table = optional + optional_size

    def file_offset(rva):
        for index in range(sections):
            entry = section_table + index * 40
            virtual_size, address, raw_size, raw = struct.unpack_from('<IIII', data, entry + 8)
            if address <= rva < address + max(virtual_size, raw_size):
                return raw + rva - address
        raise ValueError(f'Invalid PE RVA {rva}')

    export_rva, export_size = struct.unpack_from('<II', data, optional + 112)
    directory = file_offset(export_rva)
    ordinal_base = struct.unpack_from('<I', data, directory + 16)[0]
    function_count, name_count = struct.unpack_from('<II', data, directory + 20)
    functions = file_offset(struct.unpack_from('<I', data, directory + 28)[0])
    names = file_offset(struct.unpack_from('<I', data, directory + 32)[0])
    ordinals = file_offset(struct.unpack_from('<I', data, directory + 36)[0])
    names_by_index = {}
    for index in range(name_count):
        start = file_offset(struct.unpack_from('<I', data, names + index * 4)[0])
        ordinal = struct.unpack_from('<H', data, ordinals + index * 2)[0]
        names_by_index[ordinal] = data[start:data.index(b'\0', start)].decode('ascii')
    exports = []
    for index in range(function_count):
        rva = struct.unpack_from('<I', data, functions + index * 4)[0]
        forwarder = None
        if export_rva <= rva < export_rva + export_size:
            start = file_offset(rva)
            forwarder = data[start:data.index(b'\0', start)].decode('ascii')
        exports.append({'ordinal': ordinal_base + index, 'name': names_by_index.get(index),
                        'present': bool(rva), 'forwarder': forwarder})
    return {'machine': '0x8664', 'function_count': function_count,
            'names': sorted(names_by_index.values()), 'exports': exports}


def native_version(path):
    library = ctypes.WinDLL(str(path.resolve()))
    library.sk_version_get_string.restype = ctypes.c_char_p
    return {
        'milestone': library.sk_version_get_milestone(),
        'increment': library.sk_version_get_increment(),
        'string': library.sk_version_get_string().decode('ascii'),
    }


def gn_text(arguments):
    return '\n'.join(f'{key} = {json.dumps(value)}' for key, value in arguments.items()) + '\n'


def run_upstream_mini(wrapper, binary, work, logs):
    """Run the unchanged upstream native utility, without adding test sources."""
    mini = work / 'upstream-native-mini'
    mini.mkdir()
    origin = wrapper / 'utils/NativeLibraryMiniTest'
    shutil.copyfile(origin / 'source/Program.cs', mini / 'Program.cs')
    shutil.copyfile(origin / 'docker/NativeLibraryMiniTest.csproj', mini / 'NativeLibraryMiniTest.csproj')
    run(['dotnet', 'build', mini / 'NativeLibraryMiniTest.csproj', '-c', 'Release',
         '-p:TargetFramework=net10.0', '-o', mini / 'bin', '--nologo'], mini, logs / 'upstream-mini-build.log')
    shutil.copyfile(binary, mini / 'bin/libSkiaSharp.dll')
    run(['dotnet', mini / 'bin/NativeLibraryMiniTest.dll'], mini, logs / 'upstream-mini-run.log')
    png = mini / 'output.png'
    if not png.is_file() or png.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('The unchanged upstream native utility did not produce a PNG')
    shutil.copyfile(png, logs / 'upstream-mini-output.png')
    return {'program_sha256': file_digest(mini / 'Program.cs'),
            'original_program_path': 'utils/NativeLibraryMiniTest/source/Program.cs',
            'output_png_sha256': file_digest(png)}


def native_notice_paths(lock):
    catalog = json.loads((ROOT / 'third-party/components.json').read_text(encoding='utf-8'))
    entry = next(item for item in catalog.get('native_overrides', []) if item['id'] == lock['artifact_id'])
    paths = entry.get('native_notice_paths', [])
    if not paths or not all(isinstance(path, str) and (ROOT / path).is_file() for path in paths):
        raise ValueError('The reviewed native license inventory is incomplete; refusing to make a native ZIP')
    return paths


def add_zip_tree(archive, source, prefix, excluded=()):
    for path in sorted(source.rglob('*')):
        relative = path.relative_to(source)
        if any(part in excluded for part in relative.parts):
            continue
        target = str(pathlib.PurePosixPath(prefix, *relative.parts))
        if path.is_symlink():
            info = zipfile.ZipInfo(target)
            info.create_system = 3
            info.external_attr = 0o120777 << 16
            archive.writestr(info, os.readlink(path))
        elif path.is_file():
            archive.write(path, target)


def source_bundle(output, wrapper, downloaded, cache, metadata, notice_paths):
    destination = output / SOURCE_NAME
    with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        add_zip_tree(archive, wrapper, 'sources/SkiaSharp', excluded=('.git', 'out'))
        for relative in (*PATCHES, 'third-party/skia-no-dng-build.json', 'third-party/components.json',
                         'scripts/build_skia_without_dng.py', 'scripts/collect_third_party_sources.py',
                         '.github/workflows/build-skia-without-dng.yml', 'docs/NATIVE_BUILD.md',
                         'LICENSE.md', 'NOTICE.md', *notice_paths):
            archive.write(ROOT / relative, 'rebuild/' + relative)
        # Original source archives allow a rebuild to reuse --cache-dir without
        # relying on Gitiles availability; generated source changes are above.
        for source in downloaded:
            path = cache / source['archive_path']
            archive.write(path, 'upstream-archives/' + path.name)
            archive.write(cache / (path.name + '.download.json'), 'upstream-archives/' + path.name + '.download.json')
        archive.writestr('BUILD_PROVENANCE.json', json.dumps(metadata, indent=2) + '\n')
    return {'name': destination.name, 'sha256': file_digest(destination), 'bytes': destination.stat().st_size}


def dependency_hashes(skia, lock):
    return {path.relative_to(skia).as_posix(): file_digest(path)
            for source in lock['dependencies'] for path in (skia / source['path']).rglob('*')
            if path.is_file() and not path.is_symlink()}


def build(args):
    lock = json.loads(LOCK_PATH.read_text(encoding='utf-8'))
    output = args.output_dir.resolve()
    work = args.work_dir.resolve()
    cache = args.cache_dir.resolve()
    logs = output / 'logs'
    for directory in (output, work, cache, logs):
        directory.mkdir(parents=True, exist_ok=True)
    wrapper, skia, patches, downloaded = prepare_sources(lock, work, cache, logs)
    write_json(output / 'source-preparation.json', {'sources': downloaded, 'patches': patches})
    if args.prepare_only:
        print('Exact source trees prepared and patches applied; no native build was attempted.')
        return
    if sys.platform != 'win32':
        raise RuntimeError('A real Windows x64 runner is required; source preparation is the only Linux mode')
    if not args.msvc_path or not args.llvm_dir:
        raise ValueError('--msvc-path and --llvm-dir are required for the Windows native build')
    llvm = args.llvm_dir.resolve()
    tools = work / 'tools'
    tools.mkdir()
    for tool_name in ('gn', 'ninja'):
        archive = download_tool(lock['tools'][tool_name], tool_name + '-windows.zip', cache)
        with zipfile.ZipFile(archive) as package:
            package.extract(tool_name + '.exe', tools)
    gn = tools / 'gn.exe'
    ninja = tools / 'ninja.exe'
    compiler = llvm / 'bin/clang-cl.exe'
    compiler_version = run([compiler, '--version'], work, logs / 'clang-version.txt', capture=True)
    if not re.search(r'clang version 12\.0\.0\b', compiler_version):
        raise ValueError('The actual compiler is not the pinned LLVM 12.0.0 toolchain')
    run([ninja, '--version'], work, logs / 'ninja-version.txt')
    run([gn, '--version'], work, logs / 'gn-version.txt')
    arguments = {
        'target_os': 'win', 'target_cpu': 'x64', 'skia_enable_fontmgr_win_gdi': False,
        'skia_use_dng_sdk': False, 'skia_use_piex': False, 'skia_use_icu': False,
        'skia_use_sfntly': False, 'skia_use_system_expat': False,
        'skia_use_system_libjpeg_turbo': False, 'skia_use_system_libpng': False,
        'skia_use_system_libwebp': False, 'skia_use_system_zlib': False,
        'skia_enable_skottie': True, 'skia_use_vulkan': True,
        'clang_win': llvm.as_posix(), 'clang_win_version': '12.0.0',
        'win_vc': args.msvc_path.resolve().as_posix(), 'win_vcvars_version': '14.2',
        'extra_cflags': ['-DSKIA_C_DLL', '/MT', '/EHsc', '/Z7', '-D_HAS_AUTO_PTR_ETC=1'],
        'extra_ldflags': ['/DEBUG:FULL', '/DEBUGTYPE:CV,FIXUP'],
        'skia_enable_tools': False, 'is_official_build': True,
    }
    out = skia / 'out/windows/x64'
    out.mkdir(parents=True)
    (out / 'args.gn').write_text(gn_text(arguments), encoding='utf-8')
    shutil.copyfile(out / 'args.gn', logs / 'args.gn')
    before_generation = dependency_hashes(skia, lock)
    run([gn, 'gen', out, '--script-executable=' + sys.executable], skia, logs / 'gn-gen.log')
    after_generation = dependency_hashes(skia, lock)
    transformations = [{'path': path, 'before_sha256': value, 'after_sha256': after_generation[path]}
                       for path, value in before_generation.items() if after_generation.get(path) != value]
    write_json(logs / 'gn-source-transformations.json', transformations)
    run([gn, 'args', out, '--list', '--short'], skia, logs / 'gn-effective-args.txt')
    graph = run([gn, 'desc', out, '*', '--format=json'], skia, logs / 'gn-graph.json', capture=True)
    json.loads(graph)
    dependencies = run([gn, 'desc', out, '//:SkiaSharp', 'deps', '--all'], skia, logs / 'gn-transitive-deps.txt', capture=True)
    raw = json.loads(run([gn, 'desc', out, '//:raw', '--format=json'], skia, logs / 'gn-raw.json', capture=True))
    if any(value.get('sources') or value.get('deps') for value in raw.values()) or FORBIDDEN.search(dependencies):
        raise ValueError('DNG/PIEX remain in the generated native dependency graph')
    commands = run([ninja, '-C', out, '-t', 'commands', 'SkiaSharp'], skia, logs / 'ninja-commands.txt', capture=True)
    if FORBIDDEN.search(commands):
        raise ValueError('DNG/PIEX remain in generated native build commands')
    run([ninja, '-C', out, '-j', str(args.jobs), 'SkiaSharp'], skia, logs / 'ninja-build.log')
    inputs = run([ninja, '-C', out, '-t', 'inputs', 'SkiaSharp'], skia, logs / 'ninja-inputs.txt', capture=True)
    if FORBIDDEN.search(inputs):
        raise ValueError('DNG/PIEX remain in actual native build inputs')
    source_inputs = []
    for name in inputs.splitlines():
        path = (out / name.strip()).resolve()
        if path.is_relative_to(skia) and not path.is_relative_to(out):
            if not path.is_file():
                raise ValueError(f'A native source input is unavailable for the source bundle: {path}')
            source_inputs.append({'path': path.relative_to(skia).as_posix(), 'sha256': file_digest(path)})
    if not source_inputs:
        raise ValueError('Ninja did not report any actual native source inputs')
    for source in lock['dependencies']:
        if not any(item['path'].startswith(source['path'] + '/') for item in source_inputs):
            raise ValueError(f'Required retained codec dependency is absent from the compiled input graph: {source["path"]}')
    write_json(logs / 'native-source-inputs.json', source_inputs)
    binary = out / 'libSkiaSharp.dll'
    official_package = cache / 'skiasharp.nativeassets.win32.2.88.9.nupkg'
    official_tool = {
        'url': 'https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.win32/2.88.9/skiasharp.nativeassets.win32.2.88.9.nupkg',
        **lock['official_package'],
    }
    official_package = download_tool(official_tool, official_package.name, cache)
    official_dir = work / 'official'
    official_dir.mkdir()
    with zipfile.ZipFile(official_package) as package:
        official = official_dir / 'libSkiaSharp.dll'
        official.write_bytes(package.read('runtimes/win-x64/native/libSkiaSharp.dll'))
    before = pe_exports(official)
    after = pe_exports(binary)
    write_json(logs / 'official-exports.json', before)
    write_json(logs / 'rebuilt-exports.json', after)
    if before != after:
        raise ValueError('Rebuilt native ABI exports differ from the official 2.88.9 DLL')
    versions = {'official': native_version(official), 'rebuilt': native_version(binary)}
    write_json(logs / 'native-versions.json', versions)
    if versions['official'] != versions['rebuilt']:
        raise ValueError('Native milestone/increment/version differs from official 2.88.9')
    image = binary.read_bytes()
    if any(marker in image for marker in (b'dng_', b'SkDng', b'SkRawCodec', b'piex::')):
        raise ValueError('DNG/PIEX code symbols remain in the rebuilt DLL')
    upstream_mini = run_upstream_mini(wrapper, binary, work, logs)
    build_commit = args.build_commit or run(['git', 'rev-parse', 'HEAD'], ROOT, logs / 'build-commit.txt', capture=True).strip()
    if not re.fullmatch('[0-9a-f]{40}', build_commit):
        raise ValueError('A complete build-script Git commit is required for provenance')
    metadata = {
        'schema_version': 1, 'artifact_id': lock['artifact_id'], 'runtime_identifier': 'win-x64',
        'status': 'verified', 'build_commit': build_commit,
        'binary': {'path': 'libSkiaSharp.dll', 'sha256': file_digest(binary), 'bytes': binary.stat().st_size, 'pe_machine': '0x8664'},
        'sources': {key: {'repository': source['repository'], 'commit': source['commit'], 'archive_sha256': source['sha256']} for key, source in lock['sources'].items()},
        'dependencies': downloaded[2:], 'patches': patches, 'gn_args': arguments,
        'tools': lock['tools'], 'compiler_binary_sha256': file_digest(compiler),
        'native_versions': versions, 'upstream_native_utility': upstream_mini,
        'gn_source_transformations': transformations,
        'native_source_input_count': len(source_inputs),
        'verification': {'no_dng_sources_in_graph': True, 'no_dng_commands': True,
                         'no_dng_binary_symbols': True, 'exports_match_official': True,
                         'smoke_tests': {'native_abi_version_and_load': True, 'upstream_png_encode': True}},
        'unverified_features': ['Dedicated JPEG/WebP/GIF decoding checks were not run; no new test harness was introduced.'],
        'distribution_review': 'Separate from technical verification; this manifest never grants distribution approval.',
    }
    notice_paths = native_notice_paths(lock)
    metadata['source_archive'] = source_bundle(output, wrapper, downloaded, cache, metadata, notice_paths)
    write_json(output / 'native-build.json', metadata)
    shutil.copyfile(binary, output / 'libSkiaSharp.dll')
    if (out / 'libSkiaSharp.pdb').exists():
        shutil.copyfile(out / 'libSkiaSharp.pdb', output / 'libSkiaSharp.pdb')
    notice = (
        'SkiaSharp 2.88.9, rebuilt for Windows x64 without RAW/DNG and PIEX.\n'
        'Original copyrights, license grants and disclaimers are preserved in LICENSES/.\n'
        'This software is based in part on the work of the Independent JPEG Group.\n'
        'The libgifcodec MPL/GPL/LGPL alternative is elected under GPL-2.0-or-later, version 3; '
        'the original alternatives and BSD portions remain in the supplied notices.\n'
        f'Corresponding native sources: {SOURCE_NAME}\n'
        f'Source archive SHA-256: {metadata["source_archive"]["sha256"]}\n'
        'Exact revisions, patches, tool origins, generated GN arguments and verification are in native-build.json.\n'
        'Technical build verification is separate from distribution/license approval.\n'
    )
    (output / 'NOTICE-NATIVE.txt').write_text(notice, encoding='utf-8')
    with zipfile.ZipFile(output / BINARY_NAME, 'w', compression=zipfile.ZIP_DEFLATED) as package:
        for name in ('native-build.json', 'libSkiaSharp.dll', 'NOTICE-NATIVE.txt'):
            if (output / name).is_file():
                package.write(output / name, name)
        for relative in notice_paths:
            package.write(ROOT / relative, relative if relative.startswith('LICENSES/') else 'LICENSES/' + relative)
    write_json(output / 'artifact-hashes.json', {
        'binary_archive': {'name': BINARY_NAME, 'sha256': file_digest(output / BINARY_NAME), 'bytes': (output / BINARY_NAME).stat().st_size},
        'manifest_sha256': file_digest(output / 'native-build.json'), 'source_archive': metadata['source_archive'],
    })
    print(json.dumps({'binary': metadata['binary'], 'source_archive': metadata['source_archive']}, indent=2), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work-dir', type=pathlib.Path, default=ROOT / 'artifacts/skia-native-work')
    parser.add_argument('--output-dir', type=pathlib.Path, default=ROOT / 'artifacts/skia-native')
    parser.add_argument('--cache-dir', type=pathlib.Path, default=ROOT / 'artifacts/skia-native-cache')
    parser.add_argument('--msvc-path', type=pathlib.Path)
    parser.add_argument('--llvm-dir', type=pathlib.Path)
    parser.add_argument('--build-commit')
    parser.add_argument('--jobs', type=int, choices=range(1, 17), default=2)
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args()
    try:
        build(args)
    except Exception as error:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        write_json(args.output_dir / 'native-build-failure.json', {'status': 'failed', 'error': str(error)})
        parser.exit(1, f'Native build failed: {error}\n')


if __name__ == '__main__':
    main()
