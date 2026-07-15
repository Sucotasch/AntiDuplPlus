@echo off

set RAR_EXE="C:\Program Files\WinRAR\WinRar.exe"

set ROOT_DIR=..
set RELEASE_DIR=%ROOT_DIR%\bin\Release
set VERSION_FILE=%ROOT_DIR%\src\version.txt

if not exist %RELEASE_DIR% (
echo Can't find "%RELEASE_DIR%" directory!
exit 0
)

if not exist %VERSION_FILE% (
echo Can't find "%VERSION_FILE%" file!
exit 0
)

set VERSION=
for /f "delims=" %%i in ('type %VERSION_FILE%') do set VERSION=%%i

set OUT_DIR=%ROOT_DIR%\out\bin
set TMP_DIR=%ROOT_DIR%\out\bin\AntiDupl.NET-%VERSION%

if not exist %OUT_DIR% mkdir %OUT_DIR%

if exist %TMP_DIR% (
echo Delete old files:
erase %TMP_DIR%\* /q /s /f
rmdir %TMP_DIR% /q /s
)

if not exist %TMP_DIR% mkdir %TMP_DIR%

REM === Core application files ===
xcopy %RELEASE_DIR%\AntiDupl.NET.WinForms.exe %TMP_DIR%\* /y /i
xcopy %RELEASE_DIR%\AntiDupl.NET.WinForms.dll %TMP_DIR%\* /y /i
xcopy %RELEASE_DIR%\AntiDupl.NET.WinForms.runtimeconfig.json %TMP_DIR%\* /y /i
xcopy %RELEASE_DIR%\AntiDupl.NET.Core.dll %TMP_DIR%\* /y /i
xcopy %RELEASE_DIR%\AntiDupl.dll %TMP_DIR%\* /y /i

REM === CUDA / nvJPEG runtime ===
xcopy %RELEASE_DIR%\nvjpeg64_12.dll %TMP_DIR%\* /y /i
if exist %RELEASE_DIR%\cudart64_12.dll xcopy %RELEASE_DIR%\cudart64_12.dll %TMP_DIR%\* /y /i

REM === GPU collector utility ===
xcopy %RELEASE_DIR%\NvJpegCollector.exe %TMP_DIR%\* /y /i

REM === .NET runtime dependencies ===
xcopy %RELEASE_DIR%\System.*.dll %TMP_DIR%\* /y /i
xcopy %RELEASE_DIR%\Microsoft.*.dll %TMP_DIR%\* /y /i

REM === Data folder (resources, strings, images) ===
xcopy %RELEASE_DIR%\data\* %TMP_DIR%\data\* /y /i /s

REM === Config files ===
if exist %RELEASE_DIR%\ad_database.xml xcopy %RELEASE_DIR%\ad_database.xml %TMP_DIR%\* /y /i
if exist %RELEASE_DIR%\configuration.xml xcopy %RELEASE_DIR%\configuration.xml %TMP_DIR%\* /y /i
if exist %RELEASE_DIR%\locations.xml xcopy %RELEASE_DIR%\locations.xml %TMP_DIR%\* /y /i

REM === Remove unnecessary files ===
erase %TMP_DIR%\data\resources\strings\English.xml /q /s /f 2>nul
erase %TMP_DIR%\data\resources\strings\Russian.xml /q /s /f 2>nul
erase %TMP_DIR%\AntiDupl.NET.WPF.* /q /s /f 2>nul
erase %TMP_DIR%\*.pdb /q /s /f 2>nul

echo.
echo Release directory: %TMP_DIR%
echo.

if exist %RAR_EXE% (
%RAR_EXE% a -ep1 -s -m5 -r -sfx %OUT_DIR%\AntiDupl.NET-%VERSION%.exe %TMP_DIR%
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%.exe SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%.exe.hash.txt
%RAR_EXE% a -afzip -ep1 -r %OUT_DIR%\AntiDupl.NET-%VERSION%.zip %TMP_DIR%
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%.zip SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%.zip.hash.txt
) else (
.\7-zip\7za_2201.exe a -sfx7z.sfx %OUT_DIR%\AntiDupl.NET-%VERSION%.exe %TMP_DIR%
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%.exe SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%.exe.hash.txt
.\7-zip\7za_2201.exe a -tzip %OUT_DIR%\AntiDupl.NET-%VERSION%.zip .\%TMP_DIR%\*
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%.zip SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%.zip.hash.txt
)
