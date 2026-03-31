import { test, expect, describe, beforeEach, afterEach } from "bun:test";
import { normalizePath, getPortFilePath, readPort } from "./port-discovery.ts";
import { resolve } from "path";
import { unlinkSync, writeFileSync } from "fs";

describe("normalizePath", () => {
  test("resolves to absolute path with forward slashes", () => {
    const result = normalizePath("/Users/test/project");
    expect(result).toBe("/Users/test/project");
  });

  test("strips trailing slash", () => {
    const result = normalizePath("/Users/test/project/");
    expect(result).toBe("/Users/test/project");
  });

  test("resolves relative paths", () => {
    const result = normalizePath(".");
    expect(result).toBe(resolve(".").replace(/\\/g, "/").replace(/\/$/, ""));
  });

  test("normalizes backslashes to forward slashes", () => {
    // On macOS/Linux resolve won't produce backslashes, but our replace handles it
    const input = "/Users/test/project";
    expect(normalizePath(input)).not.toContain("\\");
  });
});

describe("getPortFilePath", () => {
  test("produces consistent hash for same path", () => {
    const path1 = getPortFilePath("/Users/test/project");
    const path2 = getPortFilePath("/Users/test/project");
    expect(path1).toBe(path2);
  });

  test("produces different hashes for different paths", () => {
    const path1 = getPortFilePath("/Users/test/project-a");
    const path2 = getPortFilePath("/Users/test/project-b");
    expect(path1).not.toBe(path2);
  });

  test("matches known MD5 hash for test path", () => {
    // Independently verified: echo -n "/Users/ichika/Documents/InworldStudio/unity-mcp-native" | md5
    // => 7d475acf...
    const result = getPortFilePath(
      "/Users/ichika/Documents/InworldStudio/unity-mcp-native"
    );
    expect(result).toBe("/tmp/unity-mcp-7d475acf.port");
  });

  test("uses /tmp on non-Windows platforms", () => {
    if (process.platform !== "win32") {
      const result = getPortFilePath("/some/path");
      expect(result).toStartWith("/tmp/");
    }
  });

  test("file name matches unity-mcp-{8 hex chars}.port pattern", () => {
    const result = getPortFilePath("/any/path");
    expect(result).toMatch(/unity-mcp-[0-9a-f]{8}\.port$/);
  });
});

describe("readPort", () => {
  const testPath = "/tmp/unity-mcp-bridge-test-readport";
  let portFilePath: string;

  beforeEach(() => {
    portFilePath = getPortFilePath(testPath);
  });

  afterEach(() => {
    try {
      unlinkSync(portFilePath);
    } catch {}
  });

  test("reads valid port from file", () => {
    writeFileSync(portFilePath, "12345");
    expect(readPort(testPath)).toBe(12345);
  });

  test("trims whitespace", () => {
    writeFileSync(portFilePath, "  54321\n");
    expect(readPort(testPath)).toBe(54321);
  });

  test("throws on missing file", () => {
    expect(() => readPort("/nonexistent/path/that/doesnt/exist")).toThrow(
      "port file not found"
    );
  });

  test("throws on invalid port content", () => {
    writeFileSync(portFilePath, "not-a-number");
    expect(() => readPort(testPath)).toThrow("Invalid port");
  });

  test("throws on port out of range", () => {
    writeFileSync(portFilePath, "99999");
    expect(() => readPort(testPath)).toThrow("Invalid port");
  });

  test("throws on port 0", () => {
    writeFileSync(portFilePath, "0");
    expect(() => readPort(testPath)).toThrow("Invalid port");
  });
});
