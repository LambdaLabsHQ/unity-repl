using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LambdaLabs.UnityRepl.Editor
{
    /// <summary>
    /// Dynamic two-tier registry for unity-agent-* ability packages.
    /// Packages call <see cref="Register"/> from [InitializeOnLoad] with a
    /// structured manifest. Agents discover in two tiers:
    ///   Tier 1: ListAbilities()  → (name, description) summary
    ///   Tier 2: GetAbility(name) → full AbilityManifest (incl. markdown Doc)
    /// Disk mirror is written to Temp/UnityReplIpc/abilities/ for non-REPL consumers.
    /// </summary>
    public static class ReplAbilityRegistry
    {
        private static readonly List<AbilityManifest> s_entries = new List<AbilityManifest>();

        public static void Register(AbilityManifest ability)
        {
            if (ability == null || string.IsNullOrEmpty(ability.Name)) return;
            s_entries.RemoveAll(e => e.Name == ability.Name);
            s_entries.Add(ability);
            FlushDisk();
        }

        public static IReadOnlyList<AbilitySummary> ListAbilities()
        {
            var result = new List<AbilitySummary>(s_entries.Count);
            foreach (var e in s_entries)
                result.Add(new AbilitySummary(e.Name, e.Description));
            return result;
        }

        public static AbilityManifest GetAbility(string name)
        {
            foreach (var e in s_entries)
                if (e.Name == name) return e;
            return null;
        }

        private static void FlushDisk()
        {
            try
            {
                var dir = "Temp/UnityReplIpc/abilities";
                Directory.CreateDirectory(dir);

                foreach (var e in s_entries)
                    File.WriteAllText(Path.Combine(dir, e.Name + ".md"), e.Doc ?? "");

                var sb = new StringBuilder();
                sb.Append('[');
                for (int i = 0; i < s_entries.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var e = s_entries[i];
                    sb.Append("{\"name\":").Append(JsonString(e.Name))
                      .Append(",\"description\":").Append(JsonString(e.Description ?? string.Empty));
                    if (!string.IsNullOrEmpty(e.Version))
                        sb.Append(",\"version\":").Append(JsonString(e.Version));
                    sb.Append('}');
                }
                sb.Append(']');
                File.WriteAllText(Path.Combine(dir, "index.json"), sb.ToString());

                var legacy = "Temp/UnityReplIpc/extensions.md";
                if (File.Exists(legacy)) File.Delete(legacy);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ReplAbilityRegistry] Flush failed: {ex.Message}");
            }
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    public sealed class AbilityManifest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Doc { get; set; }
        public string Version { get; set; }
    }

    public sealed class AbilitySummary
    {
        public AbilitySummary(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }

        public override string ToString() => $"{Name} — {Description}";
    }
}
