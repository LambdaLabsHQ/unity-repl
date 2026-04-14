---
name: unity-repl
description: Executes raw C# expressions and statements directly on the Unity Editor Main Thread via file-based IPC. Use this to query state, modify the scene, and execute Unity API commands with infinite control.
---

# UnityREPL

UnityREPL evaluates your native C# statements directly in the running Unity Editor. You command the engine's runtime memory, scene graph, and complete API surface directly without predefined tools.

## Launching

### Interactive (REPL prompt)

**Mac / Linux**:
```bash
./Packages/com.lambda-labs.unity-repl/repl.sh
```

**Windows**:
```cmd
.\Packages\com.lambda-labs.unity-repl\repl.bat
```

Use `RunPersistent=true` with the `run_command` tool to keep the terminal alive, then send C# code using `send_command_input`.

### One-shot (preferred for scripted / agent use)

For single invocations, skip the persistent session and call the REPL like `python -c` / `node -e`:

```bash
./repl.sh -e 'GameObject.FindObjectsOfType<Camera>().Length'
./repl.sh -p 'SceneManager.GetActiveScene().name'      # -p alias, Node-style
./repl.sh -f snippet.cs                                # evaluate a file
echo 'AssetDatabase.Refresh()' | ./repl.sh             # piped stdin (auto-detected)
./repl.sh -                                            # explicit stdin
./repl.sh --timeout 10 -e '...'                        # override 60s default
```

No quoting gymnastics, no persistent session, no manual file IPC. Prefer `-f` for multi-line C# — it avoids shell-escape headaches entirely.

**Output contract (one-shot mode):** success → stdout, errors → stderr. Exit codes: `0` success, `1` runtime error, `2` compile error (or incomplete expression), `3` usage/IO error, `4` timeout. No `> ` prefix, no banner.

On Windows, **any argument** to `repl.bat` triggers one-shot mode (cmd.exe TTY detection is unreliable).

### Dry-run validation (`--validate` / `-V`)

Pair with any code source (`-e`, `-f`, `-`, or piped stdin) to compile without executing:

```bash
./repl.sh --validate -e 'EditorApplication.isPlaying = true'    # → COMPILE OK (did NOT toggle Play Mode)
./repl.sh -V -f snippet.cs                                       # → COMPILE ERROR: ... if snippet.cs has syntax errors
./repl.sh --validate -e 'class Foo {}'                           # → COMPILE OK (and Foo is NOT left in the session)
```

Responses: `COMPILE OK` (exit 0), `COMPILE ERROR: ...` (exit 2), `INCOMPLETE: ...` (exit 2). Use this for agent-side sanity checks before committing a risky eval.

**Side-effect freedom.** Validate compiles in isolation and then rolls back the evaluator's declaration state (top-level `var`/field dict, `using` directives, and type containers on the source file). Expressions, statements, `var` declarations, `using` directives, and `class`/`struct`/`method` declarations are all side-effect-free.

**Remaining caveat — redefining an existing variable.** Mono.CSharp nulls a variable's previous field value *before* the new definition is written, so `--validate -e 'var x = 100;'` when `x` already holds a value will leave `x` bound but null afterwards. Name bindings are restored by rollback, but original runtime values of re-declared vars are not. Avoid using `--validate` to probe variable redefinitions in a live session.

**Fallback.** If rollback wiring fails at startup (e.g. future Unity ships an incompatible Mono.CSharp layout), Validate degrades to the pre-rollback behavior and logs `Validate rollback DISABLED` in the Unity Console. Check `[UnityREPL] Evaluator ready` on startup for `Validate rollback enabled` to confirm.

## Basic Usage

Directly input C# expressions or statements, one per line:

```csharp
EditorApplication.isPlaying                              // Test if running, outputs True / False
EditorApplication.isPlaying = true                       // Enter Play Mode
AssetDatabase.Refresh()                                  // Refresh Asset Database
Selection.activeGameObject?.name ?? "(none)"             // Get the name of the active selected object
var go = new GameObject("Test"); go.name                 // Create object or execute multiple statements
SceneManager.GetActiveScene().name                       // Get the active scene name
GameObject.FindObjectsOfType<Camera>().Length            // Generic searches
```

## Preloaded Namespaces

The evaluation environment automatically preloads the following namespaces:

```csharp
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
```

## Responses

- **Interactive mode:** results and errors all print to stdout (prefixed by the prompt on subsequent lines).
- **One-shot mode:** success values go to **stdout**, errors go to **stderr** with exit-code classification:
  - `COMPILE ERROR:` / `INCOMPLETE:` → exit 2
  - `RUNTIME ERROR:` / generic `ERROR:` → exit 1
  - timeout → exit 4
- In both modes, exceptions are additionally logged to the Unity Console.

## Multi-Step Scenarios (Coroutines)

When an eval expression returns an `IEnumerator`, the REPL **auto-drives it across frames** and writes the final yielded value as the response. This lets you orchestrate multi-step gameplay sequences — hold keys, wait, release, screenshot — in one REPL call.

### Pattern

Wrap the coroutine in a `public static class` (Mono.CSharp doesn't accept top-level method declarations). Use `System.Collections.IEnumerator` fully qualified to disambiguate from `System.Collections.Generic.IEnumerator<T>`.

**Call 1 — define the coroutine** (persists until domain reload):

```csharp
// demo_countdown.cs
public static class Demo {
    public static System.Collections.IEnumerator CountDown() {
        Debug.Log("3");
        yield return new WaitForSeconds(1);
        Debug.Log("2");
        yield return new WaitForSeconds(1);
        Debug.Log("1");
        yield return "done";
    }
}
```

```bash
./repl.sh -f demo_countdown.cs
```

**Call 2 — invoke.** Return value is `IEnumerator` → REPL pumps it until completion. The `.res` response arrives after the coroutine finishes (~2 s here) with `"done"`.

```bash
./repl.sh -e 'Demo.CountDown()'
```

**Important:** Always split class definitions and their invocations into two separate REPL calls. The compiler treats each input as a single compilation unit — mixing a declaration with a trailing expression in one call causes a compile error.

### Supported yield instructions

| Value yielded | Edit Mode | Play Mode |
|---|---|---|
| `null`, scalars, strings | advances one tick (value becomes `LastValue`) | advances one tick |
| `WaitForSeconds` / `WaitForSecondsRealtime` | waits wall-clock seconds | native Unity scheduler |
| `CustomYieldInstruction` | polls `.keepWaiting` | native |
| `AsyncOperation` | polls `.isDone` | native |
| nested `IEnumerator` | driven recursively | native |
| `WaitForEndOfFrame`, `WaitForFixedUpdate`, `WWW`, `Task<T>` | ❌ advances one tick | ✅ native |

### Response values

- **Last yielded non-null value** becomes the `.res` response.
- `(ok)` if nothing was yielded.
- `TIMEOUT` if the coroutine exceeds the per-request timeout (default 60s).
- `CANCELLED` if a `.cancel` file is dropped (or Ctrl-C from `repl.sh`).
- `RUNTIME ERROR: ...` if the coroutine throws.
- `RELOAD` if Unity domain reloads mid-flight.
- `BUSY: queue full` if more than 8 coroutines are queued.

### Timeout override

First-line directive inside the class-definition file:
```csharp
//!timeout=30s
public static class Slow { public static System.Collections.IEnumerator Go() { ... } }
```
Then invoke separately: `./repl.sh --timeout 30 -e 'Slow.Go()'`

Also accepts `//!timeout=2m` or `//!timeout=5000` (bare ms). Align client `--timeout` / `TIMEOUT_S` env var to match.

### Pitfalls

- **Domain reload wipes defined classes.** Entering/exiting Play Mode triggers a reload — any `public static class Foo {}` you defined needs to be re-sent. Keep coroutine definitions in a `.cs` file and replay via `repl.sh -f` after reload.
- **`yield` at top level is illegal** (Mono.CSharp doesn't support local functions). Always wrap: `public static class X { public static System.Collections.IEnumerator Y() { ... } }`.
- **Top-level method declarations fail** with `CS1525: Unexpected symbol '('` even when prefixed with modifiers. The wrapper class is required.
- **One declaration per call.** Each REPL input is compiled as a single unit. Mixing a class/using declaration with a trailing expression (e.g. `class Foo {} new Foo().x`) causes a compile error. Always split: define in one call, invoke in the next.

## Snippets & Reference

Do not use legacy tools. Query properties or modify systems by authoring temporary C# execution blocks.

### Control and Scenes
| Feature | Native C# Equivalent Command |
|------|---------|
| Get current scene | `SceneManager.GetActiveScene().name` |
| Enter Play Mode | `EditorApplication.isPlaying = true` |
| Stop Play Mode | `EditorApplication.isPlaying = false` |
| Compile Assets | `AssetDatabase.Refresh()` |
| Open scene | `EditorSceneManager.OpenScene("Assets/Scenes/Lab.unity")` |

### GameObject and Instantiation
| Feature | Native C# Equivalent Command |
|------|---------|
| Find object | `GameObject.Find("Player") != null ? "Found" : "Missing"` |
| All Cameras | `string.Join(", ", GameObject.FindObjectsOfType<Camera>().Select(c => c.name))` |
| Instantiate prefab | `var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cube.prefab"); var instance = PrefabUtility.InstantiatePrefab(p) as GameObject; instance.name` |
| Add Component | `var go = GameObject.Find("Player"); if(go != null) go.AddComponent<BoxCollider>();` |

### Debugging and Logging
| Feature | Native C# Equivalent Command |
|------|---------|
| Get error count | `var m = typeof(UnityEditor.LogEntries).GetMethod("GetCount", System.Reflection.BindingFlags.Static\|System.Reflection.BindingFlags.Public); m.Invoke(null,null)` |
| Take Screenshot | `ScreenCapture.CaptureScreenshot("/tmp/shot.png")` |

## Installed Agent Packages

If any `unity-agent-*` packages are installed in this project, their tool docs are
available at `Temp/UnityReplIpc/extensions.md` (regenerated on every domain reload).

Read them from REPL: `System.IO.File.ReadAllText("Temp/UnityReplIpc/extensions.md")`
