@echo off
setlocal enabledelayedexpansion

:: Unity REPL Batch Client
:: Pure IPC client. Zero external dependencies.
::
:: Interactive:   repl.bat                (no args)
:: One-shot:      repl.bat -e "CODE" | -p "CODE" | -f PATH | -
:: Help:          repl.bat -h
::
:: Rule: any argument puts repl.bat in non-interactive mode.
:: (cmd.exe TTY detection is unreliable, so we use an explicit flag check.)

set "IPC_DIR=%CD%\Temp\UnityReplIpc"
set "REQ_DIR=%IPC_DIR%\Requests"
set "RES_DIR=%IPC_DIR%\Responses"

if not exist "%REQ_DIR%" mkdir "%REQ_DIR%"
if not exist "%RES_DIR%" mkdir "%RES_DIR%"

set "TIMEOUT_MS=60000"
if defined REPL_TIMEOUT set /a TIMEOUT_MS=REPL_TIMEOUT*1000

:: No args -> interactive mode
if "%~1"=="" goto interactive

:: ---- Parse arguments ----
set "SRC_KIND="
set "SRC_VAL="
set "CODE_FILE="
set "VALIDATE=0"

:parse_loop
if "%~1"=="" goto dispatch
if /i "%~1"=="-h" goto help_exit0
if /i "%~1"=="--help" goto help_exit0
if /i "%~1"=="-e" goto flag_eval
if /i "%~1"=="--eval" goto flag_eval
if /i "%~1"=="-p" goto flag_eval
if /i "%~1"=="--print" goto flag_eval
if /i "%~1"=="-f" goto flag_file
if /i "%~1"=="--file" goto flag_file
if "%~1"=="-" (
    set "SRC_KIND=stdin"
    shift
    goto parse_loop
)
if /i "%~1"=="-V" goto flag_validate
if /i "%~1"=="--validate" goto flag_validate
if /i "%~1"=="--timeout" goto flag_timeout
echo ERROR: unknown argument: %~1 1>&2
call :print_usage 1>&2
exit /b 3

:flag_validate
set "VALIDATE=1"
shift
goto parse_loop

:flag_eval
if "%~2"=="" (
    echo ERROR: %~1 requires an argument 1>&2
    call :print_usage 1>&2
    exit /b 3
)
set "SRC_KIND=eval"
set "SRC_VAL=%~2"
shift
shift
goto parse_loop

:flag_file
if "%~2"=="" (
    echo ERROR: %~1 requires a PATH argument 1>&2
    call :print_usage 1>&2
    exit /b 3
)
set "SRC_KIND=file"
set "CODE_FILE=%~2"
shift
shift
goto parse_loop

:flag_timeout
if "%~2"=="" (
    echo ERROR: --timeout requires seconds 1>&2
    exit /b 3
)
set /a TIMEOUT_MS=%~2*1000
shift
shift
goto parse_loop

:dispatch
if not defined SRC_KIND (
    echo ERROR: no code source specified 1>&2
    call :print_usage 1>&2
    exit /b 3
)

:: Generate UUID
for /f "tokens=*" %%a in ('powershell -NoProfile -Command "[guid]::NewGuid().ToString()"') do set UUID=%%a
set "REQ_TMP=%REQ_DIR%\%UUID%.tmp"
set "REQ_FILE=%REQ_DIR%\%UUID%.req"
set "RES_FILE=%RES_DIR%\%UUID%.res"

:: Write request based on source kind
if "%SRC_KIND%"=="eval" (
    set "CODE=!SRC_VAL!"
    powershell -NoProfile -Command "[IO.File]::WriteAllText('%REQ_TMP%', $env:CODE)"
) else if "%SRC_KIND%"=="file" (
    if not exist "!CODE_FILE!" (
        echo ERROR: cannot read file: !CODE_FILE! 1>&2
        exit /b 3
    )
    copy /Y "!CODE_FILE!" "%REQ_TMP%" >nul
    if errorlevel 1 (
        echo ERROR: cannot read file: !CODE_FILE! 1>&2
        exit /b 3
    )
) else if "%SRC_KIND%"=="stdin" (
    powershell -NoProfile -Command "[IO.File]::WriteAllText('%REQ_TMP%', [Console]::In.ReadToEnd())"
)

:: Prepend //!validate directive when -V/--validate was requested. The
:: transport's directive parser strips it and routes to Validate().
if "%VALIDATE%"=="1" (
    powershell -NoProfile -Command "$b = [IO.File]::ReadAllText('%REQ_TMP%'); [IO.File]::WriteAllText('%REQ_TMP%', \"//!validate`n$b\")"
)

move /Y "%REQ_TMP%" "%REQ_FILE%" >nul

:: Wait for response
set /a waited=0
:wait_loop
if exist "%RES_FILE%" goto classify
powershell -NoProfile -Command "Start-Sleep -Milliseconds 50"
set /a waited+=50
if !waited! gtr !TIMEOUT_MS! (
    set /a timeout_s=!TIMEOUT_MS!/1000
    echo ERROR: timeout ^(!timeout_s!s^) -- is Unity Editor running? 1>&2
    del /f /q "%REQ_FILE%" 2>nul
    exit /b 4
)
goto wait_loop

:classify
set "FIRSTLINE="
set /p FIRSTLINE=<"%RES_FILE%"
echo !FIRSTLINE! | findstr /B /C:"COMPILE ERROR:" >nul && ( type "%RES_FILE%" 1>&2 & del /f /q "%RES_FILE%" & exit /b 2 )
echo !FIRSTLINE! | findstr /B /C:"INCOMPLETE:" >nul && ( type "%RES_FILE%" 1>&2 & del /f /q "%RES_FILE%" & exit /b 2 )
echo !FIRSTLINE! | findstr /B /C:"RUNTIME ERROR:" >nul && ( type "%RES_FILE%" 1>&2 & del /f /q "%RES_FILE%" & exit /b 1 )
echo !FIRSTLINE! | findstr /B /C:"ERROR:" >nul && ( type "%RES_FILE%" 1>&2 & del /f /q "%RES_FILE%" & exit /b 1 )
type "%RES_FILE%"
del /f /q "%RES_FILE%"
exit /b 0

:help_exit0
call :print_usage
exit /b 0

:print_usage
echo Usage: repl.bat [options]
echo   (no args^)            Interactive REPL
echo   -e, --eval CODE      Evaluate CODE and exit
echo   -p, --print CODE     Same as --eval (Node-style alias^)
echo   -f, --file PATH      Evaluate file contents and exit
echo   -                    Read code from stdin explicitly
echo   -V, --validate       Compile-only dry run (no execution^)
echo   --timeout SECONDS    Override timeout (default: 60, env: REPL_TIMEOUT^)
echo   -h, --help           Show this help
echo.
echo Any argument puts repl.bat in non-interactive mode.
echo Exit codes: 0 ok, 1 runtime error, 2 compile error, 3 usage/IO, 4 timeout.
goto :eof

:: ---- Interactive mode (unchanged from original) ----
:interactive
echo UnityREPL ready. Type C# expressions:

:loop
set /p code="> "

if "!code!"=="" goto loop
if /i "!code!"=="exit" goto :EOF
if /i "!code!"=="quit" goto :EOF

for /f "tokens=*" %%a in ('powershell -NoProfile -Command "[guid]::NewGuid().ToString()"') do set UUID=%%a

set "REQ_TMP=%REQ_DIR%\%UUID%.tmp"
set "REQ_FILE=%REQ_DIR%\%UUID%.req"
set "RES_FILE=%RES_DIR%\%UUID%.res"

echo !code!> "%REQ_TMP%"
move /Y "%REQ_TMP%" "%REQ_FILE%" >nul

set /a waited=0

:iwait_loop
if exist "%RES_FILE%" goto iread_res
powershell -nop -c "Start-Sleep -Milliseconds 50"
set /a waited+=50
if !waited! gtr !TIMEOUT_MS! (
    echo ERROR: timeout ^(%TIMEOUT_S%s^) -- is Unity Editor running?
    goto loop
)
goto iwait_loop

:iread_res
type "%RES_FILE%"
del /f /q "%RES_FILE%"
echo.
goto loop
