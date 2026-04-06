#!/usr/bin/env bash
# Integration tests for repl.sh against mock_unity.sh.
#
# Usage:
#   bash tests~/repl.test.sh
# Runs the mock daemon in the background, cd's into a temp project dir, executes
# 15 test cases, then kills the mock.

set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPL="$(cd "$SCRIPT_DIR/.." && pwd)/repl.sh"
MOCK="$SCRIPT_DIR/mock_unity.sh"
FIXTURE="$SCRIPT_DIR/fixtures/hello.cs"

# Sandbox project dir
WORK=$(mktemp -d -t repl-test.XXXXXX)
IPC="$WORK/Temp/UnityReplIpc"
mkdir -p "$IPC/Requests" "$IPC/Responses"

# Start mock daemon
bash "$MOCK" "$IPC" &
MOCK_PID=$!

cleanup() {
    kill "$MOCK_PID" 2>/dev/null
    wait "$MOCK_PID" 2>/dev/null
    rm -rf "$WORK"
}
trap cleanup EXIT

cd "$WORK"

PASS=0
FAIL=0

# run_case NAME EXPECTED_EXIT EXPECTED_STDOUT EXPECTED_STDERR_SUBSTR CMD...
# Use "SKIP_STDOUT" / "SKIP_STDERR" to skip those checks.
assert_case() {
    local name="$1" exp_ec="$2" exp_out="$3" exp_err_substr="$4"
    shift 4
    local out err ec
    out=$("$@" 2>/tmp/repl_test_err)
    ec=$?
    err=$(cat /tmp/repl_test_err)

    local ok=1
    if [ "$ec" != "$exp_ec" ]; then
        ok=0
        echo "  exit: expected=$exp_ec got=$ec"
    fi
    if [ "$exp_out" != "SKIP_STDOUT" ] && [ "$out" != "$exp_out" ]; then
        ok=0
        echo "  stdout: expected=<<$exp_out>> got=<<$out>>"
    fi
    if [ "$exp_err_substr" != "SKIP_STDERR" ]; then
        case "$err" in
            *"$exp_err_substr"*) ;;
            *) ok=0; echo "  stderr: expected substring=<<$exp_err_substr>> got=<<$err>>" ;;
        esac
    fi

    if [ $ok = 1 ]; then
        echo "PASS: $name"
        PASS=$((PASS+1))
    else
        echo "FAIL: $name"
        FAIL=$((FAIL+1))
    fi
}

echo "=== running tests in $WORK ==="

# 1. -e success → stdout, exit 0
assert_case "1. -e success" 0 "42" "" \
    bash "$REPL" -e "__ok__42"

# 2. -e compile error → stderr, exit 2
assert_case "2. -e compile error (exit 2, stderr)" 2 "" "COMPILE ERROR:" \
    bash "$REPL" -e "__compile__"

# 3. -e runtime error → stderr, exit 1
assert_case "3. -e runtime error (exit 1, stderr)" 1 "" "RUNTIME ERROR:" \
    bash "$REPL" -e "__runtime__"

# 4. -e incomplete → stderr, exit 2
assert_case "4. -e incomplete (exit 2)" 2 "" "INCOMPLETE:" \
    bash "$REPL" -e "__incomplete__"

# 5. -e generic ERROR → stderr, exit 1
assert_case "5. -e generic ERROR (exit 1)" 1 "" "ERROR: something" \
    bash "$REPL" -e "__generic__"

# 6. --timeout triggers exit 4 (mock __sleep__ sleeps 10s)
assert_case "6. --timeout 1 triggers exit 4" 4 "" "timeout" \
    bash "$REPL" --timeout 1 -e "__sleep__"

# 7. implicit stdin (piped)
assert_case "7. implicit piped stdin" 0 "hi" "" \
    bash -c "echo '__ok__hi' | bash '$REPL'"

# 8. explicit stdin with -
assert_case "8. explicit - stdin" 0 "dash" "" \
    bash -c "echo '__ok__dash' | bash '$REPL' -"

# 9. -f preserves trailing newline (fixture has __echo__\nline2\n)
# Expected stdout: "__echo__\nline2" (echo from mock adds no trailing \n via printf '%s', bash cmd-subst strips trailing \n)
assert_case "9. -f file echoes body" 0 "$(printf '__echo__\nline2')" "" \
    bash "$REPL" -f "$FIXTURE"

# 9b. -f trailing newline preserved on wire (verify via wc -c)
# The echo mock writes back the request body verbatim. Source file is 15 bytes
# ("__echo__\nline2\n" = 8+1+5+1 = 15). Command substitution strips trailing \n, so we
# check byte count via file output.
OUT=$(bash "$REPL" -f "$FIXTURE"; printf x)
OUT="${OUT%x}"
EXPECTED=$(cat "$FIXTURE"; printf x)
EXPECTED="${EXPECTED%x}"
if [ "$OUT" = "$EXPECTED" ]; then
    echo "PASS: 9b. -f byte-exact newline preservation"
    PASS=$((PASS+1))
else
    echo "FAIL: 9b. -f byte-exact newline preservation"
    printf '  got bytes: '; printf '%s' "$OUT" | od -c | head -2
    printf '  exp bytes: '; printf '%s' "$EXPECTED" | od -c | head -2
    FAIL=$((FAIL+1))
fi

# 10. flag + piped stdin → flag wins, stdin silently ignored (python/node parity)
assert_case "10. flag+stdin, flag wins silently" 0 "x" "" \
    bash -c "echo ignored | bash '$REPL' -e '__ok__x'"

# 11. unknown flag → exit 3
assert_case "11. unknown flag" 3 "" "unknown argument" \
    bash "$REPL" -z

# 12. -e missing arg → exit 3
assert_case "12. -e missing arg" 3 "" "requires an argument" \
    bash "$REPL" -e

# 13. -f nonexistent → exit 3
assert_case "13. -f nonexistent file" 3 "" "cannot read" \
    bash "$REPL" -f /nonexistent/path/xyz.cs

# 14. --help → exit 0
assert_case "14. --help" 0 "SKIP_STDOUT" "" \
    bash "$REPL" --help

# 15. --timeout non-numeric → exit 3
assert_case "15. --timeout non-numeric" 3 "" "positive integer" \
    bash "$REPL" --timeout abc -e "__ok__y"

echo "=== $PASS passed, $FAIL failed ==="
[ "$FAIL" = 0 ]
