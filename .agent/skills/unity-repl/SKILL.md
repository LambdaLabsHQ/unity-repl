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
using LambdaLabs.UnityRepl.Editor.Helpers;
using UnityEngine.InputSystem;   // only if com.unity.inputsystem is installed
```

## Responses

- Single values or expression results are printed to stdout.
- Exceptions or compilation errors output `ERROR` and log to the Unity Console.

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
