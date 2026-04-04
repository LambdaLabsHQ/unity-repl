#!/usr/bin/env bun
/**
 * UnityREPL one-shot: evaluate a single C# expression and exit.
 * Usage: bun run eval.ts "EditorApplication.isPlaying = true"
 */
import { resolve, join } from "path";
import { readFileSync, writeFileSync, existsSync, unlinkSync, mkdirSync, renameSync } from "fs";
import { randomUUID } from "crypto";

const projectRoot = resolve(__dirname, "../../../../");
const ipcDir = join(projectRoot, "Temp", "UnityReplIpc");
const reqDir = join(ipcDir, "Requests");
const resDir = join(ipcDir, "Responses");

mkdirSync(reqDir, { recursive: true });
mkdirSync(resDir, { recursive: true });

const code = process.argv[2];
if (!code) {
  console.error("Usage: bun run eval.ts '<C# expression>'");
  process.exit(1);
}

const uuid = randomUUID();
const reqTmp = join(reqDir, `${uuid}.tmp`);
const reqFile = join(reqDir, `${uuid}.req`);
const resFile = join(resDir, `${uuid}.res`);

writeFileSync(reqTmp, code);
renameSync(reqTmp, reqFile);

// Poll for response
let waited = 0;
const TIMEOUT = 30_000;
while (!existsSync(resFile)) {
  await new Promise(r => setTimeout(r, 50));
  waited += 50;
  if (waited > TIMEOUT) {
    console.error("ERROR: timeout (30s)");
    process.exit(1);
  }
}

const result = readFileSync(resFile, "utf-8");
unlinkSync(resFile);
console.log(result);
