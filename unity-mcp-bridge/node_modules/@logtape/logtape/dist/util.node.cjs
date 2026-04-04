const require_rolldown_runtime = require('./_virtual/rolldown_runtime.cjs');
const node_util = require_rolldown_runtime.__toESM(require("node:util"));

//#region src/util.node.ts
function inspect(obj, options) {
	return node_util.default.inspect(obj, options);
}

//#endregion
exports.inspect = inspect;