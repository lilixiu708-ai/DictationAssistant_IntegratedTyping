# Compile a Windows self-contained host + two independently sideloaded plugins.
# Requires .NET 10 SDK. Source is AGPL-3.0-or-later.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location $PSScriptRoot
try {
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '没有找到 dotnet。请安装 .NET 10 SDK (10.0.402 或更新的 10.0.x 版本)。'
  }
  $sdkVersion = (& dotnet --version).Trim()
  if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) {
    throw "需要 .NET 10 SDK。当前为：$sdkVersion"
  }
  function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet 失败，退出码 $LASTEXITCODE" }
  }

  $hostProject = 'src/DictationAssistant.App/DictationAssistant.App.csproj'
  $docxProject = 'plugins/DictationAssistant.Importers.Docx/DictationAssistant.Importers.Docx.csproj'
  $typingProject = 'plugins/DictationAssistant.Practices.Typing/DictationAssistant.Practices.Typing.csproj'
  $appOut = Join-Path $PSScriptRoot 'artifacts/Windows-x64'
  $docxOut = Join-Path $PSScriptRoot 'artifacts/DocxVocabulary'
  $typingOut = Join-Path $PSScriptRoot 'artifacts/SynchronousTyping'
  New-Item -Force -ItemType Directory $appOut, $docxOut, $typingOut | Out-Null

  # The old host lockfile still applies: only compatible plugin interfaces are added.
  Invoke-DotNet restore $hostProject -r win-x64 --locked-mode
  Invoke-DotNet publish $hostProject -c Release -r win-x64 --self-contained true --no-restore `
    -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -o $appOut

  Invoke-DotNet publish $docxProject -c Release -p:DebugType=None -p:DebugSymbols=false -o $docxOut
  Invoke-DotNet publish $typingProject -c Release -p:DebugType=None -p:DebugSymbols=false -o $typingOut

  $docxInstalled = Join-Path $appOut 'Plugins/DocxVocabulary'
  $typingInstalled = Join-Path $appOut 'Plugins/SynchronousTyping'
  New-Item -Force -ItemType Directory $docxInstalled, $typingInstalled | Out-Null

  # Preserve existing Word importer and install new independent DLL plugin.
  Get-ChildItem -Path $docxOut -File | Where-Object Name -ne 'DictationAssistant.Plugin.Abstractions.dll' |
    Copy-Item -Destination $docxInstalled -Force
  foreach ($name in @('plugin.json','DictationAssistant.Practices.Typing.dll',
                      'DictationAssistant.Practices.Typing.deps.json')) {
    $file = Join-Path $typingOut $name
    if (Test-Path $file) { Copy-Item $file -Destination $typingInstalled -Force }
  }

  Copy-Item LICENSE.md $appOut -Force
  Copy-Item deploy/shared/EULA.txt $appOut -Force
  Copy-Item docs/同步打字插件使用说明.md $appOut -Force
  $zip = Join-Path $PSScriptRoot 'artifacts/DictationAssistant_Typing_Windows_x64.zip'
  if (Test-Path $zip) { Remove-Item $zip -Force }
  Compress-Archive -Path (Join-Path $appOut '*') -DestinationPath $zip -CompressionLevel Optimal
  Write-Host "已构建 Windows x64 完整包：$zip"
  Write-Host "独立同步打字插件 DLL：$typingInstalled"
}
finally { Pop-Location }
