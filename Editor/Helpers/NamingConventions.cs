using System.Text;
using System.Text.RegularExpressions;

namespace NativeMcp.Editor.Helpers
{
    /// <summary>
    /// Shared naming convention utilities (snake_case, camelCase, etc.)
    /// used across discovery, registration, and batch execution.
    /// </summary>
    public static class NamingConventions
    {
        /// <summary>
        /// Convert PascalCase or camelCase to snake_case.
        /// E.g. "ManageScene" → "manage_scene", "HTTPClient" → "http_client"
        /// </summary>
        public static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Insert underscore between a lowercase/digit and an uppercase letter
            var s1 = Regex.Replace(input, "(.)([A-Z][a-z]+)", "$1_$2");
            // Insert underscore between a lowercase/digit and an uppercase letter (for acronyms)
            var s2 = Regex.Replace(s1, "([a-z0-9])([A-Z])", "$1_$2");
            return s2.ToLower();
        }

        /// <summary>
        /// Convert snake_case to camelCase.
        /// E.g. "search_method" → "searchMethod"
        /// </summary>
        public static string ToCamelCase(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf('_') < 0)
            {
                return key;
            }

            var parts = key.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return key;
            }

            var builder = new StringBuilder(parts[0]);
            for (int i = 1; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }

            return builder.ToString();
        }
    }
}
