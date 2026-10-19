@echo off
setlocal EnableExtensions
REM Pure CMD build: does NOT start PowerShell. Requires .NET 10 SDK.
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] .NET 10 SDK not found. For a no-install build use GitHub Actions.
  exit /b 1
)
for /f "usebackq delims=" %%v in (`dotnet --version`) do set "SDKVER=%%v"
if not "%SDKVER:~0,3%"=="10." (
  echo [ERROR] .NET 10 SDK is required; installed: %SDKVER%
  exit /b 1
)

set "ROOT=%CD%"
set "OUTPUT=%ROOT%\artifacts\Windows-x64"
set "WORDPLUGIN=%OUTPUT%\Plugins\DocxVocabulary"
set "TYPEPLUGIN=%OUTPUT%\Plugins\SynchronousTyping"
if not exist "artifacts" mkdir "artifacts"
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

echo [1/6] Restore Windows host dependencies...
dotnet restore "src\DictationAssistant.App\DictationAssistant.App.csproj" -r win-x64 --locked-mode
if errorlevel 1 goto :failed
echo [2/6] Build native desktop host (self-contained)...
dotnet publish "src\DictationAssistant.App\DictationAssistant.App.csproj" -c Release -r win-x64 --self-contained true --no-restore -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT%"
if errorlevel 1 goto :failed
echo [3/6] Build the two independent plugins...
dotnet publish "plugins\DictationAssistant.Importers.Docx\DictationAssistant.Importers.Docx.csproj" -c Release -p:DebugType=None -p:DebugSymbols=false -o "%WORDPLUGIN%"
if errorlevel 1 goto :failed
dotnet publish "plugins\DictationAssistant.Practices.Typing\DictationAssistant.Practices.Typing.csproj" -c Release -p:DebugType=None -p:DebugSymbols=false -o "%TYPEPLUGIN%"
if errorlevel 1 goto :failed

echo [4/6] Check installed extension DLLs and manifests...
if not exist "%WORDPLUGIN%\DictationAssistant.Importers.Docx.dll" goto :missing
if not exist "%WORDPLUGIN%\plugin.json" goto :missing
if not exist "%TYPEPLUGIN%\DictationAssistant.Practices.Typing.dll" goto :missing
if not exist "%TYPEPLUGIN%\plugin.json" goto :missing
if not exist "%OUTPUT%\DictationAssistant.App.exe" goto :missing

echo [5/6] Run plugin timing and scoring smoke tests...
dotnet run --project "tests\TypingPlugin.SmokeTests\TypingPlugin.SmokeTests.csproj" -c Release
if errorlevel 1 goto :failed
copy /y "LICENSE.md" "%OUTPUT%\LICENSE.md" >nul
copy /y "deploy\shared\EULA.txt" "%OUTPUT%\EULA.txt" >nul
copy /y "docs\同步打字插件使用说明.md" "%OUTPUT%\同步打字插件使用说明.md" >nul

echo [6/6] Create portable ZIP...
if exist "artifacts\DictationAssistant_IntegratedTyping_Windows_x64.zip" del "artifacts\DictationAssistant_IntegratedTyping_Windows_x64.zip"
tar.exe -a -c -f "artifacts\DictationAssistant_IntegratedTyping_Windows_x64.zip" -C "%OUTPUT%" .
if errorlevel 1 goto :failed
echo [OK] artifacts\DictationAssistant_IntegratedTyping_Windows_x64.zip
exit /b 0

:missing
echo [ERROR] Build finished but plugin files are missing. Check output.
:failed
echo [ERROR] Build failed. No runnable DLL integration package produced.
exit /b 1
