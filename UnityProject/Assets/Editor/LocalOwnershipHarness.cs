#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// P4 — LOCAL DEVELOPMENT-ONLY ownership harness.
    ///
    /// Purpose: exercise ownership-dependent behavior (TryAssignTask permission
    /// path, ownership labels) without any account-linking infrastructure.
    ///
    /// Gate (explicit dev configuration, disabled by default):
    ///   the watch loop only acts when a marker file exists AND its first line
    ///   is exactly "ENABLE_LOCAL_OWNERSHIP_HARNESS". Outside local testing the
    ///   file simply never exists, so the harness is inert. No menu, no runtime
    ///   UI, no MCP/bridge tool, no viewer command exposes ownership assignment.
    ///
    /// Protocol (same marker-file pattern as VisualSpikeRemoteControl):
    ///   write Library/ownership_command.txt:
    ///     ENABLE_LOCAL_OWNERSHIP_HARNESS
    ///     &lt;discipleId&gt; &lt;OwnerType&gt; [ownerId]
    ///   e.g. "d001 Viewer viewer_test_01" or "d001 Npc"
    ///   result lands in Library/ownership_result.txt.
    ///
    /// ⚠️ SYNTHETIC IDENTITIES: viewer_test_01 etc. are made-up local ids that
    /// satisfy the owner-id convention. They must NEVER enter production Twitch
    /// authentication — real accounts arrive only with P5B+ linking.
    /// All mutation goes through the REAL SectStateProvider.TrySetDiscipleOwner
    /// (Unity-side service) — this harness performs no direct state writes.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalOwnershipHarness
    {
        private const string CommandPath = "Library/ownership_command.txt";
        private const string ResultPath = "Library/ownership_result.txt";

        static LocalOwnershipHarness()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!File.Exists(CommandPath)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandPath);
                File.Delete(CommandPath);
            }
            catch (IOException)
            {
                return; // writer still mid-write; retry next tick
            }

            if (lines.Length == 0 || lines[0].Trim() != "ENABLE_LOCAL_OWNERSHIP_HARNESS")
            {
                WriteResult("rejected: missing ENABLE_LOCAL_OWNERSHIP_HARNESS gate line");
                return;
            }

            if (lines.Length < 2)
            {
                WriteResult("rejected: command line missing (expected: <discipleId> <OwnerType> [ownerId])");
                return;
            }

            var parts = lines[1].Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                WriteResult("rejected: expected <discipleId> <OwnerType> [ownerId]");
                return;
            }

            var provider = ResolveProvider();
            if (provider == null)
            {
                WriteResult("error: ISectStateProvider not resolvable (open the boot scene / play mode first)");
                return;
            }

            if (!Enum.TryParse<DiscipleOwnerType>(parts[1], true, out var ownerType) ||
                !Enum.IsDefined(typeof(DiscipleOwnerType), ownerType))
            {
                WriteResult("error: undefined DiscipleOwnerType '" + parts[1] + "' (valid: Npc|Player|Viewer)");
                return;
            }

            var ownerId = parts.Length >= 3 ? parts[2] : string.Empty;

            var success = provider.TrySetDiscipleOwner(parts[0], ownerType, ownerId, out var failReason);
            WriteResult(success
                ? $"ok: {parts[0]} -> {ownerType} '{ownerId}'"
                : $"rejected: {failReason}");
        }

        private static ISectStateProvider ResolveProvider()
        {
            // Root-scope injection is published by GameLifetimeScope; the harness
            // only reads through it — never constructs or mutates state directly.
            // (Direct typed reference — the runtime assembly is Xianxia.Sect.Runtime,
            // so a string-based Type.GetType(..., "Assembly-CSharp") would return null.)
            try
            {
                var container = GameLifetimeScope.Injector;
                return container != null
                    ? (ISectStateProvider)container.Resolve(typeof(ISectStateProvider))
                    : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LocalOwnershipHarness] resolve failed: " + ex.Message);
                return null;
            }
        }

        private static void WriteResult(string text)
        {
            try
            {
                File.WriteAllText(ResultPath, text + Environment.NewLine);
            }
            catch (IOException) { }
            Debug.Log("[LocalOwnershipHarness] " + text);
        }
    }
}
#endif
