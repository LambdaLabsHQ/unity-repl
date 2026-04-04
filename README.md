# Unity REPL: The Post-Tool AI Architecture

> **REPL is the ultimate evolution of AI agent tooling. Meta-language abstraction is the highest form of tool calling.**

For years, integrating AI agents with game engines meant building bridges: defining strict RPC schemas, rigid JSON wrappers, and highly constrained CLI commands. Every new AI capability required human engineers to meticulously expose a new "Tool." This created a profound architectural bottleneck—putting hyper-intelligent autonomous agents inside suffocating sandboxes.

**Unity REPL shatters the sandbox.** 

We abandoned rigid MCP JSON-RPC servers. We obsoleted the restrictive Bash CLI wrappers that once claimed to replace them. We stripped away every translation layer. Instead of granting AI agents a pre-approved menu of CLI arguments or MCP endpoints, we grant them the engine itself.

By evaluating raw C# strings directly on the Unity Main Thread through a high-performance File IPC, **the meta-language becomes the universal tool.**

### The Core Paradigm: Tokens = Execution

- **Omniscient Access:** The entire Unity API, runtime memory space, live scene graph, and Editor context are fully exposed. No remote bridging required.
- **Zero-Friction Mutation:** When an agent formulates an idea, it doesn't search for an API endpoint or construct a JSON payload. It writes native C#. 
- **The Death of Predefined Tooling:** We eliminated all JSON serialization overhead, bridging layers, and mapped endpoints. The compiler *is* the API.

### Pure REPL: A Step Beyond Chrome MCP

While leading architectures like Chrome DevTools MCP introduced powerful raw JS `evaluate` capabilities, they fundamentally remained hybrid models. They continued to force AI agents to navigate between rigid wrapped tools (e.g., `navigate()`, `click()`) and a secondary Javascript sandbox. 

Unity REPL commits fully to **Pure Meta-Language Interaction**. By discarding all predefined MCP wrappers, it achieves unparalleled architectural superiority:

- **Minimal Token Overhead:** There are zero heavy JSON tool schemas or API instructions to parse.
- **Absolute Directness:** No API bridging or translation layers. The Unity C# compiler executes your tokens natively.
- **Infinite Extensibility (Self-Authoring Tools):** You never wait for an engineer to expose an MCP tool. The AI can dynamically solidify complex multi-line REPL operations into permanent C# scripts and execute them directly later (e.g. `ExecuteMacro("build_scene.cs")`). The agent builds its own self-expanding toolbelt sequentially, with zero recompilation.
- **Cognitive Consistency:** The AI's reasoning loop is entirely unified in C#, eliminating decision hesitation over "which tool to use".

## Architecture

```
AI Agent  ──(Raw C# Tokens)──►  File IPC (/Temp/UnityReplIpc/)  ──►  Unity Editor Main Thread
```

## Quickstart

This package embeds the persistent REPL server seamlessly into your Unity Editor workflow via `InitializeOnLoad`. 

1. Add this package to your Unity project.
2. The Editor continuously listens for C# compilation requests locally.
3. Drive the engine using the TypeScript client via any autonomous agent (or manual shell):

```bash
bun run Packages/com.lambda-labs.unity-repl/ts/src/repl.ts
```

## Welcome to Infinite Control

You no longer call brittle `GetState()` or `SpawnObject()` macros. You command the universe dynamically:

```csharp
// Evaluate states instantly
EditorApplication.isPlaying = true;

// Mutate and probe with absolute freedom
var components = GameObject.FindObjectsOfType<Camera>();
string.Join(", ", components.Select(c => c.name));
```

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

**Welcome to the era of unrestricted access. The language is your only tool.**
