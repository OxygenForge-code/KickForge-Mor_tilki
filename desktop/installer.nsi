; KickForge - tek dosya Windows kurucusu
;
; build.js bunu "makensis installer.nsi" ile derler, cikti dist/KickForge.exe.
; Akis:
;   - bu makineye kurulmus ve surumu ayniysa: ekran gostermeden calistir
;   - degilse (ilk acilis ya da yeni surum): $LOCALAPPDATA\KickForge'e kur,
;     kisayol birak, sonra calistir
;
; RequestExecutionLevel user -> yonetici gerekmez, Program Files'a dokunulmaz.
;
; Not: bu dosyayi ASCII tutuyoruz. Turkce karakterler windows'ta makensis
; tarafindan ACP olarak okunuyor ve betik ayristirmasi degisebiliyor.

Unicode true

!define APP "KickForge"
!ifndef VERSION
  !define VERSION "1.0.0"
!endif

Name "${APP}"
OutFile "dist\KickForge.exe"
InstallDir "$LOCALAPPDATA\${APP}"
InstallDirRegKey HKCU "Software\${APP}" "InstallDir"
RequestExecutionLevel user
; zlib: 268 MB'lik yuku tek parca lzma ile sikistirmak makensis'i zorluyordu.
; exe birkac MB buyuk cikiyor ama derleme saniyeler icinde bitiyor.
SetCompressor zlib
ShowInstDetails nevershow
AutoCloseWindow true
CRCCheck on

Page instfiles

Function .onInit
  ; kurulu ve guncelse kurulum ekrani acmadan dogrudan uygulamayi baslat
  ReadRegStr $R0 HKCU "Software\${APP}" "Version"
  StrCmp $R0 "${VERSION}" 0 kur
  IfFileExists "$INSTDIR\${APP}.exe" 0 kur
    Exec "$INSTDIR\${APP}.exe"
    Abort
  kur:
FunctionEnd

Section "KickForge" SecMain
  SetOutPath "$INSTDIR"
  ; dist/KickForge-win32-x64 icerigi alt dizinleriyle kopyalanir
  File /r "dist\KickForge-win32-x64\*.*"

  WriteRegStr HKCU "Software\${APP}" "Version" "${VERSION}"
  WriteRegStr HKCU "Software\${APP}" "InstallDir" "$INSTDIR"
  CreateShortCut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${APP}.exe"
SectionEnd

Function .onInstSuccess
  Exec "$INSTDIR\${APP}.exe"
FunctionEnd
