"""Prepare versioned release assets from a clean, tagged, validated build."""
import argparse
import hashlib
import pathlib
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('tag', help='Release tag, for example v4.0.0-typing.1')
args = parser.parse_args()

project = root / 'src/DictationAssistant.App/DictationAssistant.App.csproj'
version = ET.parse(project).findtext('.//Version')
if args.tag != f'v{version}':
    raise SystemExit('Release tag must match the application Version prefixed with v')

def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=root, text=True).strip()

head = git('rev-parse', 'HEAD')
if git('rev-parse', '--verify', f'refs/tags/{args.tag}^{{commit}}') != head:
    raise SystemExit('Build HEAD must match the release tag')
subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=root, check=True)
if git('ls-files', '--others', '--exclude-standard'):
    raise SystemExit('Commit or remove untracked files before preparing release assets')
subprocess.run([sys.executable, str(root / 'scripts/verify_build.py')], check=True)

archive = root / 'artifacts/DictationAssistant_IntegratedTyping_Windows_x64.zip'
with zipfile.ZipFile(archive) as zf:
    app = next(name for name in zf.namelist() if name.removeprefix('./') == 'DictationAssistant.App.dll')
    if f'{version}+{head}'.encode() not in zf.read(app):
        raise SystemExit('Application build metadata does not match the release version and commit; rebuild it')

output = root / 'artifacts/release' / args.tag
output.mkdir(parents=True, exist_ok=True)
portable = output / f'DictationAssistant-{version}-win-x64-portable.zip'
source = output / f'DictationAssistant-{version}-source.zip'
shutil.copyfile(archive, portable)
subprocess.run([
    'git', 'archive', '--format=zip', f'--prefix=DictationAssistant-{version}-source/',
    f'--output={source}', args.tag,
], cwd=root, check=True)

checksums = []
for asset in (portable, source):
    digest = hashlib.sha256()
    with asset.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    checksums.append(f'{digest.hexdigest()}  {asset.name}\n')
(output / 'SHA256SUMS').write_bytes(''.join(checksums).encode('ascii'))
print(f'Release assets: {output}\nSource commit: {head}')
