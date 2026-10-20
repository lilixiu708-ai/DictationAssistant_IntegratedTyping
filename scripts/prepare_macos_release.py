"""Bundle, verify and publish the two native macOS R8 integrated builds.

SPDX-License-Identifier: AGPL-3.0-or-later
Modified: 2026-10-20
"""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import re
import shutil
import shlex
import stat
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile

from collect_third_party_sources import collect

ROOT = pathlib.Path(__file__).resolve().parents[1]
TAG = 'v4.0.0-typing.8'
RIDS = ('osx-x64', 'osx-arm64')
PLUGINS = {'SynchronousTyping': ('synchronous-typing', '1.4.1'), 'DocxVocabulary': ('docx-vocabulary', '1.2.0')}
PROJECT_NOTICES = ('LICENSE.md', 'NOTICE.md', 'MODIFICATIONS.md', 'THIRD_PARTY_NOTICES.md')
MANIFESTS = ('THIRD_PARTY_COMPONENTS.json', 'THIRD_PARTY_SOURCES.json')
KNOWN_ISSUE = 'avalonia-mspl-agpl-compatibility'
LOCK_PROJECTS = ('src/DictationAssistant.App', 'src/DictationAssistant.Plugin.Abstractions',
                 'plugins/DictationAssistant.Importers.Docx', 'plugins/DictationAssistant.Practices.Typing',
                 'tests/TypingPlugin.SmokeTests')


def command_log(path, arguments, returncode, output):
    if path is not None:
        with path.open('a', encoding='utf-8') as stream:
            stream.write(f'$ {shlex.join(str(item) for item in arguments)}\nexit_code={returncode}\n{output}\n')


def run(*arguments, diagnostic_log=None, **kwargs):
    command = [str(item) for item in arguments]
    try:
        result = subprocess.check_output(command, text=True, **kwargs)
    except subprocess.CalledProcessError as error:
        output = error.output or ''
        if error.stderr:
            output += error.stderr
        command_log(diagnostic_log, command, error.returncode, output)
        if output:
            print(output, file=sys.stderr, end='' if output.endswith('\n') else '\n')
        raise
    command_log(diagnostic_log, command, 0, result)
    return result


def signature_preflight(runtime, diagnostic_log):
    """Record existing signatures without modifying native dependency bytes."""
    macho_magic = {b'\xfe\xed\xfa\xce', b'\xce\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xcf\xfa\xed\xfe',
                   b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca', b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca'}
    for path in sorted(runtime.rglob('*')):
        if not path.is_file():
            continue
        with path.open('rb') as stream:
            if stream.read(4) not in macho_magic:
                continue
        command = ['codesign', '--verify', '--strict', '--verbose=4', str(path)]
        result = subprocess.run(command, capture_output=True, text=True)
        command_log(diagnostic_log, command, result.returncode, result.stdout + result.stderr)
        if result.returncode:
            print(f'Signature preflight: {path.relative_to(runtime)}\n{result.stdout}{result.stderr}', file=sys.stderr)


def git(*arguments):
    return run('git', *arguments, cwd=ROOT).strip()


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, document):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def version():
    value = ET.parse(ROOT / 'src/DictationAssistant.App/DictationAssistant.App.csproj').findtext('.//Version')
    if 'v' + value != TAG:
        raise ValueError('This producer is restricted to the exact R8 application version')
    return value


def base_name(rid):
    return f'DictationAssistant-{version()}-macos-' + rid.removeprefix('osx-')


def nuget_locks(rid):
    records = []
    for project in LOCK_PROJECTS:
        path = ROOT / project / f'packages.{rid}.lock.json'
        document = json.loads(path.read_text(encoding='utf-8'))
        if document.get('version') != 1 or set(document.get('dependencies', {})) != {'net10.0', f'net10.0/{rid}'}:
            raise ValueError('A committed NuGet lock does not match its exact macOS RID: ' + str(path))
        records.append({'path': path.relative_to(ROOT).as_posix(), 'sha256': digest(path)})
    return {'locked_mode': True, 'runtime_framework_version': '10.0.0', 'lock_files': records}


def check_review(sources, review_only=False):
    review = sources.get('release_review', {})
    blockers = review.get('blocking_issues')
    if not isinstance(blockers, list):
        raise ValueError('Missing actual native/license source review')
    if not review_only and (review.get('status') not in ('ready', 'blocked')
            or any(not isinstance(item, dict) or item.get('id') != KNOWN_ISSUE
                   or not str(item.get('reason', '')).strip() for item in blockers)
            or any(item.get('review_only') for item in sources.get('native_overrides', []))):
        raise ValueError('A new or unreviewed native/source issue cannot be waived for publication')
    return review


def file_inventory(directory):
    values = {}
    for path in sorted(directory.rglob('*')):
        name = path.relative_to(directory).as_posix()
        if path.is_symlink():
            if not path.resolve().is_relative_to(directory.resolve()):
                raise ValueError('The bundle contains an escaping symlink')
            values[name] = {'symlink_target': os.readlink(path)}
        elif path.is_file():
            values[name] = {'sha256': digest(path), 'bytes': path.stat().st_size}
    return values


def verify_files(directory, inventory):
    if file_inventory(directory) != inventory:
        raise ValueError('The actual app files differ from the final bundle inventory')


def validate(args):
    """Exercise the actual release on a matching Mac; all probes stay temporary."""
    if sys.platform != 'darwin':
        raise ValueError('Runtime checks require a real macOS runner')
    expected_machine = 'x86_64' if args.rid == 'osx-x64' else 'arm64'
    if os.uname().machine != expected_machine:
        raise ValueError('Runtime checks must use the matching native runner')
    output = args.output_dir.resolve()
    app = output / 'payload/DictationAssistant.app'
    runtime = app / 'Contents/MacOS'
    executable = runtime / 'DictationAssistant.App'
    report = json.loads(run(executable, '--list-plugins', cwd=runtime, timeout=60))
    expected = {value[0]: value[1] for value in PLUGINS.values()}
    plugins = {item['Manifest']['Id']: item for item in report.get('Plugins', []) if item.get('Manifest')}
    if (report.get('ApiVersion') != 1 or report.get('HostFeatureLevel') != 5
            or report.get('LoadedCount') != 2 or set(plugins) != set(expected)):
        raise ValueError('The actual app failed to discover its two bundled plugins')
    for identity, plugin_version in expected.items():
        item = plugins[identity]
        manifest = item['Manifest']
        capabilities = {'practice', 'configuration'} if identity == 'synchronous-typing' else {'document-importer'}
        if (item.get('IsLoaded') is not True or item.get('Error') is not None
                or manifest.get('Version') != plugin_version or manifest.get('ApiVersion') != 1
                or manifest.get('MinimumHostFeatureLevel') != 5
                or not capabilities <= set(item.get('Capabilities', []))):
            raise ValueError('The actual app rejected a bundled plugin: ' + identity)
    write_json(output / 'PLUGIN_DISCOVERY.json', report)
    smoke = ROOT / 'tests/TypingPlugin.SmokeTests/TypingPlugin.SmokeTests.csproj'
    smoke_restore = run('dotnet', 'restore', smoke, '-r', args.rid, '--locked-mode',
                        f'-p:RuntimeIdentifier={args.rid}', '-p:SelfContained=false', '-p:RestorePackagesWithLockFile=true',
                        f'-p:NuGetLockFilePath=packages.{args.rid}.lock.json', cwd=ROOT)
    smoke_build = run('dotnet', 'build', smoke, '-c', 'Release', '-r', args.rid,
                      '--no-restore', '-p:SelfContained=false', cwd=ROOT)
    smoke_result = run('dotnet', smoke.parent / f'bin/Release/net10.0/{args.rid}/TypingPlugin.SmokeTests.dll',
                       cwd=ROOT, timeout=60)
    (output / 'typing-checks.log').write_text(smoke_restore + smoke_build + smoke_result, encoding='utf-8')
    with tempfile.TemporaryDirectory(prefix='dictation-macos-probe-') as temporary:
        temporary = pathlib.Path(temporary)
        probe_project = temporary / 'AudioProbe.csproj'
        probe_project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
</PropertyGroup><ItemGroup><Reference Include="DictationAssistant.App"><HintPath>'''
            + str(runtime / 'DictationAssistant.App.dll') + '''</HintPath></Reference>
</ItemGroup></Project>''', encoding='utf-8')
        (temporary / 'Program.cs').write_text('''using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using DictationAssistant.App.Voice;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var voices = await new MacSayVoiceFactoryProvider().GetFactoriesAsync(timeout.Token);
if (voices.Count == 0) throw new Exception("The system exposed no say voices.");
var voice = voices.FirstOrDefault(v => v.Info.LocaleOrLanguage?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true) ?? voices[0];
using var pcm = await voice.Create().SynthesizePcmAsync("Dictation assistant", new VoiceSynthesisOptions(), timeout.Token);
if (!pcm.Data.CanRead) throw new Exception("System speech returned unreadable PCM.");
using var buffer = new MemoryStream();
var transfer = new byte[16384];
int count;
while ((count = await pcm.Data.ReadAsync(transfer, timeout.Token)) != 0) {
    if (buffer.Length + count > 4 * 1024 * 1024) throw new Exception("Short speech exceeded the probe PCM limit.");
    await buffer.WriteAsync(transfer.AsMemory(0, count), timeout.Token);
}
if (buffer.Length == 0) throw new Exception("System speech returned empty PCM.");
buffer.Position = 0;
using var playback = new PcmAudio { Data = buffer, Format = pcm.Format };
using var player = new SdlPcmPlayer();
await player.PlayAsync(playback, timeout.Token);
Console.WriteLine($"PASS say synthesis and SDL dummy playback: {voice.Info.DisplayName}; {pcm.Format}");
''', encoding='utf-8')
        probe_build = run('dotnet', 'build', probe_project, '-c', 'Release', '-o', temporary / 'compiled', cwd=temporary)
        # The signed/distributed app is never modified by this temporary probe.
        # A disposable copy supplies the published native and managed deps.
        clone = temporary / 'runtime'
        shutil.copytree(runtime, clone, ignore=shutil.ignore_patterns('Plugins', 'LICENSES', 'NATIVE_BUILDS'))
        shutil.copyfile(temporary / 'compiled/AudioProbe.dll', clone / 'AudioProbe.dll')
        probe_env = {**os.environ, 'SDL_AUDIODRIVER': 'dummy'}
        audio = run('dotnet', 'exec', '--depsfile', clone / 'DictationAssistant.App.deps.json',
                    '--runtimeconfig', clone / 'DictationAssistant.App.runtimeconfig.json',
                    clone / 'AudioProbe.dll', env=probe_env, cwd=clone, timeout=60)
        (output / 'audio-checks.log').write_text(probe_build + audio, encoding='utf-8')
        swift = temporary / 'WindowProbe.swift'
        swift.write_text('''import AppKit
import CoreGraphics
let pid = pid_t(CommandLine.arguments[2])!
if CommandLine.arguments[1] == "window" {
    let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
    let found = windows.contains { item in
        guard (item[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == pid,
              (item[kCGWindowLayer as String] as? NSNumber)?.intValue == 0,
              let bounds = item[kCGWindowBounds as String] as? [String: Any],
              let width = bounds["Width"] as? NSNumber, let height = bounds["Height"] as? NSNumber else { return false }
        return width.doubleValue > 20 && height.doubleValue > 20
    }
    print(found ? "true" : "false")
} else {
    guard let app = NSRunningApplication(processIdentifier: pid) else { exit(2) }
    print(app.terminate() ? "true" : "false")
}
''', encoding='utf-8')
        window_probe = temporary / 'WindowProbe'
        run('swiftc', swift, '-o', window_probe)
        with (output / 'main-window.log').open('w', encoding='utf-8') as log:
            process = subprocess.Popen([executable], cwd=runtime, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 40
                opened = False
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise ValueError(f'The production app exited before opening a window: {process.returncode}')
                    if run(window_probe, 'window', process.pid).strip() == 'true':
                        opened = True
                        break
                    time.sleep(1)
                if not opened:
                    raise ValueError('The production app did not expose an on-screen main window')
                time.sleep(2)
                if run(window_probe, 'terminate', process.pid).strip() != 'true':
                    raise ValueError('macOS rejected the application normal-quit request')
                if process.wait(timeout=15) != 0:
                    raise ValueError('The production app failed to close normally')
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
    run('codesign', '--verify', '--deep', '--strict', app, stderr=subprocess.STDOUT)
    verify_files(app, json.loads((output / 'payload/BUNDLE_FILES.json').read_text()))
    write_json(output / 'BUILD_VALIDATION.json', {
        'source_commit': git('rev-parse', 'HEAD'), 'runtime_identifier': args.rid,
        'plugin_discovery': True, 'existing_typing_checks': True,
        'main_window_opened': True, 'main_window_closed_normally': True,
        'main_window_close_method': 'NSRunningApplication.terminate; process exit code 0',
        'mac_say_synthesis': True, 'sdl_dummy_playback': True,
        'physical_speaker_output_tested': False, 'interactive_ime_online_translation_tested': False})
    print('Validated both actual plugins, existing logic, say PCM, SDL dummy output and the production main window.')


def bundle(args):
    if sys.platform != 'darwin':
        raise ValueError('Signing and bundling must run on macOS')
    output = args.output_dir.resolve()
    signature_log = output / 'codesign-diagnostics.log'
    payload = output / 'payload'
    app = payload / 'DictationAssistant.app'
    runtime = app / 'Contents/MacOS'
    head = git('rev-parse', 'HEAD')
    components = json.loads((runtime / MANIFESTS[0]).read_text(encoding='utf-8'))
    sources = json.loads((runtime / MANIFESTS[1]).read_text(encoding='utf-8'))
    review = check_review(sources, args.review_only)
    for folder, (plugin_id, plugin_version) in PLUGINS.items():
        manifest = json.loads((runtime / 'Plugins' / folder / 'plugin.json').read_text())
        assembly = runtime / 'Plugins' / folder / manifest['entryAssembly']
        if (manifest.get('id') != plugin_id or manifest.get('version') != plugin_version
                or manifest.get('apiVersion') != 1 or manifest.get('minimumHostFeatureLevel') != 5
                or f'{plugin_version}+{head}'.encode() not in assembly.read_bytes()):
            raise ValueError('The bundle must contain the exact current plugin builds and feature level 5')
    if f'{version()}+{head}'.encode() not in (runtime / 'DictationAssistant.App.dll').read_bytes():
        raise ValueError('The application build does not match this source commit')
    if not (runtime / 'libSkiaSharp.dylib').is_file() or not (runtime / 'libSDL2.dylib').is_file():
        raise ValueError('The self-contained app lacks required macOS native libraries')
    # A manifest that hashes the apphost cannot itself be sealed into that
    # apphost's resource signature. Keep final inventories beside the .app.
    for name in MANIFESTS:
        shutil.move(runtime / name, payload / name)
    resources = app / 'Contents/Resources'
    resources.mkdir(parents=True, exist_ok=True)
    plist = plistlib.loads((ROOT / 'deploy/macos/Info.plist').read_bytes())
    plist['CFBundleShortVersionString'] = version().split('-')[0]
    (app / 'Contents/Info.plist').write_bytes(plistlib.dumps(plist, sort_keys=False))
    iconset = output / 'DictationAssistant.iconset'
    iconset.mkdir()
    for points in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            pixels = points * scale
            suffix = '@2x' if scale == 2 else ''
            run('sips', '-z', pixels, pixels, ROOT / 'deploy/linux/dictation-assistant.png',
                '--out', iconset / f'icon_{points}x{points}{suffix}.png')
    run('iconutil', '-c', 'icns', iconset, '-o', resources / 'DictationAssistant.icns')
    shutil.rmtree(iconset)
    executable = runtime / 'DictationAssistant.App'
    executable.chmod(executable.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    architecture = 'x86_64' if args.rid == 'osx-x64' else 'arm64'
    for name in ('DictationAssistant.App', 'libcoreclr.dylib', 'libSDL2.dylib', 'libSkiaSharp.dylib'):
        archs = run('lipo', '-archs', runtime / name).split()
        if architecture not in archs:
            raise ValueError(f'The {args.rid} bundle contains an incompatible native file: {name}')
    signing_adjustments = []
    if args.rid == 'osx-x64':
        # This exact NuGet SDL is unsigned. The collector has already checked
        # its original package hash and source notices; sign only this library.
        sdl = runtime / 'libSDL2.dylib'
        original_sha = digest(sdl)
        if not any(item['path'] == sdl.name and item['sha256'] == original_sha for item in components['files']):
            raise ValueError('The unsigned Intel SDL was not verified against its original package')
        run('codesign', '--force', '--sign', '-', '--verbose=4', sdl,
            diagnostic_log=signature_log, stderr=subprocess.STDOUT)
        run('codesign', '--verify', '--strict', '--verbose=4', sdl,
            diagnostic_log=signature_log, stderr=subprocess.STDOUT)
        signing_adjustments.append({'path': sdl.name, 'source_original_sha256': original_sha,
            'distributed_signed_sha256': digest(sdl), 'signing': 'ad-hoc',
            'reason': 'The original osx-x64 NuGet SDL2 dylib has no code signature.',
            'command': 'codesign --force --sign - libSDL2.dylib'})
    signature_preflight(runtime, signature_log)
    run('codesign', '--force', '--sign', '-', '--options', 'runtime', '--verbose=4', '--entitlements',
        ROOT / 'deploy/macos/entitlements.plist', app, diagnostic_log=signature_log, stderr=subprocess.STDOUT)
    run('codesign', '--verify', '--deep', '--strict', '--verbose=4', app,
        diagnostic_log=signature_log, stderr=subprocess.STDOUT)
    apphost_found = False
    for item in components['files']:
        actual = digest(runtime / item['path'])
        if item['path'] == 'DictationAssistant.App':
            item['source_original_sha256'] = item['sha256']
            item['sha256'] = actual
            apphost_found = True
        elif any(item['path'] == adjustment['path'] for adjustment in signing_adjustments):
            adjustment = next(value for value in signing_adjustments if value['path'] == item['path'])
            if item['sha256'] != adjustment['source_original_sha256'] or actual != adjustment['distributed_signed_sha256']:
                raise ValueError('The SDL signature adjustment does not match the original and final bytes')
            item.update(adjustment)
            item['sha256'] = actual
        elif actual != item['sha256']:
            raise ValueError('App signing unexpectedly changed a native or managed dependency: ' + item['path'])
    if not apphost_found:
        raise ValueError('The final apphost is absent from the runtime/notice inventory')
    write_json(payload / MANIFESTS[0], components)
    for name in PROJECT_NOTICES:
        shutil.copyfile(ROOT / name, payload / name)
    (payload / 'README-macOS.txt').write_text(
        'DictationAssistant macOS 整合版\n\n'
        '需要 macOS 15 或更新版本。解压后，将 DictationAssistant.app 拖到“应用程序”并打开。\n'
        '本包自带 .NET、背诵练习 1.4.1 和 Word / Excel 导入 1.2.0，无需另外安装插件。\n'
        'Intel 电脑选择 x64 包；M 系列电脑选择 arm64 包。\n'
        '可静音计时背诵，或选择系统语音听写。练习中点击“结算成绩”可提前结束。\n'
        '翻译引擎在插件配置中填写；macOS 上的凭据仅本次使用，不保存到下次启动。\n'
        '本版使用临时签名，尚未经过 Apple 公证。若系统阻止打开，核对来源后可在\n'
        '“系统设置 → 隐私与安全性”中确认打开。\n'
        '原始许可证随应用保留在 Contents/MacOS/LICENSES；旁边的清单用于核验来源。\n'
        'Avalonia/MS-PL 与 AGPL 的组合授权事项仍未解决，具体声明随包保留。\n', encoding='utf-8')
    info = {'schema_version': 1, 'application_version': version(), 'source_commit': head,
            'runtime_identifier': args.rid, 'minimum_macos': '15.0', 'self_contained': True,
            'plugin_api_version': 1, 'host_feature_level': 5,
            'nuget': nuget_locks(args.rid),
            'bundled_plugins': {value[0]: value[1] for value in PLUGINS.values()},
            'signing': {'type': 'ad-hoc', 'apple_notarized': False,
                        'deep_strict_verification_passed': True,
                        'native_dependency_bytes_unchanged': not signing_adjustments,
                        'native_signing_adjustments': signing_adjustments,
                        'other_native_dependency_bytes_unchanged': True},
            'license_review': review, 'review_only': args.review_only,
            'binary_inventory_base': 'DictationAssistant.app/Contents/MacOS'}
    write_json(payload / 'BUNDLE_INFO.json', info)
    write_json(payload / 'BUNDLE_FILES.json', file_inventory(app))
    destination = output / (base_name(args.rid) + '.zip')
    run('ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', payload, destination)
    # ditto preserves executable modes and signed bundle resources; source ZIPs
    # below use git archive so filenames and bytes match the immutable Git tree.
    shutil.copyfile(payload / 'BUNDLE_INFO.json', output / 'BUNDLE_INFO.json')
    for name in MANIFESTS:
        shutil.copyfile(payload / name, output / name)
    notice_destination = output / 'runtime-notices'
    for path in runtime.glob('LICENSES/runtime/**/*'):
        if path.is_file():
            target = notice_destination / path.relative_to(runtime)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
    print(destination)


def verify_part(part, rid, head):
    info = json.loads((part / 'BUNDLE_INFO.json').read_text())
    if (info.get('source_commit') != head or info.get('runtime_identifier') != rid
            or info.get('application_version') != version() or info.get('review_only')
            or info.get('nuget') != nuget_locks(rid)
            or info.get('bundled_plugins') != {value[0]: value[1] for value in PLUGINS.values()}):
        raise ValueError('A macOS build part has unexpected source, versions or review-only native files')
    sources = json.loads((part / MANIFESTS[1]).read_text())
    check_review(sources)
    validation = json.loads((part / 'BUILD_VALIDATION.json').read_text())
    required = ('plugin_discovery', 'existing_typing_checks', 'main_window_opened',
                'main_window_closed_normally', 'mac_say_synthesis', 'sdl_dummy_playback')
    if (validation.get('source_commit') != head or validation.get('runtime_identifier') != rid
            or any(validation.get(key) is not True for key in required)):
        raise ValueError('A real macOS build has incomplete runtime validation')
    binary = part / (base_name(rid) + '.zip')
    with zipfile.ZipFile(binary) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or archive.testzip() is not None:
            raise ValueError('The macOS archive is corrupt or has duplicate members')
        for name in names:
            path = pathlib.PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts or '\\' in name:
                raise ValueError('The macOS ZIP contains an unsafe member')
        prefix = 'payload/'
        files = json.loads(archive.read(prefix + 'BUNDLE_FILES.json'))
        actual_files = {name.removeprefix(prefix + 'DictationAssistant.app/') for name in names
                        if name.startswith(prefix + 'DictationAssistant.app/') and not name.endswith('/')}
        if actual_files != set(files):
            raise ValueError('The macOS archive has unlisted or missing application files')
        if (json.loads(archive.read(prefix + 'BUNDLE_INFO.json')) != info
                or json.loads(archive.read(prefix + MANIFESTS[1])) != sources
                or archive.read(prefix + MANIFESTS[0]) != (part / MANIFESTS[0]).read_bytes()):
            raise ValueError('External and packaged macOS inventories differ')
        for name, record in files.items():
            item = archive.getinfo(prefix + 'DictationAssistant.app/' + name)
            data = archive.read(item)
            if 'symlink_target' in record:
                if data.decode() != record['symlink_target']:
                    raise ValueError('A bundle link differs from its sealed file inventory')
            elif hashlib.sha256(data).hexdigest() != record['sha256']:
                raise ValueError('A bundle file differs from its sealed file inventory: ' + name)
        executable = archive.getinfo(prefix + 'DictationAssistant.app/Contents/MacOS/DictationAssistant.App')
        if not executable.external_attr >> 16 & stat.S_IXUSR:
            raise ValueError('The macOS apphost lost its executable mode in ZIP packaging')
    return info, sources, validation, binary


def assemble(args):
    head = git('rev-parse', 'HEAD')
    if args.tag != TAG or git('rev-parse', f'refs/tags/{TAG}^{{commit}}') != head:
        raise ValueError('The exact R8 tag must match the release source HEAD')
    if not args.acknowledge_license_review:
        raise ValueError('The existing Avalonia combination review must be explicitly disclosed')
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    parts = []
    for rid in RIDS:
        part = args.parts_root / rid
        info, sources, validation, binary = verify_part(part, rid, head)
        destination = output / binary.name
        shutil.copyfile(binary, destination)
        parts.append((rid, part, info, sources, validation, destination))
    review = {'schema_version': 1, 'tag': TAG, 'application_version': version(), 'source_commit': head,
              'platforms': list(RIDS), 'minimum_macos': '15.0', 'plugin_api_version': 1, 'host_feature_level': 5,
              'bundled_plugins': {value[0]: value[1] for value in PLUGINS.values()},
              'publication_decision': 'explicitly_requested_publication_with_declared_unresolved_license_review',
              'license_review_resolved': False, 'acknowledged_issue_ids': [KNOWN_ISSUE],
              'apple_notarized': False, 'signing': 'ad-hoc',
              'build_validation': {rid: validation for rid, _, _, _, validation, _ in parts},
              'release_review': {rid: sources['release_review'] for rid, _, _, sources, _, _ in parts}}
    review_bytes = (json.dumps(review, ensure_ascii=False, indent=2) + '\n').encode()
    notes = f'''# R8 · macOS 整合版

自带背诵练习 1.4.1 和 Word / Excel 导入 1.2.0，解压后即可使用，无需安装 .NET 或插件。

需要 macOS 15 或更新版本。Intel 电脑选 `macos-x64.zip`，M 系列电脑选 `macos-arm64.zip`。
两包功能相同，按处理器分别提供 .NET 和原生运行库。将解压后的 `DictationAssistant.app` 拖到“应用程序”打开。

支持文档配对、静音计时、系统语音听写和中途结算。翻译凭据在插件配置中输入，macOS 目前仅本次使用。

两个处理器版本均在对应 macOS runner 上完成构建、双插件加载、现有逻辑检查、系统语音合成、SDL dummy 播放及主窗口开闭。
dummy 播放不代表已验证扬声器声音；中文输入法、完整背诵交互和真实在线翻译仍需实机检查。

本版为未经过 Apple 公证的预发布，使用临时签名。首次打开如被系统拦截，请核对来源后从“系统设置 → 隐私与安全性”确认打开。
Avalonia/MS-PL 与 AGPL 的组合授权事项仍未解决，原始许可、配套源码和具体声明随附件保留。

`*-source.zip` 是完整配套源码；`SHA256SUMS` 用于核验下载。对应提交：`{head}`。
'''
    notes_bytes = notes.encode()
    (output / 'RELEASE_REVIEW.json').write_bytes(review_bytes)
    (output / 'RELEASE_NOTES.md').write_bytes(notes_bytes)
    source = output / f'DictationAssistant-{version()}-macos-source.zip'
    prefix = f'DictationAssistant-{version()}-source/'
    subprocess.run(['git', 'archive', '--format=zip', '--prefix=' + prefix, '--output=' + str(source), head],
                   cwd=ROOT, check=True)
    cache = ROOT / 'artifacts/macos-source-cache'
    cache.mkdir(parents=True, exist_ok=True)
    added = {}
    with zipfile.ZipFile(source, 'a', compression=zipfile.ZIP_DEFLATED) as archive:
        for rid, part, info, sources, _, _ in parts:
            for locked in info['nuget']['lock_files']:
                if hashlib.sha256(archive.read(prefix + locked['path'])).hexdigest() != locked['sha256']:
                    raise ValueError('A build NuGet lock differs from the full corresponding-source archive')
            index = collect(part / MANIFESTS[1], cache)
            for item in index.get('inline_source_files', []):
                if hashlib.sha256(archive.read(prefix + item['path'])).hexdigest() != item['sha256']:
                    raise ValueError('An inline dependency source differs from the immutable release commit')
            archive.writestr(prefix + f'platforms/{rid}/SOURCE_ARCHIVES.json', json.dumps(index, ensure_ascii=False, indent=2) + '\n')
            for name in (*MANIFESTS, 'BUILD_VALIDATION.json', 'BUNDLE_INFO.json'):
                archive.write(part / name, prefix + f'platforms/{rid}/' + name)
            for item in index['archives']:
                if item.get('archive_path'):
                    name = prefix + 'third-party-sources/' + item['archive_path']
                    if name in added and added[name] != digest(cache / item['archive_path']):
                        raise ValueError('Two platforms supplied different source bytes under the same filename')
                    if name not in added:
                        archive.write(cache / item['archive_path'], name, compress_type=zipfile.ZIP_STORED)
                        added[name] = digest(cache / item['archive_path'])
            for path in sorted((part / 'runtime-notices').rglob('*')):
                if path.is_file():
                    name = prefix + path.relative_to(part / 'runtime-notices').as_posix()
                    if name in added and added[name] != digest(path):
                        raise ValueError('Two platforms supplied different runtime notices under the same filename')
                    if name not in added:
                        archive.write(path, name)
                        added[name] = digest(path)
        archive.writestr(prefix + 'RELEASE_REVIEW.json', review_bytes)
        archive.writestr(prefix + 'RELEASE_NOTES.md', notes_bytes)
    for _, _, _, _, _, binary in parts:
        with zipfile.ZipFile(binary, 'a', compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('payload/RELEASE_REVIEW.json', review_bytes)
            archive.writestr('payload/RELEASE_NOTES.md', notes_bytes)
    assets = [*(part[-1] for part in parts), source, output / 'RELEASE_REVIEW.json', output / 'RELEASE_NOTES.md']
    for path in [source, *(part[-1] for part in parts)]:
        with zipfile.ZipFile(path) as archive:
            if len(archive.namelist()) != len(set(archive.namelist())) or archive.testzip() is not None:
                raise ValueError('A final release ZIP is corrupt or contains duplicate members')
    (output / 'SHA256SUMS').write_text(''.join(f'{digest(path)}  {path.name}\n' for path in assets), encoding='ascii')
    print('Prepared two native macOS app packages, full corresponding source, review, notes and checksums.')


def publish(args):
    head = git('rev-parse', 'HEAD')
    source_run = os.environ.get('MACOS_BUILD_RUN_ID', '')
    if (args.tag != TAG or git('rev-parse', f'refs/tags/{TAG}^{{commit}}') != head
            or os.environ.get('GITHUB_SHA') != head or os.environ.get('GITHUB_EVENT_NAME') != 'workflow_dispatch'
            or os.environ.get('GITHUB_REF') != 'refs/tags/' + TAG
            or not re.fullmatch('[1-9][0-9]*', source_run) or source_run == os.environ.get('GITHUB_RUN_ID')):
        raise ValueError('Only an explicit R8 tag dispatch promoting a prior verified build can be published')
    repository = os.environ['GH_REPO']
    if repository != 'lilixiu708-ai/DictationAssistant_IntegratedTyping':
        raise ValueError('Unexpected release repository')
    folder = args.output_dir.resolve()
    assets = {path.name: path for path in folder.iterdir() if path.is_file()}
    expected = {base_name(rid) + '.zip' for rid in RIDS} | {
        f'DictationAssistant-{version()}-macos-source.zip', 'RELEASE_REVIEW.json', 'RELEASE_NOTES.md', 'SHA256SUMS'}
    if set(assets) != expected:
        raise ValueError('The final macOS asset set is incomplete or contains unrelated files')
    checked = set()
    for line in assets['SHA256SUMS'].read_text(encoding='ascii').splitlines():
        checksum, name = line.split('  ', 1)
        if name in checked or name not in assets or not re.fullmatch('[0-9a-f]{64}', checksum) or digest(assets[name]) != checksum:
            raise ValueError('A release checksum is invalid or changed')
        checked.add(name)
    if checked != set(assets) - {'SHA256SUMS'}:
        raise ValueError('Release checksum coverage is incomplete')
    review = json.loads(assets['RELEASE_REVIEW.json'].read_text())
    if (review.get('source_commit') != head or review.get('tag') != TAG
            or review.get('acknowledged_issue_ids') != [KNOWN_ISSUE] or review.get('license_review_resolved') is not False):
        raise ValueError('The macOS release disclosure changed')
    pages = json.loads(run('gh', 'api', '--paginate', '--slurp', f'repos/{repository}/releases?per_page=100'))
    if any(item['tag_name'] == TAG for page in pages for item in page):
        raise ValueError('Refusing to replace an existing release or draft')
    run('gh', 'release', 'create', TAG, '--draft', '--prerelease', '--latest=false', '--verify-tag',
        '--target', head, '--title', 'R8 · macOS 整合版（Intel / Apple Silicon）', '--notes-file', assets['RELEASE_NOTES.md'])
    run('gh', 'release', 'upload', TAG, *assets.values())
    pages = json.loads(run('gh', 'api', '--paginate', '--slurp', f'repos/{repository}/releases?per_page=100'))
    matches = [item for page in pages for item in page if item['tag_name'] == TAG]
    if len(matches) != 1:
        raise ValueError('Expected exactly the newly created macOS draft')
    draft = matches[0]
    if not draft['draft'] or draft['target_commitish'] != head or len(draft['assets']) != len(assets):
        raise ValueError('The macOS draft metadata changed')
    for item in draft['assets']:
        path = assets[item['name']]
        if item.get('state') != 'uploaded' or item['size'] != path.stat().st_size or item.get('digest') != 'sha256:' + digest(path):
            raise ValueError('An uploaded macOS asset differs from its original file')
    remote = run('git', 'ls-remote', 'origin', f'refs/tags/{TAG}', f'refs/tags/{TAG}^{{}}', cwd=ROOT)
    refs = dict(line.split()[::-1] for line in remote.splitlines())
    if refs.get(f'refs/tags/{TAG}^{{}}', refs.get(f'refs/tags/{TAG}')) != head:
        raise ValueError('The macOS release tag changed before publication')
    run('gh', 'release', 'edit', TAG, '--tag', TAG, '--target', head, '--draft=false', '--prerelease', '--latest=false')
    print(run('gh', 'release', 'view', TAG, '--json', 'url', '--jq', '.url').strip())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument('--bundle', action='store_true')
    modes.add_argument('--validate', action='store_true')
    modes.add_argument('--assemble', action='store_true')
    modes.add_argument('--publish', action='store_true')
    parser.add_argument('--rid', choices=RIDS)
    parser.add_argument('--parts-root', type=pathlib.Path, default=ROOT / 'artifacts/macos/parts')
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    parser.add_argument('--tag', default=TAG)
    parser.add_argument('--review-only', action='store_true')
    parser.add_argument('--acknowledge-license-review', action='store_true')
    args = parser.parse_args()
    if (args.bundle or args.validate) and not args.rid:
        parser.error('--bundle and --validate require --rid')
    if args.review_only and not args.bundle:
        parser.error('--review-only is a bundle diagnostic mode, never a publication override')
    try:
        bundle(args) if args.bundle else validate(args) if args.validate else assemble(args) if args.assemble else publish(args)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        parser.exit(1, f'macOS release preparation failed: {error}\n')


if __name__ == '__main__':
    main()
