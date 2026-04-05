#!/bin/bash
# Unity REPL Bash Client
# Pure IPC client. Zero external dependencies (No Bun, No .NET, No Node).
#
# Env vars:
#   TIMEOUT_S  — how long to wait for a .res file before giving up (default 60)
#
# Ctrl-C: first press writes {uuid}.cancel so the server aborts the coroutine
#         and writes CANCELLED to .res; client keeps waiting so you see the
#         response. Second Ctrl-C hard-exits the client.

PROJECT_ROOT=$(pwd)
IPC_DIR="$PROJECT_ROOT/Temp/UnityReplIpc"
REQ_DIR="$IPC_DIR/Requests"
RES_DIR="$IPC_DIR/Responses"

mkdir -p "$REQ_DIR"
mkdir -p "$RES_DIR"

TIMEOUT_S=${TIMEOUT_S:-60}
TIMEOUT_MS=$((TIMEOUT_S * 1000))

echo "UnityREPL ready. Type C# expressions:"

# Exported so the trap handler (runs in a subshell context) can touch it.
CURRENT_UUID=""
CANCEL_SENT=0

on_int() {
    if [ -n "$CURRENT_UUID" ] && [ $CANCEL_SENT -eq 0 ]; then
        touch "$REQ_DIR/$CURRENT_UUID.cancel" 2>/dev/null
        CANCEL_SENT=1
        echo ""
        echo "(cancelling — waiting for CANCELLED response, Ctrl-C again to force-exit)"
    else
        exit 130
    fi
}
trap on_int INT

while true; do
    printf "> "
    if ! read -r code; then
        # EOF (Ctrl-D)
        echo ""
        exit 0
    fi

    # Trim whitespace
    code=$(echo "$code" | xargs)
    if [ -z "$code" ]; then
        continue
    fi
    if [ "$code" = "exit" ] || [ "$code" = "quit" ]; then
        exit 0
    fi

    # Generate a lightweight unique ID
    UUID=$(uuidgen)
    CURRENT_UUID="$UUID"
    CANCEL_SENT=0
    REQ_TMP="$REQ_DIR/$UUID.tmp"
    REQ_FILE="$REQ_DIR/$UUID.req"
    RES_FILE="$RES_DIR/$UUID.res"

    echo "$code" > "$REQ_TMP"
    mv "$REQ_TMP" "$REQ_FILE"

    waited=0
    # Wait for the response file to appear (polling every ~0.05 seconds)
    while [ ! -f "$RES_FILE" ]; do
        sleep 0.05
        waited=$((waited + 50))
        if [ $waited -gt $TIMEOUT_MS ]; then
            echo "ERROR: timeout (${TIMEOUT_S}s) — is Unity Editor running?"
            break
        fi
    done

    # Output result if it exists
    if [ -f "$RES_FILE" ]; then
        cat "$RES_FILE"
        rm -f "$RES_FILE"
        echo ""
    fi

    CURRENT_UUID=""
    CANCEL_SENT=0
done
