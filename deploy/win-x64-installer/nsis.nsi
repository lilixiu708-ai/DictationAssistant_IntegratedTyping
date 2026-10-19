!pragma warning error all
!include "MUI2.nsh"
!include "x64.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\..\artifacts\win-x64-self-contained"
!endif

!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "..\..\artifacts\DictationAssistant-${APP_VERSION}-win-x64-installer.exe"
!endif

!define PRODUCT_NAME "自动默写"
!define PRODUCT_NAME_EN "DictationAssistant"
!define PRODUCT_PUBLISHER "DictationAssistant Contributors"
!define PRODUCT_EXE "DictationAssistant.App.exe"
!define REGPATH_UNINSTSUBKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME_EN}"
!define PRODUCT_REGKEY "Software\${PRODUCT_NAME_EN}"

Unicode true
RequestExecutionLevel Admin
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME_EN}"
InstallDirRegKey HKLM "${PRODUCT_REGKEY}" "InstallFolder"
Name "${PRODUCT_NAME} ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"

!define MUI_ABORTWARNING
!define MUI_ICON "..\shared\DictationAssistant.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_NAME}"

!define MUI_LANGDLL_REGISTRY_ROOT "HKLM"
!define MUI_LANGDLL_REGISTRY_KEY "${PRODUCT_REGKEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\shared\EULA.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

!insertmacro MUI_RESERVEFILE_LANGDLL

Section "Program" SecProgram
    SetOutPath "$INSTDIR"
    File /r "${PUBLISH_DIR}\*"

    WriteRegStr HKLM "${PRODUCT_REGKEY}" "InstallFolder" "$INSTDIR"

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE},0"
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "${REGPATH_UNINSTSUBKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKLM "${REGPATH_UNINSTSUBKEY}" "NoModify" 1
    WriteRegDWORD HKLM "${REGPATH_UNINSTSUBKEY}" "NoRepair" 1

    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
    CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall ${PRODUCT_NAME}.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Function .onInit
    !insertmacro MUI_LANGDLL_DISPLAY
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "This installer only supports 64-bit Windows."
        Abort
    ${EndIf}
    SetRegView 64
FunctionEnd

Section "Uninstall"
    SetRegView 64
    SetShellVarContext all
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKLM "${REGPATH_UNINSTSUBKEY}"
    DeleteRegKey HKLM "${PRODUCT_REGKEY}"
SectionEnd

Function un.onInit
    !insertmacro MUI_UNGETLANGUAGE
FunctionEnd
