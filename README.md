# Native MCP Server for Unity

A pure C# MCP (Model Context Protocol) Streamable HTTP server running **inside Unity Editor**.

Implements the MCP Streamable HTTP protocol directly using `System.Net.HttpListener`, with built-in Unity tool handlers for scene, asset, script, material, prefab, VFX management and more.

## Architecture

```
AI Client  ──(HTTP Streamable/SSE)──►  Unity Editor (HttpListener :8090/mcp)  ──►  Unity API
```

## Setup

1. Add this package to your Unity project (via UPM `file:` reference or git URL)
2. Server starts automatically on editor load (port 8090)
3. Configure your MCP client:

```json
{
  "mcpServers": {
    "unity-mcp-native": {
      "url": "http://localhost:8090/mcp"
    }
  }
}
```

## Tools

| Tool | Description |
|------|-------------|
| `get_scene_tree` | Returns full in-memory scene hierarchy as a compact recursive tree. Supports filters (maxDepth, componentFilter, nameFilter, includeInactive). |
| `manage_scene` | Scene management: load, save, create, get_hierarchy (paged), screenshot. |
| `find_gameobjects` | Search GameObjects by name/tag/component with pagination. |
| `manage_gameobject` | Create, modify, delete GameObjects. |
| `manage_components` | Add, remove, configure components on GameObjects. |
| `invoke_dynamic` | Reflection-based method invocation and dynamic tool registry. |
| `manage_asset` | Asset import, move, delete, search operations. |
| `manage_script` | Script creation and editing. |
| `manage_material` | Material and shader property management. |
| `manage_editor` | Editor state control (play/stop/pause). |
| `refresh_unity` | Trigger asset database refresh and recompilation. |
| `read_console` | Read Unity console logs. |
| `simulate_input` | Simulate keyboard/mouse input in Play Mode. |
| `batch_execute` | Execute multiple tool calls in a single request. |

## Dependencies

- `com.unity.nuget.newtonsoft-json`
