<#
  cst4-build-installer.ps1

  Automates the build of the CST4 installer.

  Assumptions:
  -- script is run from cst\src
  -- a Debug build of CST4 has been built already with Visual Studio.
  -- cst and tipitaka.org repositories are under a single parent directory.
  -- tipitaka.org local repo has latest data from correct branch

  FSnow, January 14, 2022
#>

$debugDir = '.\Cst4\bin\Debug'
$devaMasterDir = '..\..\tipitaka.org\deva master'

# Delete previous installer (original name, not renamed) and intermediate files
Remove-Item -Path $debugDir\CST4.msi
Remove-Item -Path $debugDir\CST4.wix*

# Copy cst/src/CST4.wxs to cst/src/Cst4/bin/Debug
Copy-Item -Path .\CST4.wxs -Destination $debugDir -Force

# Copy cst/Fonts to cst/src/Cst4/bin/Debug/Fonts
Copy-Item ..\Fonts\* -Destination $debugDir\Fonts -Force

# Copy cst/src/CST4-license.rtf to cst/src/Cst4/bin/Debug
Copy-Item -Path .\CST4-license.rtf -Destination $debugDir -Force

# Copy latest dictionaries into cst/src/Cst4/bin/Debug/Reference
Copy-Item .\Cst4\Reference\* -Recurse -Destination $debugDir\Reference -Force

#   Copy 217 XML files from "C:\github\tipitaka.org\deva master" to cst/src/Cst4/bin/Debug\Xml
Copy-Item $devaMasterDir\* -Destination $debugDir\Xml -Force

# Copy usp10.dll from cst/src/Cst4 to cst/src/Cst4/bin/Debug
Copy-Item -Path .\Cst4\usp10.dll -Destination $debugDir -Force

# cd to cst/src/Cst4/bin/Debug
cd $debugDir

candle CST4.wxs

light -ext WixUIExtension -dWixUILicenseRtf="CST4-license.rtf" CST4.wixobj

cd ..\..\..
