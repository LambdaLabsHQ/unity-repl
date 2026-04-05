@echo off
setlocal enabledelayedexpansion

:: Unity REPL Batch Client
:: Pure IPC client. Zero external dependencies.
::
:: Env vars:
::   TIMEOUT_S  -- how long to wait for a .res file before giving up (default 60)
::
:: To cancel a running coroutine, create an empty file:
::   Temp\UnityReplIpc\Requests\<uuid>.cancel
:: Ctrl-C terminates the client (no automatic cancel file on Windows).

set "IPC_DIR=%CD%\Temp\UnityReplIpc"
set "REQ_DIR=%IPC_DIR%\Requests"
set "RES_DIR=%IPC_DIR%\Responses"

if not exist "%REQ_DIR%" mkdir "%REQ_DIR%"
if not exist "%RES_DIR%" mkdir "%RES_DIR%"

if "%TIMEOUT_S%"=="" set "TIMEOUT_S=60"
set /a TIMEOUT_MS=%TIMEOUT_S% * 1000

echo UnityREPL ready. Type C# expressions:

:loop
set /p code="> "

if "!code!"=="" goto loop
if /i "!code!"=="exit" goto :EOF
if /i "!code!"=="quit" goto :EOF

:: Generate pseudo-UUID
for /f "tokens=*" %%a in ('powershell -NoProfile -Command "[guid]::NewGuid().ToString()"') do set UUID=%%a

set "REQ_TMP=%REQ_DIR%\%UUID%.tmp"
set "REQ_FILE=%REQ_DIR%\%UUID%.req"
set "RES_FILE=%RES_DIR%\%UUID%.res"

echo !code!> "%REQ_TMP%"
move /Y "%REQ_TMP%" "%REQ_FILE%" >nul

set /a waited=0

:wait_loop
if exist "%RES_FILE%" goto read_res

:: Wait 50ms using powershell
powershell -nop -c "Start-Sleep -Milliseconds 50"
set /a waited+=50

if !waited! gtr !TIMEOUT_MS! (
    echo ERROR: timeout ^(%TIMEOUT_S%s^) -- is Unity Editor running?
    goto loop
)
goto wait_loop

:read_res
type "%RES_FILE%"
del /f /q "%RES_FILE%"
echo.
goto loop
