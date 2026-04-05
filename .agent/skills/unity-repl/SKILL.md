---
name: unity-repl
description: Executes raw C# expressions and statements directly on the Unity Editor Main Thread via file-based IPC. Use this to query state, modify the scene, and execute Unity API commands with infinite control.
---

# UnityREPL

UnityREPL evaluates your native C# statements directly in the running Unity Editor. You command the engine's runtime memory, scene graph, and complete API surface directly without predefined tools.

## Launching

Run directly via the system native terminal:

**Mac / Linux**:
```bash
./Packages/com.lambda-labs.unity-repl/repl.sh
```

**Windows**:
```cmd
.\Packages\com.lambda-labs.unity-repl\repl.bat
```

Use `RunPersistent=true` with the `run_command` tool to keep the terminal alive, then send C# code using `send_command_input`.

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

- Single values or expression results are printed to stdout.
- Exceptions or compilation errors output `ERROR` and log to the Unity Console.

## Multi-Step Scenarios (Coroutines)

When an eval expression returns an `IEnumerator`, the REPL **auto-drives it across frames** and writes the final yielded value as the response. This lets you orchestrate multi-step gameplay sequences — hold keys, wait, release, screenshot — in one REPL call.

### Pattern

Wrap the coroutine in a `public static class` (Mono.CSharp doesn't accept top-level method declarations). Use `System.Collections.IEnumerator` fully qualified to disambiguate from `System.Collections.Generic.IEnumerator<T>`.

```csharp
// Step 1: define the coroutine (persists until domain reload)
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

// Step 2: invoke. Return value is IEnumerator → REPL pumps it until completion.
// The .res response arrives after the coroutine finishes (~2s here) with "done".
Demo.CountDown()
```

Ship both steps as one file via `repl.sh -f scenario.cs` — the last expression is the invocation, and the whole file evaluates as a single unit.

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

First-line directive inside the file:
```csharp
//!timeout=30s
public static class Slow { public static System.Collections.IEnumerator Go() { ... } }
Slow.Go()
```
Also accepts `//!timeout=2m` or `//!timeout=5000` (bare ms). Align client `--timeout` / `TIMEOUT_S` env var to match.

### Pitfalls

- **Domain reload wipes defined classes.** Entering/exiting Play Mode triggers a reload — any `public static class Foo {}` you defined needs to be re-sent. Keep coroutine definitions in a `.cs` file and replay via `repl.sh -f` after reload.
- **`yield` at top level is illegal** (Mono.CSharp doesn't support local functions). Always wrap: `public static class X { public static System.Collections.IEnumerator Y() { ... } }`.
- **Top-level method declarations fail** with `CS1525: Unexpected symbol '('` even when prefixed with modifiers. The wrapper class is required.

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
