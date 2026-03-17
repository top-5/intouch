; InTouch NSIS Installer Script
; Called by build.ps1 — defines passed on command line:
;   /DVERSION=x.y.z  /DARCH=x64|arm64  /DPUBLISH_DIR=...  /DOUTPUT_DIR=...

!include "MUI2.nsh"
!include "FileFunc.nsh"

;--- Defaults (overridden by build.ps1 /D flags) ---
!ifndef VERSION
  !define VERSION "0.1.0"
!endif
!ifndef ARCH
  !define ARCH "x64"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR must be defined (path to dotnet publish output)"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "."
!endif

;--- Installer metadata ---
Name "InTouch ${VERSION}"
OutFile "${OUTPUT_DIR}\InTouch-${VERSION}-${ARCH}-setup.exe"
InstallDir "$PROGRAMFILES64\InTouch"
InstallDirRegKey HKLM "Software\InTouch" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "InTouch ${VERSION} (${ARCH})"

;--- MUI pages ---
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\InTouch.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch InTouch"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

;--- Version info embedded in the .exe ---
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName"     "InTouch"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "FileDescription" "InTouch — Wacom touch-to-cursor remapper"
VIAddVersionKey "FileVersion"     "${VERSION}"
VIAddVersionKey "LegalCopyright"  "MIT License"

;==============================
Section "Install"
  SetOutPath "$INSTDIR"

  ; Copy all published files
  File /r "${PUBLISH_DIR}\*.*"

  ; Uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\InTouch"
  CreateShortcut  "$SMPROGRAMS\InTouch\InTouch.lnk"           "$INSTDIR\InTouch.exe"
  CreateShortcut  "$SMPROGRAMS\InTouch\Uninstall InTouch.lnk" "$INSTDIR\Uninstall.exe"

  ; Add/Remove Programs registry
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "DisplayName"     "InTouch ${VERSION} (${ARCH})"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "Publisher"       "InTouch"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "NoRepair" 1

  ; Estimated size for Add/Remove Programs
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch" \
                "EstimatedSize" $0

  ; Remember install dir
  WriteRegStr HKLM "Software\InTouch" "InstallDir" "$INSTDIR"

SectionEnd

;==============================
Section "Uninstall"

  ; Kill running instance (silent)
  nsExec::ExecToLog 'taskkill /F /IM InTouch.exe'

  ; Remove installed files
  RMDir /r "$INSTDIR"

  ; Remove Start Menu
  RMDir /r "$SMPROGRAMS\InTouch"

  ; Remove registry entries
  DeleteRegKey  HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\InTouch"
  DeleteRegKey  HKLM "Software\InTouch"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "InTouch"

SectionEnd
