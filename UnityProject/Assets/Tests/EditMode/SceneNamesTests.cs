using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// SceneNames ↔ Build Settings ↔ SceneLoader guard (Phase 3 rename).
    ///
    /// ชื่อฉากคือสัญญาระหว่างโค้ดกับ asset: SceneLoader โหลดตามชื่อ, rig/overlay/
    /// backdrop กรองด้วยชื่อฉาก และ additive build ต้องมีฉากนั้นใน Build Settings
    /// (ไม่งั้น LoadSceneAsync ล้มตอน build). เทสต์ชุดนี้ล็อกให้ค่าคงที่ทั้งสามฝั่ง
    /// ตรงกันเสมอ และกันการ rename ฉากแบบหลุดทั้ง pipeline.
    /// </summary>
    public class SceneNamesTests
    {
        private const string SwapTestScenePath = "Assets/Scenes/TestGameplayScene2.unity";

        [Test]
        public void Sect_IsNotEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(SceneNames.Sect),
                "SceneNames.Sect ต้องมีชื่อฉากจริง");
        }

        [Test]
        public void SectAssetPath_IsDerivedFromSect()
        {
            Assert.AreEqual("Assets/Scenes/" + SceneNames.Sect + ".unity", SceneNames.SectAssetPath,
                "SectAssetPath ต้อง derive จาก Sect — กันสองค่าตกหล่นกัน");
            Assert.AreEqual(SceneNames.Sect,
                Path.GetFileNameWithoutExtension(SceneNames.SectAssetPath),
                "ชื่อไฟล์ฉากต้องเท่ากับชื่อใน SceneManager (LoadSceneAsync อ้างชื่อไฟล์)");
        }

        [Test]
        public void SectAssetPath_PointsToAnExistingSceneAsset()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneNames.SectAssetPath);

            Assert.IsNotNull(scene, "ไม่พบฉากที่ SceneNames.SectAssetPath: " + SceneNames.SectAssetPath);
            Assert.AreEqual(SceneNames.Sect, scene.name);
        }

        [Test]
        public void SectAssetPath_IsAnEnabledBuildSettingsScene()
        {
            var entry = EditorBuildSettings.scenes
                .FirstOrDefault(s => s.path == SceneNames.SectAssetPath);

            Assert.IsNotNull(entry,
                "ฉากเกมเพลย์ต้องอยู่ใน Build Settings — additive load จะล้มตอน build ถ้าไม่มี: " +
                SceneNames.SectAssetPath);
            Assert.IsTrue(entry.enabled, SceneNames.SectAssetPath + " ต้อง enabled");

            string assetGuid = AssetDatabase.AssetPathToGUID(SceneNames.SectAssetPath);
            Assert.IsFalse(string.IsNullOrEmpty(assetGuid), "asset ต้องมี GUID");
            // entry.guid เป็น UnityEditor.GUID (struct) — เทียบเป็นข้อความ
            Assert.AreEqual(assetGuid, entry.guid.ToString(),
                "GUID ใน Build Settings ต้องตรงกับ asset (rename ต้องผ่าน AssetDatabase เท่านั้น)");
        }

        [Test]
        public void SceneLoader_DefaultGameplayScene_IsTheSectScene()
        {
            Assert.AreEqual(SceneNames.Sect, SceneLoader.DefaultGameplayScene,
                "auto-load ตอนเริ่มเกมต้องโหลดฉากเกมเพลย์ (ไม่ใช่ชื่อที่ถูกลบไปแล้ว)");
        }

        [Test]
        public void SwapTestScene_KeepsItsOwnName()
        {
            Assert.IsTrue(File.Exists(ProjectPath(SwapTestScenePath)),
                "ฉากสำหรับ verify การสลับฉากต้องคงอยู่: " + SwapTestScenePath);
            Assert.AreNotEqual(SceneNames.Sect, Path.GetFileNameWithoutExtension(SwapTestScenePath),
                "ฉากทดสอบสลับฉากต้องไม่ถูก rename ทับชื่อฉากเกมเพลย์");
        }

        private static string ProjectPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
