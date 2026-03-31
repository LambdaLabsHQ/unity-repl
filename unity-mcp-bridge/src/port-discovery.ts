import { createHash } from "crypto";
import { readFileSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

// Use /tmp on macOS/Linux for a stable path that matches the C# side.
// os.tmpdir() returns $TMPDIR which varies per user session and may differ
// between the Unity Editor process and the bridge process.
const TMP_DIR = process.platform === "win32" ? tmpdir() : "/tmp";

/**
 * Normalize a path to match the C# PortFileManager normalization:
 * absolute path, forward slashes, no trailing slash.
 */
export function normalizePath(p: string): string {
  return resolve(p).replace(/\\/g, "/").replace(/\/$/, "");
}

/**
 * Compute the port file path for a given project directory.
 * Uses first 8 hex chars of MD5(normalized_path) as the identifier.
 */
export function getPortFilePath(projectPath?: string): string {
  const normalized = normalizePath(projectPath || process.cwd());
  const hash = createHash("md5").update(normalized).digest("hex").slice(0, 8);
  return join(TMP_DIR, `unity-mcp-${hash}.port`);
}

/**
 * Read the port number from the port file for the given project directory.
 * Throws if the file doesn't exist or contains an invalid port.
 */
export function readPort(projectPath?: string): number {
  const filePath = getPortFilePath(projectPath);
  const file = Bun.file(filePath);
  let content: string;
  try {
    content = readFileSync(filePath, "utf-8").trim();
  } catch {
    throw new Error(
      `Unity MCP port file not found: ${filePath}\n` +
        `Is the Unity Editor running for this project?`
    );
  }
  const port = parseInt(content, 10);
  if (isNaN(port) || port < 1 || port > 65535) {
    throw new Error(`Invalid port in ${filePath}: "${content}"`);
  }
  return port;
}
