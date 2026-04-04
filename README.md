# Lambda Labs Unity REPL

A pure C# REPL running **inside Unity Editor**.
Executes arbitrary C# code directly on the Unity Main Thread using File-based IPC.

## Architecture

```
Client (bun/ts)  ──(File IPC: /Temp/UnityReplIpc/)──►  Unity Editor (UnityReplServerHost)  ──►  Unity API
```

## Setup

1. Add this package to your Unity project.
2. The REPL server starts automatically on Unity Editor load (via `InitializeOnLoad`).
3. Connect and execute scripts using the TS client:

```bash
bun run Packages/com.lambda-labs.unity-repl/ts/src/repl.ts
```

## Usage

Send C# expressions or statements directly from the REPL shell. The Unity Editor will execute them and return the stringified results.

```csharp
EditorApplication.isPlaying = true;
var go = new GameObject("Test"); go.name;
```

## Dependencies

- `com.unity.nuget.newtonsoft-json`
