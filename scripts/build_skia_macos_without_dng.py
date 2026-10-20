"""Build and verify pinned SkiaSharp 2.88.9 for one native macOS architecture.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
The producer uploads diagnostics only; publication is a separate reviewed step.
"""
import argparse
import ctypes
import json
import os
import pathlib
import platform
import re
import shutil
import struct
import subprocess
import sys
import zipfile

from collect_third_party_sources import file_digest
from build_skia_without_dng import (
    FORBIDDEN, PATCHES, ROOT, add_zip_tree, dependency_hashes, download_tool,
    extract_source, fetch_source, gn_text, native_notice_paths, prepare_sources,
    run, write_json,
)

LOCK_PATH = ROOT / 'third-party/skia-no-dng-macos-build.json'
CPU = {'x64': (0x01000007, 'x86_64'), 'arm64': (0x0100000C, 'arm64')}


def macho_slice(path, cpu):
    data = path.read_bytes()
    if data[:4] == b'\xca\xfe\xba\xbe':
        count = struct.unpack_from('>I', data, 4)[0]
        for index in range(count):
            kind, _, offset, size, _ = struct.unpack_from('>IIIII', data, 8 + index * 20)
            if kind == cpu:
                return data[offset:offset + size]
        raise ValueError(f'The required architecture is absent: {path}')
    if data[:4] != b'\xcf\xfa\xed\xfe' or struct.unpack_from('<I', data, 4)[0] != cpu:
        raise ValueError(f'The library is not the expected 64-bit Mach-O architecture: {path}')
    return data


def c_exports(path, cpu):
    data = macho_slice(path, cpu)
    count = struct.unpack_from('<I', data, 16)[0]
    offset, names = 32, []
    for _ in range(count):
        command, size = struct.unpack_from('<II', data, offset)
        if command == 2:  # LC_SYMTAB
            symbols, number, strings, _ = struct.unpack_from('<IIII', data, offset + 8)
            for index in range(number):
                string_index, kind = struct.unpack_from('<IB', data, symbols + index * 16)
                # Defined external symbols only; undefined SDK imports are not ABI exports.
                if not string_index or not kind & 1 or kind & 0x10 or kind & 0x0E not in (2, 14):
                    continue
                start = strings + string_index
                name = data[start:data.index(b'\0', start)].decode('ascii')
                if re.fullmatch(r'_(?:sk|gr)[a-z0-9_]+', name):
                    names.append(name)
        offset += size
    if len(names) < 100 or len(names) != len(set(names)):
        raise ValueError('The native C ABI symbol table is empty, incomplete or duplicated')
    return sorted(names)


def version(path):
    library = ctypes.CDLL(str(path.resolve()))
    library.sk_version_get_string.restype = ctypes.c_char_p
    return {'milestone': library.sk_version_get_milestone(),
            'increment': library.sk_version_get_increment(),
            'string': library.sk_version_get_string().decode('ascii')}


def deployment_target(path, cpu):
    data = macho_slice(path, cpu)
    count = struct.unpack_from('<I', data, 16)[0]
    offset, values = 32, []
    for _ in range(count):
        command, size = struct.unpack_from('<II', data, offset)
        if command == 0x32:  # LC_BUILD_VERSION
            platform_id, minimum = struct.unpack_from('<II', data, offset + 8)
            if platform_id != 1:
                raise ValueError('The rebuilt library does not target macOS')
            values.append(minimum)
        elif command == 0x24:  # LC_VERSION_MIN_MACOSX
            values.append(struct.unpack_from('<I', data, offset + 8)[0])
        offset += size
    if len(values) != 1:
        raise ValueError('The rebuilt macOS deployment target is absent or ambiguous')
    value = values[0]
    return f'{value >> 16}.{(value >> 8) & 255}.{value & 255}'


def build_gn(lock, work, cache, logs, ninja):
    source = fetch_source(lock['tools']['gn_source'], cache)
    directory = work / 'gn-source'
    extract_source(cache / source['archive_path'], directory, False)
    # A source archive has no Git position history. Record the exact revision,
    # with an explicitly non-release numeric position, rather than inventing one.
    out = directory / 'out'
    out.mkdir()
    revision = lock['tools']['gn_source']['revision']
    (out / 'last_commit_position.h').write_text(
        '#define LAST_COMMIT_POSITION_NUM 0\n'
        f'#define LAST_COMMIT_POSITION "0 ({revision[:7]}; fixed-source-build)"\n', encoding='utf-8')
    run([sys.executable, 'build/gen.py', '--no-last-commit-position'], directory, logs / 'gn-bootstrap-gen.log')
    run([ninja, '-C', out, '-j', '3', 'gn'], directory, logs / 'gn-bootstrap-build.log')
    return out / 'gn', source


def upstream_png(wrapper, binary, work, logs):
    mini = work / 'upstream-native-mini'
    mini.mkdir()
    source = wrapper / 'utils/NativeLibraryMiniTest'
    shutil.copyfile(source / 'source/Program.cs', mini / 'Program.cs')
    shutil.copyfile(source / 'docker/NativeLibraryMiniTest.csproj', mini / 'NativeLibraryMiniTest.csproj')
    run(['dotnet', 'build', 'NativeLibraryMiniTest.csproj', '-c', 'Release',
         '-p:TargetFramework=net10.0', '-p:TargetFrameworks=net10.0', '-o', 'bin', '--nologo'],
        mini, logs / 'upstream-mini-build.log')
    loaded_binary = mini / 'bin/libSkiaSharp.dylib'
    shutil.copyfile(binary, loaded_binary)
    run(['/usr/bin/env', 'DYLD_PRINT_LIBRARIES=1', 'dotnet', 'bin/NativeLibraryMiniTest.dll'],
        mini, logs / 'upstream-mini-run.log')
    loaded_lines = [line for line in (logs / 'upstream-mini-run.log').read_text(encoding='utf-8').splitlines()
                    if 'libSkiaSharp.dylib' in line]
    if (not loaded_lines or any(str(loaded_binary.resolve()) not in line for line in loaded_lines)
            or file_digest(loaded_binary) != file_digest(binary)):
        raise ValueError('The upstream utility did not prove it loaded only the rebuilt dylib')
    png = mini / 'output.png'
    if png.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('The unchanged upstream native utility did not create a PNG')
    shutil.copyfile(png, logs / 'upstream-mini-output.png')
    return {'program_sha256': file_digest(mini / 'Program.cs'),
            'original_program_path': 'utils/NativeLibraryMiniTest/source/Program.cs',
            'output_png_sha256': file_digest(png), 'loaded_library_path': str(loaded_binary.resolve()),
            'loaded_library_sha256': file_digest(loaded_binary)}


def source_bundle(destination, wrapper, downloaded, gn_source, cache, metadata, notices, logs, mac_patch):
    with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        add_zip_tree(archive, wrapper, 'sources/SkiaSharp', excluded=('.git', 'out'))
        for relative in (*PATCHES, mac_patch, 'third-party/skia-no-dng-build.json',
                         'third-party/skia-no-dng-macos-build.json', 'third-party/components.json',
                         'scripts/build_skia_macos_without_dng.py', 'scripts/build_skia_without_dng.py',
                         'scripts/collect_third_party_sources.py',
                         '.github/workflows/build-skia-macos-without-dng.yml',
                         'LICENSE.md', 'NOTICE.md', *notices):
            archive.write(ROOT / relative, 'rebuild/' + relative)
        add_zip_tree(archive, logs, 'build-records')
        for source in [*downloaded, gn_source]:
            path = cache / source['archive_path']
            archive.write(path, 'upstream-archives/' + path.name)
            archive.write(cache / (path.name + '.download.json'), 'upstream-archives/' + path.name + '.download.json')
        archive.writestr('BUILD_PROVENANCE.json', json.dumps(metadata, ensure_ascii=False, indent=2) + '\n')
    return {'name': destination.name, 'sha256': file_digest(destination), 'bytes': destination.stat().st_size}


def build(args):
    lock = json.loads(LOCK_PATH.read_text(encoding='utf-8'))
    base = ROOT / lock['source_lock']
    if file_digest(base) != lock['source_lock_sha256']:
        raise ValueError('The shared fixed upstream source lock changed')
    source_lock = json.loads(base.read_text(encoding='utf-8'))
    output, work, cache = args.output_dir.resolve(), args.work_dir.resolve(), args.cache_dir.resolve()
    logs = output / 'logs'
    for directory in (output, work, cache, logs):
        directory.mkdir(parents=True, exist_ok=True)
    wrapper, skia, patches, downloaded = prepare_sources(source_lock, work, cache, logs)
    mac_patch = lock['macos_patch']
    patch_path = ROOT / mac_patch['path']
    if file_digest(patch_path) != mac_patch['sha256']:
        raise ValueError('The fixed macOS GN patch changed')
    run(['git', 'apply', '--check', patch_path], skia, logs / (patch_path.stem + '-check.log'))
    run(['git', 'apply', patch_path], skia, logs / (patch_path.stem + '-apply.log'))
    patches.append(mac_patch)
    write_json(output / 'source-preparation.json', {'sources': downloaded, 'patches': patches})
    if args.prepare_only:
        print('Exact shared source trees prepared; macOS execution was not attempted.')
        return
    cpu, arch = CPU[args.arch]
    if sys.platform != 'darwin' or platform.machine() != arch:
        raise ValueError('A real macOS runner of the selected native architecture is required')
    tools = work / 'tools'
    tools.mkdir()
    ninja_archive = download_tool(lock['tools']['ninja'], 'ninja-mac.zip', cache)
    with zipfile.ZipFile(ninja_archive) as package:
        package.extract('ninja', tools)
    ninja = tools / 'ninja'
    ninja.chmod(0o755)
    gn, gn_source = build_gn(lock, work, cache, logs, ninja)
    compiler = pathlib.Path(subprocess.check_output(['xcrun', '--find', 'clang'], text=True).strip())
    compiler_version = run([compiler, '--version'], work, logs / 'clang-version.txt', capture=True)
    run(['xcodebuild', '-version'], work, logs / 'xcode-version.txt')
    run(['xcrun', '--show-sdk-version'], work, logs / 'sdk-version.txt')
    run([gn, '--version'], work, logs / 'gn-version.txt')
    run([ninja, '--version'], work, logs / 'ninja-version.txt')
    arguments = {
        'target_os': 'mac', 'target_cpu': args.arch, 'min_macos_version': lock['minimum_macos_version'],
        'cc': str(compiler), 'cxx': str(compiler.with_name('clang++')),
        'skia_use_dng_sdk': False, 'skia_use_piex': False, 'skia_use_icu': False,
        'skia_use_sfntly': False, 'skia_use_metal': True,
        'skia_use_system_expat': False, 'skia_use_system_libjpeg_turbo': False,
        'skia_use_system_libpng': False, 'skia_use_system_libwebp': False, 'skia_use_system_zlib': False,
        'skia_enable_skottie': True, 'skia_enable_tools': False, 'is_official_build': True,
        'extra_cflags': ['-DSKIA_C_DLL', '-DHAVE_ARC4RANDOM_BUF', '-stdlib=libc++'],
        'extra_ldflags': ['-stdlib=libc++'],
    }
    out = skia / ('out/macos/' + args.arch)
    out.mkdir(parents=True)
    (out / 'args.gn').write_text(gn_text(arguments), encoding='utf-8')
    shutil.copyfile(out / 'args.gn', logs / 'args.gn')
    before = dependency_hashes(skia, source_lock)
    run([gn, 'gen', out, '--script-executable=' + sys.executable], skia, logs / 'gn-gen.log')
    after = dependency_hashes(skia, source_lock)
    transformations = [{'path': path, 'before_sha256': value, 'after_sha256': after[path]}
                       for path, value in before.items() if after.get(path) != value]
    write_json(logs / 'gn-source-transformations.json', transformations)
    run([gn, 'args', out, '--list', '--short'], skia, logs / 'gn-effective-args.txt')
    json.loads(run([gn, 'desc', out, '*', '--format=json'], skia, logs / 'gn-graph.json', capture=True))
    dependencies = run([gn, 'desc', out, '//:SkiaSharp', 'deps', '--all'], skia, logs / 'gn-transitive-deps.txt', capture=True)
    raw = json.loads(run([gn, 'desc', out, '//:raw', '--format=json'], skia, logs / 'gn-raw.json', capture=True))
    if any(value.get('sources') or value.get('deps') for value in raw.values()) or FORBIDDEN.search(dependencies):
        raise ValueError('DNG/PIEX remain in the generated native graph')
    commands = run([ninja, '-C', out, '-t', 'commands', 'SkiaSharp'], skia, logs / 'ninja-commands.txt', capture=True)
    if FORBIDDEN.search(commands):
        raise ValueError('DNG/PIEX remain in generated native build commands')
    run([ninja, '-C', out, '-j', str(args.jobs), 'SkiaSharp'], skia, logs / 'ninja-build.log')
    inputs = run([ninja, '-C', out, '-t', 'inputs', 'SkiaSharp'], skia, logs / 'ninja-inputs.txt', capture=True)
    if FORBIDDEN.search(inputs):
        raise ValueError('DNG/PIEX remain in actual compiled inputs')
    source_inputs = []
    for name in inputs.splitlines():
        path = (out / name.strip()).resolve()
        if path.is_relative_to(skia) and not path.is_relative_to(out):
            if not path.is_file():
                raise ValueError(f'A compiled input is unavailable: {path}')
            source_inputs.append({'path': path.relative_to(skia).as_posix(), 'sha256': file_digest(path)})
    if not source_inputs:
        raise ValueError('No actual compiled source inputs were recorded')
    write_json(logs / 'native-source-inputs.json', source_inputs)
    binary = out / 'libSkiaSharp.dylib'
    if binary.read_bytes()[:4] != b'\xcf\xfa\xed\xfe':
        raise ValueError('The produced library must be a thin native Mach-O dylib')
    # Final producer bytes are signed once, before hashing or verifying. The app
    # bundle must retain these exact bytes rather than signing this dylib again.
    run(['codesign', '--force', '--sign', '-', '--timestamp=none', binary], work, logs / 'codesign.log')
    run(['codesign', '--verify', '--strict', binary], work, logs / 'codesign-verify.log')
    run(['codesign', '--display', '--verbose=4', binary], work, logs / 'codesign-details.txt')
    run(['otool', '-l', binary], work, logs / 'macho-load-commands.txt')
    dylib_id = run(['otool', '-D', binary], work, logs / 'macho-install-name.txt', capture=True).splitlines()[1:]
    if dylib_id != ['@rpath/libSkiaSharp.dylib']:
        raise ValueError('The rebuilt dylib has an unexpected install name')
    dependencies_text = run(['otool', '-L', binary], work, logs / 'macho-dependencies.txt', capture=True)
    dylib_dependencies = [line.strip().split(' (', 1)[0] for line in dependencies_text.splitlines()[1:]]
    if not dylib_dependencies or any(path != dylib_id[0] and not path.startswith(('/usr/lib/', '/System/Library/'))
                                     for path in dylib_dependencies):
        raise ValueError('The rebuilt library depends on an unbundled non-system dylib')
    minimum_macos = deployment_target(binary, cpu)
    if minimum_macos != lock['minimum_macos_version'] + '.0':
        raise ValueError('The Mach-O deployment target differs from the fixed minimum macOS version')
    official_package = download_tool(lock['official_package'], 'skiasharp.nativeassets.macos.2.88.9.nupkg', cache)
    official = work / 'official-libSkiaSharp.dylib'
    with zipfile.ZipFile(official_package) as package:
        official.write_bytes(package.read('runtimes/osx/native/libSkiaSharp.dylib'))
    old_exports, new_exports = c_exports(official, cpu), c_exports(binary, cpu)
    write_json(logs / 'official-c-exports.json', old_exports)
    write_json(logs / 'rebuilt-c-exports.json', new_exports)
    if old_exports != new_exports:
        raise ValueError('The public SkiaSharp/GR C ABI differs from the official package')
    versions = {'official': version(official), 'rebuilt': version(binary)}
    if versions['official'] != versions['rebuilt']:
        raise ValueError('The native version does not match official SkiaSharp 2.88.9')
    write_json(logs / 'native-versions.json', versions)
    if any(marker in binary.read_bytes() for marker in (b'dng_', b'SkDng', b'SkRawCodec', b'piex::')):
        raise ValueError('DNG/PIEX binary markers remain in the produced dylib')
    png = upstream_png(wrapper, binary, work, logs)
    commit = args.build_commit or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r'[0-9a-f]{40}', commit):
        raise ValueError('The exact producer Git commit must be recorded')
    artifact_id = lock['artifact_id_prefix'] + args.arch
    metadata = {
        'schema_version': 1, 'artifact_id': artifact_id, 'runtime_identifier': 'osx-' + args.arch,
        'status': 'verified', 'build_commit': commit,
        'binary': {'path': binary.name, 'sha256': file_digest(binary), 'bytes': binary.stat().st_size,
                   'mach_o_arch': arch, 'mach_o_cputype': hex(cpu), 'minimum_macos_version': minimum_macos,
                   'install_name': dylib_id[0], 'dylib_dependencies': dylib_dependencies,
                   'code_signature': 'ad-hoc; verified before hashing'},
        'sources': {key: {'repository': item['repository'], 'commit': item['commit'], 'archive_sha256': item['sha256']}
                    for key, item in source_lock['sources'].items()},
        'dependencies': downloaded[2:], 'patches': patches, 'gn_args': arguments,
        'tools': lock['tools'], 'gn_source_archive': gn_source,
        'compiler_binary_sha256': file_digest(compiler), 'compiler_version': compiler_version,
        'native_versions': versions, 'upstream_native_utility': png,
        'gn_source_transformations': transformations, 'native_source_input_count': len(source_inputs),
        'build_records': [{'path': path.relative_to(logs).as_posix(), 'sha256': file_digest(path)}
                          for path in sorted(logs.rglob('*')) if path.is_file()],
        'verification': {'no_dng_sources_in_graph': True, 'no_dng_commands': True,
                         'no_dng_binary_symbols': True, 'exports_match_official': True,
                         'smoke_tests': {'native_abi_version_and_load': True, 'upstream_png_encode': True}},
        'unverified_features': ['Dedicated JPEG/WebP/GIF decoding was not exercised by the upstream PNG utility.'],
        'distribution_review': 'Technical verification alone does not grant distribution or license approval.',
    }
    notices = native_notice_paths(source_lock)
    stem = 'SkiaSharp-2.88.9-no-dng-osx-' + args.arch
    metadata['source_archive'] = source_bundle(output / (stem + '-sources.zip'), wrapper, downloaded,
                                                gn_source, cache, metadata, notices, logs, mac_patch['path'])
    write_json(output / 'native-build.json', metadata)
    shutil.copyfile(binary, output / binary.name)
    notice = ('SkiaSharp 2.88.9 for macOS, rebuilt without RAW/DNG and PIEX.\n'
              'Original copyright, license grants and disclaimers are retained in LICENSES/.\n'
              'This software is based in part on the work of the Independent JPEG Group.\n'
              'The libgifcodec MPL/GPL/LGPL alternative is elected under GPL-2.0-or-later, version 3; '
              'original alternatives and BSD portions remain in the supplied notices.\n'
              f'Corresponding native sources: {metadata["source_archive"]["name"]}\n'
              f'Source archive SHA-256: {metadata["source_archive"]["sha256"]}\n')
    (output / 'NOTICE-NATIVE.txt').write_text(notice, encoding='utf-8')
    with zipfile.ZipFile(output / (stem + '.zip'), 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for filename in (binary.name, 'native-build.json', 'NOTICE-NATIVE.txt'):
            archive.write(output / filename, filename)
        for relative in notices:
            archive.write(ROOT / relative, relative)
    print('Native macOS build and source pair verified; no public release was created.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--arch', choices=CPU, required=True)
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    parser.add_argument('--work-dir', type=pathlib.Path, required=True)
    parser.add_argument('--cache-dir', type=pathlib.Path, required=True)
    parser.add_argument('--jobs', type=int, default=3, choices=range(1, 9))
    parser.add_argument('--build-commit')
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args()
    try:
        build(args)
    except Exception as error:
        parser.exit(1, f'Native macOS producer failed: {error}\n')


if __name__ == '__main__':
    main()
