#!/usr/bin/env bash
# Manual smoketest for Validate() rollback behavior.
# REQUIRES a running Unity Editor with the REPL server loaded — NOT run by
# tests~/repl.test.sh (which uses mock_unity.sh and can't exercise real
# Mono.CSharp internals).
#
# Usage:
#   cd <your Unity project root>
#   bash path/to/unity-repl/tests~/validate_rollback_smoketest.sh
#
# Exit 0 if rollback works end-to-end, non-zero otherwise.

set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPL="$(cd "$SCRIPT_DIR/.." && pwd)/repl.sh"

PASS=0
FAIL=0

# assert_validate_rolls_back NAME VALIDATE_CODE PROBE_CODE
# Runs --validate on VALIDATE_CODE (expecting success), then runs PROBE_CODE
# which is expected to FAIL with COMPILE ERROR because rollback should have
# removed whatever VALIDATE_CODE declared.
assert_validate_rolls_back() {
    local name="$1" validate_code="$2" probe_code="$3"

    local vout vec
    vout=$(bash "$REPL" --validate -e "$validate_code" 2>&1); vec=$?
    if [ "$vec" != 0 ] || [ "$vout" != "COMPILE OK" ]; then
        echo "FAIL: $name — validate unexpected: exit=$vec out=<<$vout>>"
        FAIL=$((FAIL+1)); return
    fi

    local pout pec
    pout=$(bash "$REPL" -e "$probe_code" 2>&1); pec=$?
    if [ "$pec" = 2 ]; then
        echo "PASS: $name (probe correctly failed after rollback)"
        PASS=$((PASS+1))
    else
        echo "FAIL: $name — probe should have failed post-rollback but got: exit=$pec out=<<$pout>>"
        FAIL=$((FAIL+1))
    fi
}

echo "=== Validate rollback smoketest (requires live Unity Editor) ==="

# Sanity: REPL is alive.
if ! bash "$REPL" --timeout 5 -e '1+1' >/dev/null 2>&1; then
    echo "ERROR: REPL server not responding. Start Unity Editor with the package loaded." >&2
    exit 2
fi

# 1. var declaration rollback. __probe_var should not exist after validate.
assert_validate_rolls_back \
    "1. var x = 42 rollback" \
    'var __probe_var = 42;' \
    '__probe_var'

# 2. class declaration rollback. new __ProbeClass() should fail to resolve.
assert_validate_rolls_back \
    "2. class __ProbeClass {} rollback" \
    'class __ProbeClass { public int x = 5; }' \
    'new __ProbeClass().x'

# 3. using directive rollback. Regex should not be in scope afterwards.
#    Use a namespace not in the default usings.
assert_validate_rolls_back \
    "3. using System.Text.RegularExpressions rollback" \
    'using System.Text.RegularExpressions;' \
    'typeof(Regex).Name'

# 4. method-containing class rollback.
assert_validate_rolls_back \
    "4. class with static method rollback" \
    'public static class __ProbeStatics { public static int Answer() => 42; }' \
    '__ProbeStatics.Answer()'

# 5. Positive control — validate without any declaration should obviously not
#    affect subsequent state. Baseline "repl is alive" re-check.
vout=$(bash "$REPL" --validate -e '1 + 1' 2>&1); vec=$?
if [ "$vec" = 0 ] && [ "$vout" = "COMPILE OK" ]; then
    echo "PASS: 5. validate expression still COMPILE OK"
    PASS=$((PASS+1))
else
    echo "FAIL: 5. validate expression: exit=$vec out=<<$vout>>"
    FAIL=$((FAIL+1))
fi

echo "=== $PASS passed, $FAIL failed ==="
[ "$FAIL" = 0 ]
