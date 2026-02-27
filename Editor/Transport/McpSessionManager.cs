using System;
using System.Collections.Concurrent;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// Manages MCP session IDs. Sessions are created during initialization
    /// and validated on subsequent requests via the Mcp-Session-Id header.
    /// </summary>
    internal class McpSessionManager
    {
        private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

        private class SessionInfo
        {
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessedAt { get; set; }
        }

        /// <summary>
        /// Create a new session and return its ID.
        /// </summary>
        public string CreateSession()
        {
            string sessionId = Guid.NewGuid().ToString("N");
            _sessions[sessionId] = new SessionInfo
            {
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };
            return sessionId;
        }

        /// <summary>
        /// Validate a session ID. Returns true if the session exists.
        /// </summary>
        public bool ValidateSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            if (_sessions.TryGetValue(sessionId, out var info))
            {
                info.LastAccessedAt = DateTime.UtcNow;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Terminate a session by ID.
        /// </summary>
        public bool TerminateSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            return _sessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Clear all sessions (e.g., on domain reload).
        /// </summary>
        public void ClearAll()
        {
            _sessions.Clear();
        }
    }
}
