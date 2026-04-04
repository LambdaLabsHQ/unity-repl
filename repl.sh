#!/bin/bash
# Unity REPL Bash Client
# Pure IPC client. Zero external dependencies (No Bun, No .NET, No Node).

PROJECT_ROOT=$(pwd)
IPC_DIR="$PROJECT_ROOT/Temp/UnityReplIpc"
REQ_DIR="$IPC_DIR/Requests"
RES_DIR="$IPC_DIR/Responses"

mkdir -p "$REQ_DIR"
mkdir -p "$RES_DIR"

TIMEOUT_MS=60000

echo "UnityREPL ready. Type C# expressions:"

while true; do
    printf "> "
    read -r code
    
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
            echo "ERROR: timeout (60s) — is Unity Editor running?"
            break
        fi
    done

    # Output result if it exists
    if [ -f "$RES_FILE" ]; then
        cat "$RES_FILE"
        rm -f "$RES_FILE"
        echo ""
    fi
done
