# unity-mcp-bridge

A stdio-to-HTTP bridge that lets Claude Code connect to a Unity Editor's MCP server. Supports multiple Unity instances (e.g. one per git worktree) by automatically discovering the correct port via a temp file keyed on the project path.

## Architecture

```
Claude Code (stdio JSON-RPC)
    |
unity-mcp-bridge (this project)
    | HTTP POST
Unity Editor HttpListener (/mcp)
```

## How it works

1. Unity Editor starts its MCP server on a random port and writes the port to `/tmp/unity-mcp-{hash}.port`, where `{hash}` is the first 8 chars of `MD5(project_path)`.
2. Claude Code spawns the bridge via `.mcp.json`. The bridge computes the same hash from `process.cwd()`, reads the port file, and connects.
3. All JSON-RPC messages are transparently proxied: stdin -> HTTP POST -> stdout.

## Setup

Requires [Bun](https://bun.sh) runtime.

### Global install (recommended)

Register the bridge globally so any project can use it:

```bash
cd unity-mcp-bridge
bun link
```

Then in your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "unity": {
      "command": "bunx",
      "args": ["unity-mcp-bridge"]
    }
  }
}
```

This works across git worktrees since it doesn't depend on relative paths.

### Local (direct path)

Alternatively, run the bridge directly by path:

```json
{
  "mcpServers": {
    "unity": {
      "command": "bun",
      "args": ["./unity-mcp-bridge/src/index.ts"]
    }
  }
}
```

## Testing

```bash
cd unity-mcp-bridge
bun test
```

## Multi-instance usage

Each git worktree has its own project path, so each gets a unique port file. As long as each worktree has a Unity Editor open, the bridge in each worktree will auto-connect to the correct instance.
