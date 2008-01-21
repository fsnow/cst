
; CST 4.0 Installer Script
; (uses NSIS Modern User Interface module)

;--------------------------------
;Include Modern UI

  !include "MUI.nsh"

;--------------------------------
;General

  ;Name and file
  Name "Chattha Sangayana Tipitaka 4.0"
  OutFile "Cst4_Installer.exe"

  ;Default installation folder
  InstallDir "$PROGRAMFILES\Chattha Sangayana Tipitaka 4.0"
  
  ;Vista redirects $SMPROGRAMS to all users without this
  RequestExecutionLevel admin

;--------------------------------
;Variables

  Var MUI_TEMP
  Var STARTMENU_FOLDER

;--------------------------------
;Interface Settings

  !define MUI_ABORTWARNING
  
  !define MUI_ICON "Cst.ico"
  !define MUI_UNICON "classic-uninstall.ico"

;--------------------------------
;Pages

  !insertmacro MUI_PAGE_LICENSE "InstallerLicense.txt"
  ;insertmacro MUI_PAGE_COMPONENTS
  !insertmacro MUI_PAGE_DIRECTORY
  
  !insertmacro MUI_PAGE_STARTMENU Application $STARTMENU_FOLDER
  
  !insertmacro MUI_PAGE_INSTFILES
  
  !insertmacro MUI_UNPAGE_CONFIRM
  !insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
;Languages
 
  !insertmacro MUI_LANGUAGE "English"
  
;--------------------------------
;Functions


 ; GetParent
 ; input, top of stack  (e.g. C:\Program Files\Stuff)
 ; output, top of stack (replaces, with e.g. C:\Program Files)
 ; modifies no other variables.
 ;
 ; Usage:
 ;   Push "C:\Program Files\Directory\Whatever"
 ;   Call GetParent
 ;   Pop $R0
 ;   ; at this point $R0 will equal "C:\Program Files\Directory"

 Function un.GetParent
 
   Exch $R0
   Push $R1
   Push $R2
   Push $R3
   
   StrCpy $R1 0
   StrLen $R2 $R0
   
   loop:
     IntOp $R1 $R1 + 1
     IntCmp $R1 $R2 get 0 get
     StrCpy $R3 $R0 1 -$R1
     StrCmp $R3 "\" get
     Goto loop
   
   get:
     StrCpy $R0 $R0 -$R1
     
     Pop $R3
     Pop $R2
     Pop $R1
     Exch $R0
     
 FunctionEnd
 
 
;--------------------------------
;Installer Sections

Section "Dummy Section" SecDummy

  ; ***************************** Specific to beta 13 ****************************
  
  ;Delete the old DAT files
  Delete "$INSTDIR\*.dat"
  
  ; Force a re-index
  Delete "$INSTDIR\Index\*.*"
  RMDir "$INSTDIR\Index"
  
  ; ***************************** End beta 13 ************************************
  
  
  ; Remove bogus German translation
  Delete "$INSTDIR\de\*.*"
  RMDir "$INSTDIR\de"
  
  ; Remove dictionary from old location
  Delete "$INSTDIR\Reference\pali-english-dictionary.txt"
  
  
  
  
  SetOutPath "$INSTDIR"
  File bin\Debug\Cst4.exe
  File bin\Debug\CST.dll
  File bin\Debug\Lucene.Net.dll
  File bin\Debug\Microsoft.mshtml.dll
  File bin\Debug\usp10.dll
  
  SetOutPath "$INSTDIR\de"
  File bin\Debug\de\*.*

  SetOutPath "$INSTDIR\en"
  File bin\Debug\en\*.*
  
  SetOutPath "$INSTDIR\gu"
  File bin\Debug\gu\*.*
  
  SetOutPath "$INSTDIR\hi"
  File bin\Debug\hi\*.*
  
  SetOutPath "$INSTDIR\id"
  File bin\Debug\id\*.*
  
  SetOutPath "$INSTDIR\it"
  File bin\Debug\it\*.*
  
  SetOutPath "$INSTDIR\sv"
  File bin\Debug\sv\*.*
  
  SetOutPath "$INSTDIR\ta"
  File bin\Debug\ta\*.*
  
  SetOutPath "$INSTDIR\zh-CHS"
  File bin\Debug\zh-CHS\*.*
  
  SetOutPath "$INSTDIR\zh-CHT"
  File bin\Debug\zh-CHT\*.*
  
  SetOutPath "$INSTDIR\Fonts"
  File bin\Debug\Fonts\*.*
  
  ;SetOutPath "$INSTDIR\Images"
  ;File bin\Debug\Images\*.*
  
  SetOutPath "$INSTDIR\Reference"
  File bin\Debug\Reference\en\pali-english-dictionary.txt
  File bin\Debug\Reference\pali-hindi-dictionary.txt
  
  SetOutPath "$INSTDIR\Xml"
  File bin\Debug\Xml\*.mul.xml 
  File bin\Debug\Xml\*.att.xml 
  File bin\Debug\Xml\*.tik.xml 
  File bin\Debug\Xml\*.nrf.xml 
  File bin\Debug\Xml\tipitaka-deva.xsl 
  
  SetOutPath "$INSTDIR\Xsl"
  File bin\Debug\Xsl\*.*
  
  ;Create uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  
  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    
    ;Create shortcuts
    CreateDirectory "$SMPROGRAMS\$STARTMENU_FOLDER"
    CreateShortCut "$SMPROGRAMS\$STARTMENU_FOLDER\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    CreateShortCut "$SMPROGRAMS\$STARTMENU_FOLDER\Chattha Sangayana Tipitaka 4.0.lnk" "$INSTDIR\Cst4.exe"
  
  !insertmacro MUI_STARTMENU_WRITE_END

SectionEnd

;--------------------------------
;Descriptions

  ;Language strings
  LangString DESC_SecDummy ${LANG_ENGLISH} "A test section."

  ;Assign language strings to sections
  !insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDummy} $(DESC_SecDummy)
  !insertmacro MUI_FUNCTION_DESCRIPTION_END
 
;--------------------------------
;Uninstaller Section

Section "Uninstall"

  ;ADD YOUR OWN FILES HERE...

  Delete "$INSTDIR\de\*.*"
  RMDir "$INSTDIR\de"
  
  Delete "$INSTDIR\en\*.*"
  RMDir "$INSTDIR\en"
  
  Delete "$INSTDIR\hi\*.*"
  RMDir "$INSTDIR\hi"
  
  Delete "$INSTDIR\Fonts\*.*"
  RMDir "$INSTDIR\Fonts"
  
  Delete "$INSTDIR\Reference\en\pali-english-dictionary.txt"
  Delete "$INSTDIR\Reference\pali-hindi-dictionary.txt"
  RMDir "$INSTDIR\Reference"
  
  Delete "$INSTDIR\Xml\*.xml"
  Delete "$INSTDIR\Xml\tipitaka-deva.xsl"
  RMDir "$INSTDIR\Xml"
  
  Delete "$INSTDIR\Xsl\*.xsl"
  RMDir "$INSTDIR\Xsl"
  
  Delete "$INSTDIR\Index\*.*"
  RMDir "$INSTDIR\Index"
  
  Delete "$INSTDIR\Cst4.exe"
  Delete "$INSTDIR\CST.dll"
  Delete "$INSTDIR\Lucene.Net.dll"
  Delete "$INSTDIR\Microsoft.mshtml.dll"
  Delete "$INSTDIR\usp10.dll"
  Delete "$INSTDIR\*.dat"
  
  Delete "$INSTDIR\Uninstall.exe"

  ; Delete installation directory
  RMDir "$INSTDIR"
  
  ; Get parent of installation directory
  Push "$INSTDIR"
  Call un.GetParent
  Pop $INSTDIR
  
  ; Delete the parent if it is empty (no /r, which would delete if not empty)
  RMDir "$INSTDIR"
  
  !insertmacro MUI_STARTMENU_GETFOLDER Application $MUI_TEMP
    
  Delete "$SMPROGRAMS\$MUI_TEMP\Uninstall.lnk"
  Delete "$SMPROGRAMS\$MUI_TEMP\Chattha Sangayana Tipitaka 4.0.lnk"
  
  ;Delete empty start menu parent diretories
  StrCpy $MUI_TEMP "$SMPROGRAMS\$MUI_TEMP"
 
  startMenuDeleteLoop:
	ClearErrors
    RMDir $MUI_TEMP
    GetFullPathName $MUI_TEMP "$MUI_TEMP\.."
    
    IfErrors startMenuDeleteLoopDone
  
    StrCmp $MUI_TEMP $SMPROGRAMS startMenuDeleteLoopDone startMenuDeleteLoop
  startMenuDeleteLoopDone:

  ;Foo

SectionEnd




