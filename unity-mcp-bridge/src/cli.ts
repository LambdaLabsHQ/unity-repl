#!/usr/bin/env bun
import { readPort } from "./port-discovery.ts";
import { resolve } from "path";

const helpText = `usage: unity-cli [-h] <command> [args...]

Unity MCP Native CLI Wrapper

Available commands:
  invoke_dynamic      Call arbitrary C# methods
  get_scene_tree      Get scene hierarchy
  manage_editor       Managed Editor play/stop state
  manage_scene        Manage scene actions (e.g. screenshot)
  refresh_unity       Trigger assembly reload
  read_console        Read Unity console logs

options:
  -h, --help            show this help message and exit
  --port PORT           manually specify port (optional)
`;

const commandHelpText: Record<string, string> = {
  invoke_dynamic: `usage: unity-cli invoke_dynamic --method METHOD [--args ARGS_JSON]
  --method    Method name to call (e.g., MyClass.MyStaticMethod)
  --args      JSON object string of arguments (e.g., '{"val":1}')
`,
  get_scene_tree: `usage: unity-cli get_scene_tree [options]
  --max-depth          Max recursion depth (-1 for unlimited)
  --no-include-inactive Exclude inactive GameObjects
  --component-filter   Filter by component type name (substring)
  --name-filter        Filter by GameObject name (substring)
  --no-include-path    Exclude hierarchy path
  --include-transform  Include transform data
  --scene-index        Specify scene index to query
`,
  manage_editor: `usage: unity-cli manage_editor <play|stop|pause>
`,
  manage_scene: `usage: unity-cli manage_scene <action> [options]
  action               Currently supports: screenshot
  --super-size         Screenshot super size multiplier (default 1)
`,
  refresh_unity: `usage: unity-cli refresh_unity
`,
  read_console: `usage: unity-cli read_console [options]
  --count              Number of latest logs to return (default 200)
`
};

const args = process.argv.slice(2);

// Handle top-level help
if (args.length === 0 || (args.length === 1 && (args[0] === '-h' || args[0] === '--help'))) {
  console.log(helpText);
  process.exit(0);
}

// Extract port override
let explicitPort: number | undefined;
let portIndex = args.indexOf('--port');
if (portIndex >= 0 && args.length > portIndex + 1) {
  explicitPort = parseInt(args[portIndex + 1]!);
  args.splice(portIndex, 2);
}

const command = args[0] as string | undefined;
if (!command || !commandHelpText[command]) {
  console.error(`Unknown command: ${command || 'none'}\n`);
  console.log(helpText);
  process.exit(1);
}

// Handle command-level help
if (args.includes('-h') || args.includes('--help')) {
  console.log(commandHelpText[command!]);
  process.exit(0);
}

async function callMcp(method: string, argumentsObj: any) {
  let port: number;
  try {
    // If not explicit, dynamically detect the local unity port using port locator logic
    port = explicitPort || readPort();
  } catch (err: any) {
    console.error(err.message);
    process.exit(1);
  }

  const payload = {
    jsonrpc: "2.0",
    id: 1,
    method: "tools/call",
    params: {
      name: method,
      arguments: argumentsObj
    }
  };

  try {
    const response = await fetch(`http://localhost:${port}/mcp`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Accept": "application/json"
      },
      body: JSON.stringify(payload)
    });

    if (response.status === 202) return;

    const text = await response.text();
    let data;
    try {
      data = JSON.parse(text);
    } catch {
      console.log(text);
      process.exit(0);
    }

    if (data.error) {
      console.error(`Error: ${data.error.message || JSON.stringify(data.error)}`);
      process.exit(1);
    }

    const content = data.result?.content || [];
    if (content.length === 0) {
      console.log(JSON.stringify(data.result));
      return;
    }

    for (const c of content) {
      if (c.type === 'text') {
        console.log(typeof c.text === 'string' ? c.text : JSON.stringify(c.text));
      } else {
        console.log(JSON.stringify([c]));
      }
    }
  } catch (err: any) {
    console.error(`Failed to connect to Unity MCP at port ${port}: ${err.message}`);
    process.exit(1);
  }
}

const payload: any = {};

if (command === 'invoke_dynamic') {
  let method = "";
  let methodArgs = "{}";
  for(let i=1; i<args.length; i++) {
    if (args[i] === '--method' && args[i+1]) { method = args[i+1]!; i++; }
    else if (args[i] === '--args' && args[i+1]) { methodArgs = args[i+1]!; i++; }
  }
  if (!method) { console.error(commandHelpText[command!]); process.exit(1); }
  payload.action = "call_method";
  payload.method = method;
  payload.args = methodArgs;
  await callMcp(command, payload);

} else if (command === 'get_scene_tree') {
  payload.maxDepth = -1;
  payload.includeInactive = true;
  payload.includePath = true;
  payload.includeTransform = false;
  
  for(let i=1; i<args.length; i++) {
    if (args[i] === '--max-depth' && args[i+1]) { payload.maxDepth = parseInt(args[i+1]!); i++; }
    else if (args[i] === '--no-include-inactive') { payload.includeInactive = false; }
    else if (args[i] === '--no-include-path') { payload.includePath = false; }
    else if (args[i] === '--include-transform') { payload.includeTransform = true; }
    else if (args[i] === '--component-filter' && args[i+1]) { payload.componentFilter = args[i+1]; i++; }
    else if (args[i] === '--name-filter' && args[i+1]) { payload.nameFilter = args[i+1]; i++; }
    else if (args[i] === '--scene-index' && args[i+1]) { payload.sceneIndex = parseInt(args[i+1]!); i++; }
  }
  await callMcp(command, payload);

} else if (command === 'manage_editor') {
  const action = args[1];
  if (!action || !['play','stop','pause'].includes(action)) { console.error(commandHelpText[command!]); process.exit(1); }
  await callMcp(command!, { action });

} else if (command === 'manage_scene') {
  const action = args[1];
  if (!action) { console.error(commandHelpText[command!]); process.exit(1); }
  payload.action = action;
  payload.superSize = 1;
  for(let i=2; i<args.length; i++) {
    if (args[i] === '--super-size' && args[i+1]) { payload.superSize = parseInt(args[i+1]!); i++; }
  }
  await callMcp(command!, payload);

} else if (command === 'refresh_unity') {
  await callMcp(command, {});

} else if (command === 'read_console') {
  payload.count = 200;
  for(let i=1; i<args.length; i++) {
    if (args[i] === '--count' && args[i+1]) { payload.count = parseInt(args[i+1]!); i++; }
  }
  await callMcp(command!, payload);
}
