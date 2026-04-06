using System.Collections.Generic;
using System.Text;

namespace LambdaLabs.UnityRepl.Editor
{
    /// <summary>
    /// Registry for unity-agent-* packages to declare their available tools.
    /// Registered docs are written to Temp/UnityReplIpc/extensions.md so the
    /// external skill layer can include them in agent context.
    /// </summary>
    public static class ReplExtensionRegistry
    {
        private static readonly List<ExtensionEntry> s_entries = new List<ExtensionEntry>();

        public static void Register(string packageName, string skillDoc)
        {
            s_entries.RemoveAll(e => e.PackageName == packageName);
            s_entries.Add(new ExtensionEntry(packageName, skillDoc));
            FlushContextFile();
        }

        private static void FlushContextFile()
        {
            var sb = new StringBuilder();
            foreach (var e in s_entries)
            {
                sb.AppendLine($"<!-- {e.PackageName} -->");
                sb.AppendLine(e.SkillDoc);
                sb.AppendLine();
            }
            var dir = "Temp/UnityReplIpc";
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText($"{dir}/extensions.md", sb.ToString());
        }

        private sealed class ExtensionEntry
        {
            public ExtensionEntry(string packageName, string skillDoc)
            {
                PackageName = packageName;
                SkillDoc = skillDoc;
            }

            public string PackageName { get; }
            public string SkillDoc { get; }
        }
    }
}
