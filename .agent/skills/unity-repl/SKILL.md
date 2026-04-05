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
using LambdaLabs.UnityRepl.Editor.Helpers;
using UnityEngine.InputSystem;   // only if com.unity.inputsystem is installed
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

### Keyboard & Mouse Input Simulation
Simulate input during Play Mode — useful for driving games, UI, or any code that polls `Keyboard.current` / `Mouse.current`. Press state persists across frames until explicitly released. Requires `com.unity.inputsystem` package.

| Feature | Native C# Equivalent Command |
|------|---------|
| Hold key | `InputUtility.PressKey(Key.W)` |
| Release key | `InputUtility.ReleaseKey(Key.W)` |
| Tap key (advance frames between) | `InputUtility.PressKey(Key.Space); EditorApplication.Step(); InputUtility.ReleaseKey(Key.Space)` |
| Click mouse button | `InputUtility.PressMouseButton("left"); EditorApplication.Step(); InputUtility.ReleaseMouseButton("left")` |
| Move mouse (screenshot coords) | `InputUtility.SetMousePosition(400, 800)` |
| Release everything | `InputUtility.ClearAllInput()` |

**Note on mouse coords:** use top-left origin (same as you see in a screenshot). The helper Y-flips to InputSystem space and focuses Game View so UGUI raycasting works correctly.

### Game View Screenshots (with Overlay UI)
Captures what the player sees, including HUD/UI overlays. Different from `ScreenCapture.CaptureScreenshot()` which can miss overlay UI in Editor.

| Feature | Native C# Equivalent Command |
|------|---------|
| Capture to Texture2D | `var tex = GameViewCaptureUtility.CaptureGameViewWithUI(); /* use then */ UnityEngine.Object.DestroyImmediate(tex);` |
| Capture to PNG file | `GameViewCaptureUtility.CaptureGameViewWithUIToFile("my_shot")` |

Files land under `Assets/Screenshots/`. Returns the Assets-relative path.

### DontDestroyOnLoad Access
No utility needed — Unity's public API already covers DDOL. FishNet network-spawned objects, managers, persistent services all live in the DDOL scene.

| Feature | Native C# Equivalent Command |
|------|---------|
| Find DDOL objects by type | `GameObject.FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None)` |
| Get DDOL Scene handle | `var p = new GameObject(); Object.DontDestroyOnLoad(p); var s = p.scene; Object.DestroyImmediate(p); s.name` |
| Enumerate DDOL roots | `var p = new GameObject(); Object.DontDestroyOnLoad(p); var roots = p.scene.GetRootGameObjects(); Object.DestroyImmediate(p); roots.Length` |

`FindObjectsByType` with `FindObjectsInactive.Include` crawls all loaded scenes including DontDestroyOnLoad — no reflection hacks needed.
