# Standard-library-only archive checks on the Windows CI runner.
import json
import pathlib
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
archive = root / 'artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip'
assert archive.is_file(), f'Missing Windows package: {archive}'
required = {
    'DictationAssistant.App.exe',
    'DictationAssistant.App.dll',
    'DictationAssistant.App.runtimeconfig.json',
    'DictationAssistant.Plugin.Abstractions.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'System.Private.CoreLib.dll',
    'libSkiaSharp.dll',
    'libHarfBuzzSharp.dll',
    'av_libglesv2.dll',
    'SDL2.dll',
    'bass.dll',
    'Plugins/DocxVocabulary/plugin.json',
    'Plugins/DocxVocabulary/DictationAssistant.Importers.Docx.dll',
    'Plugins/SynchronousTyping/plugin.json',
    'Plugins/SynchronousTyping/DictationAssistant.Practices.Typing.dll',
}
with zipfile.ZipFile(archive) as zf:
    names = {name.removeprefix('./') for name in zf.namelist()}
    missing = sorted(required - names)
    assert not missing, f'Missing from archive: {missing}'
    assert zf.testzip() is None, 'Corrupted zip member'
    plugin_runtimes = sorted(name for name in names if name.startswith('Plugins/') and '/runtimes/' in name)
    assert not plugin_runtimes, f'Plugins must share the host graphics libraries: {plugin_runtimes}'
    runtime = json.loads(zf.read(next(n for n in zf.namelist() if n.endswith('DictationAssistant.App.runtimeconfig.json'))))
    assert runtime['runtimeOptions'].get('includedFrameworks'), 'Windows package must include its .NET runtime'
    docx = json.loads(zf.read(next(n for n in zf.namelist() if n.endswith('Plugins/DocxVocabulary/plugin.json'))))
    typing = json.loads(zf.read(next(n for n in zf.namelist() if n.endswith('Plugins/SynchronousTyping/plugin.json'))))
    assert docx.get('kind', 'document-importer') == 'document-importer' and '.docx' in docx['extensions']
    assert typing['kind'] == 'practice' and typing['id'] == 'synchronous-typing'
print(f'Validated {len(names)} portable package files: {archive}')
