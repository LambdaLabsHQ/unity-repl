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

## Dependencies

- `com.unity.nuget.newtonsoft-json`
