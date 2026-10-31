; KickForge · tek dosya Windows kurucusu
;
; build.js bunu "makensis installer.nsi" ile derler, çıktı dist/KickForge.exe.
; Akış:
;   - bu makineye kurulmuş ve sürümü aynıysa: ekran göstermeden çalıştır
;   - değilse (ilk açılış ya da yeni sürüm): $LOCALAPPDATA\KickForge'e kur,
;     kısayol bırak, sonra çalıştır
;
; RequestExecutionLevel user → yönetici gerekmez, Program Files'a dokunulmaz.

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
; lzma yerine zlib: 268 MB'lık yükü tek parça lzma ile sıkıştırmak
; makensis'i bellek ve dosya kilidi tarafında zorluyordu. exe birkaç MB
; büyük çıkıyor ama derleme saniyeler içinde ve tekrarlanabilir bitiyor.
SetCompressor zlib
ShowInstDetails nevershow
AutoCloseWindow true
CRCCheck on

Page instfiles

Function .onInit
  ; kurulu ve güncelse kurulum ekranı açmadan doğrudan uygulamayı başlat
  ReadRegStr $R0 HKCU "Software\${APP}" "Version"
  StrCmp $R0 "${VERSION}" 0 kur
  IfFileExists "$INSTDIR\${APP}.exe" 0 kur
    Exec "$INSTDIR\${APP}.exe"
    Abort
  kur:
FunctionEnd

Section "KickForge" SecMain
  SetOutPath "$INSTDIR"
  ; dist/KickForge-win32-x64 içeriği olduğu gibi (alt dizinleriyle) kopyalanır
  File /r "dist\KickForge-win32-x64\*.*"

  WriteRegStr HKCU "Software\${APP}" "Version" "${VERSION}"
  WriteRegStr HKCU "Software\${APP}" "InstallDir" "$INSTDIR"
  CreateShortCut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${APP}.exe"
SectionEnd

Function .onInstSuccess
  Exec "$INSTDIR\${APP}.exe"
FunctionEnd
