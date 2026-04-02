namespace NativeMcp.Editor.Helpers
{
    /// <summary>
    /// Centralized constants for SessionState and EditorPrefs keys used across the plugin.
    /// </summary>
    internal static class NativeMcpKeys
    {
        // SessionState keys (per-session, lost on editor restart)
        public const string PendingTestRun = "NativeMcp_PendingTestRun";
        public const string PendingPlayForFrames = "NativeMcp_PendingPlayForFrames";
        public const string LastPort = "NativeMcp_LastPort";

        // EditorPrefs keys (persistent across sessions)
        public const string AutoStart = "NativeMcp_AutoStart";
        public const string ResumeAfterReload = "NativeMcp_ResumeAfterReload";
    }
}
