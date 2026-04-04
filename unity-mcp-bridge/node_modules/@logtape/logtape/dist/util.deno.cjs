
//#region src/util.deno.ts
function inspect(obj, options) {
	if ("Deno" in globalThis) return Deno.inspect(obj, {
		colors: options?.colors,
		depth: options?.depth ?? void 0,
		compact: options?.compact ?? true
	});
	else {
		const indent = options?.compact === true ? void 0 : 2;
		return JSON.stringify(obj, null, indent);
	}
}

//#endregion
exports.inspect = inspect;