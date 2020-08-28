@ECHO off
SETLOCAL

REM Parse command line arg if present
SET ARG1=%~1
SET ARG2=%~2
SET ARG3=%~3

REM If command line arg present, set the BUILD_CONFIG
REM otherwise, prompt the user
IF NOT "%ARG1%" == "" SET BUILD_CONFIG=%ARG1:~-1%
IF "%ARG1%" == "" SET /P BUILD_CONFIG=Please select the build configuration to use (r = Release, d = Debug [Default]):

IF NOT "%ARG2%" == "" SET BUILD_TARGET=%ARG2:~-1%
IF "%ARG2%" == "" SET /P BUILD_TARGET=Please select the build target to use (b = prepare only [Default], u = prepare and package umbraco, n = prepare and package nuget, a = prepare and package all):
IF "%BUILD_TARGET%" == "" SET BUILD_TARGET=b

IF NOT "%ARG3%" == "" SET BUILD_PATH=%ARG3:~-1%
IF "%ARG3%" == "" SET /P BUILD_PATH=Please select the Visual Studio version on your machine (17 = Visual Studio 2017 [Default], 19 = Visual Studio 2019, else provide the full path to MsBuild.exe):
IF "%BUILD_PATH%" == "" SET BUILD_PATH=17

REM Covert build config flag to an actual config string
if "%BUILD_CONFIG%" == "r" (
  SET BUILD_CONFIG=Release
) else (
  SET BUILD_CONFIG=Debug
)

REM Covert build target flag to an actual config string
if "%BUILD_TARGET%" == "a" (
  SET BUILD_TARGET=PrepareAndPackageAll
) else (
  if "%BUILD_TARGET%" == "n" (
    SET BUILD_TARGET=PrepareAndPackageNuget
  ) else (
    if "%BUILD_TARGET%" == "u" (
      SET BUILD_TARGET=PrepareAndPackageUmbraco
    ) else (
      SET BUILD_TARGET=PrepareOnly
    )
  )
)

REM Covert VS version flag to an actual build path
if "%BUILD_PATH%" == "17" (
  SET BUILD_PATH="%programfiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\amd64\MsBuild.exe"
) else (
  if "%BUILD_PATH%" == "19" (
    SET BUILD_PATH="%programfiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MsBuild.exe"
  )
)




REM Trigger the build
CALL %BUILD_PATH% build\Build.proj  -target:%BUILD_TARGET%

ENDLOCAL

IF %ERRORLEVEL% NEQ 0 GOTO err
EXIT /B 0
:err
PAUSE
EXIT /B 1

