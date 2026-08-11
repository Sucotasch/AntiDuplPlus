@echo off
setlocal enabledelayedexpansion

REM =====================================================================
REM Deploy.cmd - build + deploy everything into bin\Release (single run).
REM Usage:  cmd\Deploy.cmd
REM Result: test AntiDupl.NET.WinForms.exe from bin\Release\
REM =====================================================================

set ROOT_DIR=%~dp0..
set BIN_DIR=%ROOT_DIR%\bin\Release
set SRC_DIR=%ROOT_DIR%\src
set CUDA_BIN=%CUDA_PATH%\bin\x64

if not exist "%BIN_DIR%" mkdir "%BIN_DIR%"

REM --- locate MSBuild via vswhere ---
set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
if not exist "%VSWHERE%" (
    echo [ERROR] vswhere.exe not found. Install Visual Studio Build Tools.
    exit /b 1
)
for /f "usebackq delims=" %%m in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%m
if not exist "%MSBUILD%" (
    echo [ERROR] MSBuild.exe not found.
    exit /b 1
)
echo [1/5] MSBuild: %MSBUILD%

REM --- step 1: build AntiDupl.dll (C++) ---
echo [2/5] Building AntiDupl.dll ...
"%MSBUILD%" "%SRC_DIR%\AntiDupl\AntiDupl.vcxproj" /p:Configuration=Release /p:Platform=x64 /m:1 /v:minimal /nologo /p:VcpkgManifestInstall=false
if errorlevel 1 (
    echo [ERROR] AntiDupl build failed.
    exit /b 1
)

REM --- step 2: build NvJpegCollector.exe (C++) ---
echo [3/5] Building NvJpegCollector.exe ...
"%MSBUILD%" "%SRC_DIR%\NvJpegCollector\NvJpegCollector.vcxproj" /p:Configuration=Release /p:Platform=x64 /m:1 /v:minimal /nologo
if errorlevel 1 (
    echo [ERROR] NvJpegCollector build failed.
    exit /b 1
)

REM --- step 3: build WinForms GUI (C#, also builds AntiDupl.NET.Core) ---
echo [4/5] Building AntiDupl.NET.WinForms ...
dotnet build "%SRC_DIR%\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj" -c Release /p:Platform=x64 /p:SolutionDir="%SRC_DIR%\"
if errorlevel 1 (
    echo [ERROR] WinForms build failed.
    exit /b 1
)

REM --- step 4: copy CUDA runtime deps next to exe ---
echo [5/5] Copying CUDA runtime deps ...
if not exist "%CUDA_BIN%\nvjpeg64_13.dll" (
    echo [WARN] nvjpeg64_13.dll not found in %CUDA_BIN%, keeping existing copy if present.
) else (
    copy /y "%CUDA_BIN%\nvjpeg64_13.dll" "%BIN_DIR%\nvjpeg64_13.dll" >nul
)
if exist "%CUDA_BIN%\cudart64_13.dll" copy /y "%CUDA_BIN%\cudart64_13.dll" "%BIN_DIR%\cudart64_13.dll" >nul
if not exist "%BIN_DIR%\cudart64_12.dll" copy /y "%CUDA_BIN%\..\..\v12.8\bin\cudart64_12.dll" "%BIN_DIR%\cudart64_12.dll" >nul 2>nul

REM --- resources (strings, images) ---
call "%ROOT_DIR%\cmd\CopyData.cmd" "%ROOT_DIR%" "%BIN_DIR%"
if errorlevel 1 (
    echo [ERROR] CopyData.cmd failed.
    exit /b 1
)

REM =====================================================================
REM verify
REM =====================================================================
echo.
echo === Verification ===
set FAIL=0
set EXPECTED=AntiDupl.dll NvJpegCollector.exe AntiDupl.NET.WinForms.exe AntiDupl.NET.Core.dll nvjpeg64_13.dll cudart64_12.dll
for %%f in (%EXPECTED%) do (
    if not exist "%BIN_DIR%\%%f" (
        echo   [MISSING] %%f
        set FAIL=1
    ) else (
        echo   [OK] %%f
    )
)
if not exist "%BIN_DIR%\data\resources" (
    echo   [MISSING] data\resources
    set FAIL=1
) else (
    echo   [OK] data\resources
)

echo.
if "%FAIL%"=="1" (
    echo [FAIL] Deploy incomplete - fix missing files above.
    exit /b 1
)
echo [OK] Deploy complete. Test GUI: %BIN_DIR%\AntiDupl.NET.WinForms.exe
exit /b 0
