//#region src/util.ts
function inspect(obj, options) {
	const indent = options?.compact === true ? void 0 : 2;
	return JSON.stringify(obj, null, indent);
}

//#endregion
export { inspect };
//# sourceMappingURL=util.js.map