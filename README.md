# Unity REPL: The most powerful AI interface for Unity

Unity REPL evaluates raw C# strings directly on the Unity Main Thread through a high-performance File IPC, providing an architecture that is fundamentally superior to both rigid MCP (Model Context Protocol) servers and standard CLI wrappers. Instead of granting AI agents a pre-approved menu of parsed arguments or JSON-RPC endpoints, we grant them the engine itself. **The meta-language becomes the universal tool.**

## A Live Session: Infinite Control

How deep does the control go? Here is a raw transcript of an Agent dynamically probing and mutating a highly complex Unity state without any pre-configured tools:

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

**With Pure REPL, asynchronous execution is solved at the language level.**

By wrapping self-authored scripts into Unity's generic `EditorCoroutine` or utilizing modern `async/await` syntax, agents can script and solidify complex time-dependent sequences synchronously in token logic, without freezing the Unity Editor's main thread:

```csharp
using Unity.EditorCoroutines.Editor;

IEnumerator ComplexSetup() {
    EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity");
    yield return null; // Wait for frame cycle
    
    var go = new GameObject("TestEnemy");
    yield return new WaitForSeconds(2.0f); // Yield dynamically
    
    go.GetComponent<Health>().Damage(10);
    Debug.Log("Pipeline Finished!");
}

// Dispatch directly
EditorCoroutineUtility.StartCoroutine(ComplexSetup(), this);
```

There is no need to write API-level queues or RPC timeout configurations. The meta-language handles it natively.

## Quickstart

This package embeds the persistent REPL server seamlessly into your Unity Editor workflow via `InitializeOnLoad`. 

1. Add this package to your Unity project.
2. The Editor continuously listens for C# compilation requests locally.
3. Drive the engine using the native REPL wrapper via any autonomous agent (or manual shell):

**Mac / Linux**:
```bash
./Packages/com.lambda-labs.unity-repl/repl.sh
```

**Windows**:
```cmd
.\Packages\com.lambda-labs.unity-repl\repl.bat
```

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

## The Post-Tool AI Architecture

> **REPL is the ultimate evolution of AI agent tooling. Meta-language abstraction is the highest form of tool calling.**

For years, integrating AI agents with game engines meant building bridges: defining strict RPC schemas, rigid JSON wrappers, and highly constrained CLI commands. Every new AI capability required human engineers to meticulously expose a new "Tool." This created a profound architectural bottleneck—putting hyper-intelligent autonomous agents inside suffocating sandboxes.

**Unity REPL shatters the sandbox.** 

We abandoned rigid MCP JSON-RPC servers. We obsoleted the restrictive Bash CLI wrappers that once claimed to replace them. We stripped away every translation layer. 

### Why Pure REPL is Superior to MCP

1. **Zero Schema Overhead:** MCP servers suffocate context windows with hundreds of lines of rigid JSON schemas that AI agents must memorize and parse. Unity REPL requires zero schema mapping—its schema is the C# language itself.
2. **Eradication of the Engineering Bottleneck:** In MCP, every time an agent needs a new capability, a human developer must write a new backend endpoint, compile it, and restart the server bridge. Unity REPL allows the agent to self-serve any capability immediately by just evaluating C#.
3. **No Serialization Loss:** There is no need to serialize deeply nested Unity Object graphs into JSON datasets merely to pipe them into LLM context. You query the exact properties you need sequentially using native C# syntax.

### Why Pure REPL is Superior to CLI Wrappers

1. **Elimination of Parse/Match Logic:** CLI wrappers (like `unity-cli inspect --object=Player`) force developers to write brittle string-argument parsers. With REPL, the agent types `GameObject.Find("Player")`. The Unity Editor's C# compiler executes the tokens natively with zero interpretation loss.
2. **Persistent Memory State:** Standard CLI commands invoke, load, execute, and die—losing state between calls. Our File IPC seamlessly taps into the *currently running* Unity Editor Main Thread without any spin-up cost, context dropping, or Editor reloading.

### A Step Beyond Chrome MCP

While leading architectures like Chrome DevTools MCP introduced powerful raw JS `evaluate` capabilities, they fundamentally remained hybrid models. They continued to force AI agents to navigate between rigid wrapped tools (e.g., `navigate()`, `click()`) and a secondary JavaScript sandbox. 

Unity REPL commits fully to **Pure Meta-Language Interaction**. 
- **Absolute Directness:** No API bridging or translation layers. The Unity C# compiler executes your tokens natively.
- **Infinite Extensibility (Self-Authoring Tools):** You never wait for an engineer to expose an MCP tool. The AI can dynamically solidify complex multi-line REPL operations into permanent C# scripts and execute them directly later (e.g. `ExecuteMacro("build_scene.cs")`). The agent builds its own self-expanding toolbelt sequentially, with zero recompilation.
- **Cognitive Consistency:** The AI's reasoning loop is entirely unified in C#, eliminating decision hesitation over "which tool to use".

### Architecture

```
AI Agent  ──(Raw C# Tokens)──►  File IPC (/Temp/UnityReplIpc/)  ──►  Unity Editor Main Thread
```

**Welcome to the era of unrestricted access. The language is your only tool.**
