using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Composition-root layout guard (Phase 1–2 refactor).
    ///
    /// ตรวจ "กฎการวางของ" 2 ระดับ:
    ///   - ระดับซอร์ส (regex) ที่นี่ — กันชื่อ type หลุดไปอยู่ไฟล์ผิด
    ///   - ระดับ registration จริง: CompositionRootContainerTests (เรียก installer
    ///     ตัวจริงบน ContainerBuilder + Exists) และเทสต์ฉากด้านล่างเปิดฉากจริง
    ///     ผ่าน EditorSceneManager แล้วหา component
    ///
    /// build GameLifetimeScope จริงจะเปิด MessagePipe TCP interprocess listener
    /// (port 3215) ซึ่งไม่ควรเกิดใน EditMode test — จึงไม่ Build ที่นี่
    ///
    /// สิ่งที่ต้องไม่มีวัน regress คือ "กฎการวางของ" ซึ่งเขียนตรวจได้ตรง ๆ:
    /// บริการที่ผูกกับฉาก (rig ports + กล้อง/overlay/backdrop) ต้องถูก register
    /// **ที่ SectSceneLifetimeScope เท่านั้น** — เคย register ที่ root ซ้ำด้วย ทำให้
    /// entry point ทุกตัวเกิด 2 instance ต่อการโหลดฉาก (กล้อง/backdrop ทำงานและ log
    /// ซ้ำ, pan/zoom ถูกประมวลผลสองรอบ)
    /// </summary>
    public class CompositionRootTests
    {
        private const string ChildScopePath = "Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs";
        private const string InstallersDir = "Scripts/Core/Installers";
        private const string GameLifetimeScopePath = "Scripts/Core/GameLifetimeScope.cs";

        /// <summary>บริการที่ผูกกับฉากเกมเพลย์ — child scope เท่านั้น</summary>
        private static readonly string[] SceneBoundTypes =
        {
            "CameraRigController",
            "GridOverlayRenderer",
            "TerrainBackdropRenderer",
            "IRigMessageBus",
            "ICameraRigEnvironment",
            "ICellSpriteMetrics",
            "IMainThreadQueue",
            "IRigClock",
        };

        /// <summary>Register&lt;T&gt; / Register&lt;TImpl, TConcrete&gt; / RegisterEntryPoint&lt;T&gt; / RegisterInstance&lt;T&gt;</summary>
        private static readonly Regex RegistrationRegex =
            new Regex(@"Register(?:EntryPoint|Instance)?\s*<\s*([^>]+?)\s*>", RegexOptions.Compiled);

        // ---- ชั้นที่ถูก: child scope ต้อง register ครบ ----

        [Test]
        public void GameplaySceneScope_RegistersEverySceneBoundService()
        {
            HashSet<string> registered = RegisteredTypesIn(ChildScopePath);

            foreach (string type in SceneBoundTypes)
            {
                Assert.IsTrue(registered.Contains(type),
                    "SectSceneLifetimeScope must register '" + type + "' — บริการนี้ผูกกับฉาก");
            }
        }

        [Test]
        public void GameplaySceneScope_IsAScopeTypeWeCanTrust()
        {
            string full = Path.Combine(Application.dataPath, ChildScopePath);
            Assert.IsTrue(File.Exists(full), "missing child scope source: " + ChildScopePath);

            string source = StripComments(File.ReadAllText(full));
            StringAssert.Contains("class SectSceneLifetimeScope : LifetimeScope", source,
                "child scope must derive from VContainer's LifetimeScope");
        }

        // ---- ชั้นที่ผิด: root ต้องไม่ register ของที่ผูกกับฉาก ----

        [Test]
        public void RootInstallersAndGameLifetimeScope_DoNotRegisterSceneBoundServices()
        {
            foreach (string file in RootRegistrationSources())
            {
                HashSet<string> registered = RegisteredTypesIn(file);

                foreach (string type in SceneBoundTypes)
                {
                    Assert.IsFalse(registered.Contains(type),
                        file + " ต้องไม่ register '" + type + "' — ของที่ผูกกับฉากอยู่ที่ " +
                        "SectSceneLifetimeScope เท่านั้น (สำเนาที่ root ทำให้ entry point " +
                        "เกิด 2 instance ต่อการโหลดฉาก)");
                }
            }
        }

        [Test]
        public void RootRegistrationSources_AreWhereWeThinkTheyAre()
        {
            List<string> files = RootRegistrationSources();

            Assert.IsTrue(files.Contains(GameLifetimeScopePath), "root scope source must be scanned");
            Assert.IsTrue(files.Count > 5,
                "expected GameLifetimeScope + the 5 installer sources, found " + files.Count);
        }

        // ---- ฉากเกมเพลย์ต้องมี child scope อยู่จริง ----

        [Test]
        public void GameplaySceneAsset_ContainsTheChildScopeComponent()
        {
            // เปิดฉากจริงแล้วตรวจ component จริง — ไม่ใช่แค่ grep GUID ในไฟล์ .unity
            // (เคสที่ GUID ยังอยู่แต่ component ถูกถอดออก จะไม่มีวันรอดเทสต์นี้)
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(SceneNames.SectAssetPath, OpenSceneMode.Single);

                Assert.IsTrue(scene.IsValid(), "เปิดฉากไม่ได้: " + SceneNames.SectAssetPath);
                Assert.AreEqual(SceneNames.Sect, scene.name,
                    "ชื่อฉากต้องเป็น SectScene");

                List<SectSceneLifetimeScope> scopes = ChildScopesIn(scene);
                Assert.AreEqual(1, scopes.Count,
                    SceneNames.SectAssetPath + " ต้องมี component SectSceneLifetimeScope อยู่ " +
                    "1 ตัวบน object ที่ root ของฉาก — ไม่มี = rig ports/กล้อง/overlay/backdrop " +
                    "ไม่ถูกสร้างตอนเล่นฉากนี้");
            }
            finally
            {
                // setup ว่างได้ในบริบท test runner — Restore ด้วยอาร์เรย์ว่างจะโยน
                // ArgumentException ("No loaded scene found")
                if (previous != null && previous.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previous);
                }
            }
        }

        [Test]
        public void RenamedScene_OldAssetPathIsGone_NewOneLoads()
        {
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/TestGameplayScene.unity"),
                "ชื่อ asset เดิมต้องไม่หลงเหลือ (rename ต้องผ่าน AssetDatabase)");

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneNames.SectAssetPath));
        }

        // ---- helpers ----

        private static List<string> RootRegistrationSources()
        {
            var files = new List<string> { GameLifetimeScopePath };
            string dir = Path.Combine(Application.dataPath, InstallersDir);

            Assert.IsTrue(Directory.Exists(dir), "missing installers folder: " + InstallersDir);

            foreach (string full in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                files.Add(InstallersDir + "/" + Path.GetFileName(full));
            }

            return files;
        }

        private static HashSet<string> RegisteredTypesIn(string assetRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetRelativePath);
            Assert.IsTrue(File.Exists(full), "missing source file: " + assetRelativePath);

            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in RegistrationRegex.Matches(StripComments(File.ReadAllText(full))))
            {
                foreach (string part in match.Groups[1].Value.Split(','))
                {
                    string name = part.Trim();
                    int lastDot = name.LastIndexOf('.');
                    if (lastDot >= 0) name = name.Substring(lastDot + 1);
                    if (name.Length > 0) types.Add(name);
                }
            }

            return types;
        }

        /// <summary>ตัด // comment (รวม /// XML doc) — กันชื่อ type ในคอมเมนต์ไม่ให้ปนผลสแกน</summary>
        private static string StripComments(string source)
        {
            return Regex.Replace(source, @"//[^\n]*", string.Empty);
        }

        /// <summary>หา SectSceneLifetimeScope ทุกตัวบน object ที่ root ของฉาก (รวม inactive)</summary>
        private static List<SectSceneLifetimeScope> ChildScopesIn(Scene scene)
        {
            var found = new List<SectSceneLifetimeScope>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<SectSceneLifetimeScope>(true));
            }

            return found;
        }
    }
}
