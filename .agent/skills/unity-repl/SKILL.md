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
```

## Responses

- **Interactive mode:** results and errors all print to stdout (prefixed by the prompt on subsequent lines).
- **One-shot mode:** success values go to **stdout**, errors go to **stderr** with exit-code classification:
  - `COMPILE ERROR:` / `INCOMPLETE:` → exit 2
  - `RUNTIME ERROR:` / generic `ERROR:` → exit 1
  - timeout → exit 4
- In both modes, exceptions are additionally logged to the Unity Console.

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
