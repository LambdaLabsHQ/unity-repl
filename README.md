# Unity REPL: The most powerful AI interface for Unity

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](https://github.com/LambdaLabsHQ/unity-repl/blob/master/LICENSE)

> Any sufficiently complicated C or Fortran program contains an ad hoc, informally-specified, bug-ridden, slow implementation of half of Common Lisp.
>
> — *Greenspun's Tenth Rule*

An interpreter that evaluates an AI's output as code is superior to any form of tool-calling infrastructure, including MCP and CLI.

MCP servers and CLI wrappers are ad hoc interpreters slowly reconstructing what a REPL already is. **Unity REPL** skips the middleman: it grants AI agents direct, evaluative access to the Unity Main Thread through raw C# — no schemas, no wrappers, no human-curated endpoints. Token becomes language, language becomes execution, execution becomes the universal tool.

<p align="center">
  <video src="https://github.com/user-attachments/assets/c4d04f42-6495-4a8a-a6e8-aa5dd997bd22" controls width="100%"></video>
</p>

> The agent executes a REPL script it previously discovered and crystallized from an earlier agent exploration session — which also used Unity REPL — with no pre-registered MCP tools and no hardcoded wrappers. Exploration turns into reusable automation.

## Getting Started

Open your coding agent inside a Unity project, then paste the following prompt to install Unity REPL:

```
Add `"com.lambda-labs.unity-repl": "https://github.com/LambdaLabsHQ/unity-repl.git"` to the `dependencies` in `Packages/manifest.json`.

Then register the Unity REPL skill: try running `npx skills add ./Packages/com.lambda-labs.unity-repl`.

If that fails (e.g. Node.js is not installed), the skill definition is at `./Packages/com.lambda-labs.unity-repl/.agents/skills/unity-repl/SKILL.md` — use your agent runtime's skill installer to register it.

Finally, verify the REPL server is working by evaluating `Application.unityVersion` through the skill.
```

For step-by-step manual instructions, see [Manual install](#manual-install).

## A Live Session: Infinite Control

How deep does the control go? Here is a raw transcript of an agent dynamically probing and mutating a highly complex Unity state without any pre-configured tools:

```text
UnityREPL ready. Type C# expressions:
> EditorApplication.isPlaying = true;
 
> SceneManager.GetActiveScene().name
MainMenu
 
> // Agent: "I need to spawn a testing unit to verify the turrets."
> var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Blender.prefab");
> var obj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
> obj.transform.position
(0.00, 0.00, 0.00)
 
> // Agent: "Let's teleport it into the kill zone and check physical state."
> obj.transform.position = new Vector3(10, 0, 5);
> obj.GetComponent<NetworkTransform>() != null
True
 
> // Agent: "I'll fetch all live active turrets and log their target distances."
> var turrets = GameObject.FindObjectsOfType<Turret>();
> string.Join("\n", turrets.Select(t => $"{t.name}: {Vector3.Distance(t.transform.position, obj.transform.position)}m"));
LaserTurret_1: 12.5m
GrenadeTurret_2: 8.2m
```

You didn't need developers to hardcode a `SpawnEnemy()` or `GetTurretDistances()` endpoint for you today. You just wrote the C# and it executed on the Main Thread.

### Native Asynchronous Execution

In conventional JSON-RPC or MCP tool architectures, waiting for a scene to load or an animation to finish requires creating complex internal state machines, or forcing the AI to spam polling requests via intervals.

**With Pure REPL, asynchronous execution is solved at the language level.** When a REPL expression returns an `IEnumerator`, the server drives it across frames and writes the final yielded value as the response. You write idiomatic Unity coroutines; the `.res` file simply arrives later.

```csharp
// Call 1 — define the helper (persists in the session)
// Mono.CSharp requires a wrapper class — top-level methods are not supported.
public static class Setup {
    public static System.Collections.IEnumerator ComplexSetup() {
        EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity");
        yield return null;                      // one editor tick
        var go = new GameObject("TestEnemy");
        yield return new WaitForSeconds(2.0f);  // delay across real seconds
        go.GetComponent<Health>().Damage(10);
        yield return "done";                    // last yielded value → .res response
    }
}
```

```csharp
// Call 2 — invoke it; .res arrives ~2 seconds later with value "done"
Setup.ComplexSetup()
```

> **Note:** Class definitions and invocations must be separate REPL calls. Each input is compiled as a single unit.

**Supported yield instructions:**

| Value yielded | Edit Mode | Play Mode |
|---|---|---|
| `null`, scalars, strings | advance next tick (value becomes `LastValue`) | advance next tick |
| `WaitForSeconds` / `WaitForSecondsRealtime` | waits wall-clock `seconds` | native Unity scheduler |
| `CustomYieldInstruction` | polls `.keepWaiting` | native |
| `AsyncOperation` | polls `.isDone` | native |
| nested `IEnumerator` | driven to completion, then outer resumes | native |
| `WaitForEndOfFrame`, `WaitForFixedUpdate`, `WWW`, `Task<T>` | ❌ advances one tick (use Play Mode) | ✅ native |

**Response contract:**

- Last yielded value (via `.ToString()`) — or `(ok)` if no value was yielded.
- `TIMEOUT` if the coroutine exceeds its timeout.
- `CANCELLED` if a `.cancel` file is dropped for its UUID.
- `RUNTIME ERROR: …` if the coroutine throws.
- `RELOAD` if the Unity domain reloads mid-flight (e.g. you save a `.cs` file).
- `BUSY: queue full` if more than 8 coroutines are already queued.

**Timeout and cancellation:**

- Default per-request timeout is **60 seconds**. Override per call with a first-line directive: `//!timeout=30s`, `//!timeout=2m`, or `//!timeout=5000` (bare ms).
- Set the client env var `TIMEOUT_S` to extend how long `repl.sh`/`repl.bat` wait for `.res` (align with your `//!timeout=`).
- Drop an empty file at `Temp/UnityReplIpc/Requests/{uuid}.cancel` to abort a running coroutine. `repl.sh` does this automatically on the first `Ctrl-C` (a second `Ctrl-C` hard-exits the client).

## Manual install

This package embeds the persistent REPL server seamlessly into your Unity Editor workflow via `InitializeOnLoad`.

1. **Add the Unity packages.** Use the Unity Package Manager (Window > Package Manager > Add by git URL) or edit `Packages/manifest.json` directly:
   ```json
   {
     "dependencies": {
       "com.lambda-labs.unity-repl": "https://github.com/LambdaLabsHQ/unity-repl.git",
       "com.lambda-labs.unity-agent-input": "https://github.com/LambdaLabsHQ/unity-agent-input.git",
       "com.lambda-labs.unity-agent-vision": "https://github.com/LambdaLabsHQ/unity-agent-vision.git"
     }
   }
   ```
   > Only `com.lambda-labs.unity-repl` is strictly required. If you prefer the minimal setup:
   > ```json
   > {
   >   "dependencies": {
   >     "com.lambda-labs.unity-repl": "https://github.com/LambdaLabsHQ/unity-repl.git"
   >   }
   > }
   > ```

2. **Register the skill.** This teaches your AI agent (Claude Code, Cursor, Codex CLI, etc.) how to use the REPL — call conventions, response formats, coroutine patterns, and ability discovery:
   ```bash
   npx skills add ./Packages/com.lambda-labs.unity-repl
   ```

3. **Verify.** With the Unity Editor open, invoke the skill from your agent (e.g. `/unity-repl what is the Unity version`).

### Non-interactive invocation

For scripts, CI pipelines, and automation, `repl.sh` / `repl.bat` also accept python/node-style flags:

```bash
# Inline expression
./repl.sh -e 'EditorApplication.isPlaying'
./repl.sh -p 'SceneManager.GetActiveScene().name'      # -p alias (Node-style)

# Evaluate a file
./repl.sh -f snippet.cs

# Piped stdin (auto-detected — no TTY = non-interactive)
echo 'AssetDatabase.Refresh()' | ./repl.sh
./repl.sh < snippet.cs

# Explicit stdin
./repl.sh -

# Override timeout (default 60s; also via REPL_TIMEOUT env var)
./repl.sh --timeout 5 -e '...'
```

**Output contract:** success value goes to **stdout**, errors/diagnostics to **stderr**. No banner or `> ` prefix in non-interactive mode — output is directly machine-parseable.

**Exit codes:**

| Code | Meaning |
|------|---------|
| 0    | success |
| 1    | runtime error |
| 2    | compile error (or incomplete expression) |
| 3    | usage error / file I/O error |
| 4    | timeout waiting for Unity |

On Windows, **any argument** puts `repl.bat` in non-interactive mode (cmd.exe TTY detection is unreliable, so detection is via arg-presence instead of stdin piping).

### Dry-run validation (`--validate` / `-V`)

Compile without executing — useful for syntax checking, linting, or pre-flight validation:

```bash
./repl.sh --validate -e 'EditorApplication.isPlaying = true'   # → COMPILE OK (did NOT toggle Play Mode)
./repl.sh -V -f snippet.cs                                      # → COMPILE ERROR: ... if syntax errors
./repl.sh --validate -e 'class Foo {}'                          # → COMPILE OK (and Foo is NOT left in session)
```

Responses: `COMPILE OK` (exit 0), `COMPILE OK (no-op)` for declarations (exit 0), `COMPILE ERROR: ...` (exit 2), `INCOMPLETE: ...` (exit 2). Declaration rollback is automatic when supported by the runtime — the validated code leaves no side effects in the evaluator session.

Alternatively, prepend `//!validate` as an inline directive in the source:
```csharp
//!validate
EditorApplication.isPlaying = true
```

## Requirements

- **Unity 2021.3** or later
- Editor-only — no runtime dependencies
- Windows, macOS, Linux

## The Post-Tool AI Architecture

> **Token is language. Language is evaluation. Evaluation is the universal tool.**

### The Death of the Tool Layer

CLI is a meaningful step in the right direction. It moves AI systems toward more general-purpose interfaces and away from narrow, hand-authored RPC schemas. But it still stops one layer too early. A universal command surface is not enough; what agents ultimately need is a Turing-complete evaluator at the tool layer itself.

Unity REPL provides that evaluator. The agent does not ask a server for permission to act, nor does it squeeze intent through a finite command vocabulary. It emits C#, and the engine evaluates it on the Main Thread. There is no tool registry to maintain, no schema to version, no wrapper to debug. The language *is* the interface.

### From Exploration to Crystallization

In a traditional tool architecture, discovery and execution are owned by separate teams: engineers build the tools, agents use them. With REPL, the agent owns both phases. It explores the Unity API interactively, distills a working pattern into a reusable C# script, and executes that script later without any recompilation or human intervention. The toolbelt is not installed — it is authored in flight.

### Architecture

```
AI Agent  ──(Raw C# Tokens)──►  File IPC (/Temp/UnityReplIpc/)  ──►  Unity Editor Main Thread
```

**Welcome to the era of unrestricted access. The language is your only tool.**

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

[AGPL-3.0](LICENSE) — Copyright (C) 2025-2026 LambdaLabs
