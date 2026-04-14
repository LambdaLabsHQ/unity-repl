#!/usr/bin/env bash
# Mock Unity REPL server for testing repl.sh / repl.bat.
# Polls Requests/ and writes canned responses based on magic strings in the code.
#
# Usage: mock_unity.sh [IPC_DIR]
#   Default IPC_DIR = $PWD/Temp/UnityReplIpc
#
# Magic strings in request body:
#   __ok__VALUE      -> write VALUE to .res (success)
#   __echo__         -> echo the full request body back verbatim (for newline-preservation tests)
#   __compile__      -> COMPILE ERROR response
#   __incomplete__   -> INCOMPLETE response
#   __runtime__      -> RUNTIME ERROR response
#   __generic__      -> ERROR response
#   __sleep__        -> sleep 10s before responding (forces client timeout)
#   __validate_ok__  -> COMPILE OK (for --validate tests; requires //!validate directive)
#   __validate_bad__ -> COMPILE ERROR (for --validate tests; requires //!validate directive)
#   anything else    -> echo the code as-is
#
# Leading //!timeout=... and //!validate directive lines are stripped before
# pattern matching (they are consumed by the real transport the same way).
# __validate_ok__ / __validate_bad__ only match when //!validate was present.

set -u

IPC_DIR="${1:-$PWD/Temp/UnityReplIpc}"
REQ_DIR="$IPC_DIR/Requests"
RES_DIR="$IPC_DIR/Responses"

mkdir -p "$REQ_DIR" "$RES_DIR"

cleanup() { exit 0; }
trap cleanup INT TERM

write_res_atomic() {
    local uuid="$1" body="$2"
    local tmp="$RES_DIR/$uuid.res.tmp"
    printf '%s' "$body" > "$tmp"
    mv "$tmp" "$RES_DIR/$uuid.res"
}

process_request() {
    local req_file="$1"
    local uuid
    uuid=$(basename "$req_file" .req)
    local code
    # Preserve trailing newlines with x-sentinel
    code=$(cat -- "$req_file"; printf x)
    code="${code%x}"
    rm -f "$req_file"

    # Strip leading //!-prefixed directive lines (any order, stackable).
    # Tracks whether //!validate was present to gate the validate-only magic strings.
    local validate_only=0
    while :; do
        case "$code" in
            "//!validate"$'\n'*)
                validate_only=1
                code="${code#*$'\n'}" ;;
            "//!timeout="*$'\n'*)
                code="${code#*$'\n'}" ;;
            *)
                break ;;
        esac
    done

    # Strip trailing newline for pattern matching only (keep original for __echo__)
    local match="${code%$'\n'}"

    if [ "$validate_only" = 1 ]; then
        case "$match" in
            __validate_ok__)
                write_res_atomic "$uuid" "COMPILE OK" ;;
            __validate_bad__)
                write_res_atomic "$uuid" "COMPILE ERROR:
(1,5): error CS0103: The name 'foo' does not exist" ;;
            *)
                write_res_atomic "$uuid" "COMPILE OK" ;;
        esac
        return
    fi

    case "$match" in
        __ok__*)
            write_res_atomic "$uuid" "${match#__ok__}" ;;
        __echo__*)
            # Echo request body verbatim (preserve trailing newlines)
            write_res_atomic "$uuid" "$code" ;;
        __compile__)
            write_res_atomic "$uuid" "COMPILE ERROR:
(1,5): error CS0103: The name 'foo' does not exist" ;;
        __incomplete__)
            write_res_atomic "$uuid" "INCOMPLETE: {" ;;
        __runtime__)
            write_res_atomic "$uuid" "RUNTIME ERROR: boom
  at Script.Eval() line 1" ;;
        __generic__)
            write_res_atomic "$uuid" "ERROR: something went sideways" ;;
        __sleep__)
            sleep 10
            write_res_atomic "$uuid" "late" ;;
        *)
            # Default: echo the code back
            write_res_atomic "$uuid" "$code" ;;
    esac
}

while true; do
    # shellcheck disable=SC2231
    for req in $REQ_DIR/*.req; do
        [ -e "$req" ] || continue
        process_request "$req"
    done
    sleep 0.02
done
