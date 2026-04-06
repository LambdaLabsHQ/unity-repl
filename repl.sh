#!/usr/bin/env bash
# Unity REPL Bash Client
# Pure IPC client. Zero external dependencies (No Bun, No .NET, No Node).
#
# Interactive:   ./repl.sh
# One-shot:      ./repl.sh -e 'CODE' | -p 'CODE' | -f PATH | -
# Piped stdin:   echo 'CODE' | ./repl.sh     (auto-detected via [ ! -t 0 ])
# Help:          ./repl.sh -h
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

print_usage() {
    cat <<'EOF'
Usage: repl.sh [options] [-]
  (no args, tty)       Interactive REPL
  -e, --eval CODE      Evaluate CODE and exit
  -p, --print CODE     Same as --eval (Node-style alias)
  -f, --file PATH      Evaluate file contents and exit
  -                    Read code from stdin explicitly
  --timeout SECONDS    Override one-shot timeout (default: 60, env: REPL_TIMEOUT)
  -h, --help           Show this help

When stdin is piped/redirected and no flag is given, reads stdin and evaluates
once (non-interactive). When stdin is a terminal and no flag is given, starts
the interactive REPL.

Exit codes:
  0  success
  1  runtime error
  2  compile error (or incomplete expression)
  3  usage error / file I/O error
  4  timeout waiting for Unity
EOF
}

# Generate UUID (uuidgen with Linux /proc fallback).
gen_uuid() {
    if command -v uuidgen >/dev/null 2>&1; then
        uuidgen
    elif [ -r /proc/sys/kernel/random/uuid ]; then
        cat /proc/sys/kernel/random/uuid
    else
        echo "ERROR: no uuidgen or /proc/sys/kernel/random/uuid available" >&2
        return 1
    fi
}

# Ctrl-C cancel state (shared by send_and_wait + on_int trap).
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

# send_and_wait CODE TIMEOUT_MS TRAILING_NEWLINE
# Writes request via printf (NOT echo — echo mangles \n/\t/-e on some shells).
# Sets globals: CUR_UUID, CUR_REQ_TMP, CUR_REQ_FILE, CUR_RES_FILE.
# On success: returns 0 (response file is at $CUR_RES_FILE).
# On timeout: prints to stderr, returns 4. Callers decide whether to exit.
send_and_wait() {
    local code="$1" timeout_ms="$2" trailing_nl="$3"
    CUR_UUID=$(gen_uuid) || return 3
    CUR_REQ_TMP="$REQ_DIR/$CUR_UUID.tmp"
    CUR_REQ_FILE="$REQ_DIR/$CUR_UUID.req"
    CUR_RES_FILE="$RES_DIR/$CUR_UUID.res"
    CURRENT_UUID="$CUR_UUID"
    CANCEL_SENT=0

    if [ "$trailing_nl" = 1 ]; then
        printf '%s\n' "$code" > "$CUR_REQ_TMP"
    else
        printf '%s' "$code" > "$CUR_REQ_TMP"
    fi
    mv "$CUR_REQ_TMP" "$CUR_REQ_FILE"

    local waited=0
    while [ ! -f "$CUR_RES_FILE" ]; do
        sleep 0.05
        waited=$((waited + 50))
        if [ "$waited" -gt "$timeout_ms" ]; then
            local timeout_s=$(( timeout_ms / 1000 ))
            echo "ERROR: timeout (${timeout_s}s) — is Unity Editor running?" >&2
            rm -f "$CUR_REQ_FILE" 2>/dev/null
            CURRENT_UUID=""
            return 4
        fi
    done
    CURRENT_UUID=""
    return 0
}

# classify_and_exit RES_FILE
# Inspects first line, routes output to stdout/stderr, deletes file, exits with classified code.
classify_and_exit() {
    local res="$1" first ec stream
    first=$(head -n1 "$res" 2>/dev/null)
    case "$first" in
        "COMPILE ERROR:"*)  ec=2; stream=stderr ;;
        "INCOMPLETE:"*)     ec=2; stream=stderr ;;
        "RUNTIME ERROR:"*)  ec=1; stream=stderr ;;
        "ERROR:"*)          ec=1; stream=stderr ;;
        *)                  ec=0; stream=stdout ;;
    esac
    if [ "$stream" = stderr ]; then
        cat "$res" >&2
    else
        cat "$res"
    fi
    rm -f "$res"
    exit "$ec"
}

# Read stdin verbatim (preserves trailing newlines via x-sentinel trick).
read_all_stdin() {
    local tmp
    tmp=$(cat; printf x)
    printf '%s' "${tmp%x}"
}

# Read file verbatim (preserves trailing newlines via x-sentinel trick).
read_file_preserving_newlines() {
    local tmp
    tmp=$(cat -- "$1"; printf x) || return 3
    printf '%s' "${tmp%x}"
}

# ---- Timeout default from env or 60s ----
if [ -n "${REPL_TIMEOUT:-}" ]; then
    case "$REPL_TIMEOUT" in
        ''|*[!0-9]*)
            echo "ERROR: REPL_TIMEOUT must be a positive integer (got: $REPL_TIMEOUT)" >&2
            exit 3 ;;
    esac
    TIMEOUT_MS=$(( REPL_TIMEOUT * 1000 ))
else
    TIMEOUT_MS=60000
fi

# ---- Argument parsing ----
SRC_KIND=tty
SRC_VAL=
ANY_FLAG=0

while [ $# -gt 0 ]; do
    case "$1" in
        -e|--eval|-p|--print)
            ANY_FLAG=1; SRC_KIND=eval
            if [ $# -lt 2 ]; then
                echo "ERROR: $1 requires an argument" >&2
                print_usage >&2
                exit 3
            fi
            SRC_VAL="$2"; shift 2 ;;
        -f|--file)
            ANY_FLAG=1; SRC_KIND=file
            if [ $# -lt 2 ]; then
                echo "ERROR: $1 requires an argument" >&2
                print_usage >&2
                exit 3
            fi
            SRC_VAL="$2"; shift 2 ;;
        -)
            ANY_FLAG=1; SRC_KIND=stdin; shift ;;
        --timeout)
            if [ $# -lt 2 ]; then
                echo "ERROR: --timeout requires seconds" >&2
                exit 3
            fi
            case "$2" in
                ''|*[!0-9]*)
                    echo "ERROR: --timeout must be a positive integer (got: $2)" >&2
                    exit 3 ;;
            esac
            TIMEOUT_MS=$(( $2 * 1000 ))
            shift 2 ;;
        -h|--help)
            print_usage; exit 0 ;;
        *)
            echo "ERROR: unknown argument: $1" >&2
            print_usage >&2
            exit 3 ;;
    esac
done

# ---- Dispatch ----
run_oneshot() {
    local code
    case "$SRC_KIND" in
        eval)
            code="$SRC_VAL" ;;
        file)
            if [ ! -r "$SRC_VAL" ]; then
                echo "ERROR: cannot read file: $SRC_VAL" >&2
                exit 3
            fi
            # x-sentinel at caller level — command substitution strips trailing newlines
            code=$(read_file_preserving_newlines "$SRC_VAL"; printf x) || exit 3
            code="${code%x}" ;;
        stdin)
            code=$(read_all_stdin; printf x)
            code="${code%x}" ;;
    esac

    # Scoped cleanup for this UUID (concurrent runs use different UUIDs, safe).
    # INT stays bound to on_int (set globally above) so Ctrl-C sends .cancel.
    trap 'rm -f "${CUR_REQ_TMP:-}" "${CUR_REQ_FILE:-}" "${CUR_RES_FILE:-}" "$REQ_DIR/${CUR_UUID:-__none__}.cancel" 2>/dev/null' EXIT TERM

    send_and_wait "$code" "$TIMEOUT_MS" 0
    rc=$?
    if [ $rc -ne 0 ]; then
        exit $rc
    fi
    classify_and_exit "$CUR_RES_FILE"
}

run_interactive() {
    echo "UnityREPL ready. Type C# expressions:"
    while true; do
        printf "> "
        read -r code || exit 0   # clean exit on Ctrl-D

        # Trim whitespace
        code=$(echo "$code" | xargs)
        if [ -z "$code" ]; then
            continue
        fi
        if [ "$code" = "exit" ] || [ "$code" = "quit" ]; then
            exit 0
        fi

        # Interactive preserves current wire format: adds trailing \n (TRAILING_NEWLINE=1).
        send_and_wait "$code" "$TIMEOUT_MS" 1
        if [ -f "${CUR_RES_FILE:-}" ]; then
            cat "$CUR_RES_FILE"
            rm -f "$CUR_RES_FILE"
            echo ""
        fi
        # Clean up any cancel marker left behind (server consumed it or never saw it).
        rm -f "$REQ_DIR/${CUR_UUID:-__none__}.cancel" 2>/dev/null
    done
}

if [ "$ANY_FLAG" = 1 ]; then
    # When both a flag and piped stdin are present, the flag wins and stdin is
    # silently ignored (matches `python -c 'x' < file.py` / `node -e 'x' < file.js`).
    run_oneshot
elif [ ! -t 0 ]; then
    SRC_KIND=stdin
    run_oneshot
else
    run_interactive
fi
