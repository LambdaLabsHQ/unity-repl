//#region src/level.d.ts
declare const logLevels: readonly ["trace", "debug", "info", "warning", "error", "fatal"];
/**
 * The severity level of a {@link LogRecord}.
 */
type LogLevel = typeof logLevels[number];
/**
 * Lists all available log levels with the order of their severity.
 * The `"trace"` level goes first, and the `"fatal"` level goes last.
 * @returns A new copy of the array of log levels.
 * @since 1.0.0
 */
declare function getLogLevels(): readonly LogLevel[];
/**
 * Parses a log level from a string.
 *
 * @param level The log level as a string.  This is case-insensitive.
 * @returns The log level.
 * @throws {TypeError} If the log level is invalid.
 */
declare function parseLogLevel(level: string): LogLevel;
/**
 * Checks if a string is a valid log level.  This function can be used as
 * as a type guard to narrow the type of a string to a {@link LogLevel}.
 *
 * @param level The log level as a string.  This is case-sensitive.
 * @returns `true` if the string is a valid log level.
 */
declare function isLogLevel(level: string): level is LogLevel;
/**
 * Compares two log levels.
 * @param a The first log level.
 * @param b The second log level.
 * @returns A negative number if `a` is less than `b`, a positive number if `a`
 *          is greater than `b`, or zero if they are equal.
 * @since 0.8.0
 */
declare function compareLogLevel(a: LogLevel, b: LogLevel): number;
//#endregion
export { LogLevel, compareLogLevel, getLogLevels, isLogLevel, parseLogLevel };
//# sourceMappingURL=level.d.ts.map