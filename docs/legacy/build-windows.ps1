# Build the Windows sideloaded-plugin edition. 2026-10-19, AGPL-3.0-or-later.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'artifacts/Windows-x64'
$pluginOutputDirectory = Join-Path $projectRoot 'artifacts/DocxVocabulary'
$hostProject = Join-Path $projectRoot 'src/DictationAssistant.App/DictationAssistant.App.csproj'
$pluginProject = Join-Path $projectRoot 'plugins/DictationAssistant.Importers.Docx/DictationAssistant.Importers.Docx.csproj'

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

Push-Location $projectRoot
try {
    # Do not clear artifacts automatically: a developer may have put other
    # locally installed plugins beside this build.
    New-Item -ItemType Directory -Force -Path $outputDirectory, $pluginOutputDirectory | Out-Null
    Invoke-DotNet restore $hostProject -r win-x64 --locked-mode
    Invoke-DotNet publish $hostProject -c Release -r win-x64 --self-contained true --no-restore `
        -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:PublishAot=false `
        -o $outputDirectory

    # Publish independently. The host has no project/assembly reference to this
    # importer: the only link is plugin.json plus the shared contract DLL.
    Invoke-DotNet publish $pluginProject -c Release -p:DebugType=None -p:DebugSymbols=false `
        -o $pluginOutputDirectory
    $installedPluginDirectory = Join-Path $outputDirectory 'Plugins/DocxVocabulary'
    New-Item -ItemType Directory -Force -Path $installedPluginDirectory | Out-Null
    Copy-Item -Path (Join-Path $pluginOutputDirectory '*') -Destination $installedPluginDirectory -Recurse -Force
    Copy-Item (Join-Path $projectRoot 'LICENSE.md') $outputDirectory
    Copy-Item (Join-Path $projectRoot 'deploy/shared/EULA.txt') $outputDirectory
    Copy-Item (Join-Path $projectRoot 'docs/插件版使用说明.md') $outputDirectory
    Copy-Item (Join-Path $projectRoot 'plugins/DictationAssistant.Importers.Docx/README.md') $installedPluginDirectory
    Write-Host "Windows application: $outputDirectory"
    Write-Host "Standalone plugin: $pluginOutputDirectory"
}
finally {
    Pop-Location
}
