using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using Xianxia.Sect.Installers;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// DI constructor-selection guard (P12A follow-up).
    ///
    /// ที่มา: trap นี้ทำให้ build scope ล้มมาแล้ว 2 ครั้ง — <c>DecisionExecutor</c> ที่ได้
    /// parameter <c>ISessionGate</c> เพิ่ม (hand-built container ในเทสต์ไม่เห็น แต่ popup ตาย)
    /// และ <c>SaveSlotRepository</c> ที่มี ctor <c>(fileSystem, paths, int maxSaveFileBytes)</c>
    /// ไว้ให้เทสต์ (เทสต์ผ่านหมด แต่เข้า Play mode ไม่ได้)
    ///
    /// กติกาของ VContainer (Runtime/Internal/TypeAnalyzer.cs): ถ้ามี ctor ที่ติด [Inject]
    /// เดียว → เลือกตัวนั้น ไม่งั้นเลือก ctor ที่มี parameters มากที่สุดจาก
    /// <c>DeclaredOnly | Instance | Public | NonPublic</c> แล้วพยายาม resolve ทุกตัว
    /// — ctor ที่เป็น test seam (รับ Int32/String/Struct) จึง "ชนะ" ctor production
    /// แล้วประกอบร่างไม่ผ่าน
    ///
    /// เทสต์นี้ถาม <see cref="IContainerBuilder.Exists"/> จาก installer ตัวจริงว่ามี type ใด
    /// ถูก register บ้าง แล้วหา type ที่ VContainer จะต้อง "เลือก ctor" (มี ctor > 1 ตัว)
    /// — รายการนี้ต้องตรงกับ allowlist ที่มีเหตุผลกำกับ ถ้าเพิ่ม type ใหม่ที่มี ctor หลายตัว
    /// เทสต์จะ fail พร้อมชื่อ แล้วให้ผู้เขียนไปเลือกเองว่า pin ด้วย factory หรือปล่อยให้ resolve
    ///
    /// ข้อจำกัด (ตรงไปตรงมา): reflection มองไม่เห็นว่า registration เป็น factory หรือไม่
    /// จึงยืนยันได้แค่ว่า type ที่มี ctor หลายตัวคือ "รายการที่รู้ตัวแล้ว" และสำหรับตัวที่
    /// พึ่งการ resolve (ไม่ได้ pin) จะตรวจให้ว่า parameter ของ ctor ที่ถูกเลือก register ครบ
    /// </summary>
    public class DiConstructorSelectionTests
    {
        /// <summary>
        /// Type ที่ register ไว้และมี ctor มากกว่า 1 ตัว — ต้องเป็นรายการที่รู้ตัวและมีเหตุผล
        /// เท่านั้น (ชื่อ + เหตุผลที่ปลอดภัย)
        /// </summary>
        private static readonly Dictionary<string, string> AllowedMultiCtorTypes = new Dictionary<string, string>
        {
            // pin ctor ผ่าน factory ที่ composition root (BuildingInstaller) — VContainer
            // ไม่ได้เลือก ctor เอง จึงไม่แตะ Int32
            { "BuildingGrid", "factory-pinned in BuildingInstaller (grid size, not injected)" },
            // pin ctor ผ่าน factory ที่ composition root (GameplayInstaller) — ctor ที่รับ
            // maxSaveFileBytes เหลือไว้ให้เทสต์เท่านั้น
            { "SaveSlotRepository", "factory-pinned in GameplayInstaller (file-size ceiling, not injected)" },
            // ไม่ได้ pin: ctor ที่มี parameters มากที่สุดรับเฉพาะ interface ที่ register ไว้
            // (ISectStateProvider + ISessionRestoreAuthority) จึง resolve ผ่าน
            { "ViewerMembershipPersistenceSystem", "widest ctor takes only registered interfaces" },
        };

        // RegisterVisualSystem เขียน VisualRuntimeConfig (static) ค่า production —
        // snapshot/restore เหมือน CompositionRootContainerTests
        private bool _spriteSheetEnabled;
        private bool _spineActivationRequested;
        private string _spineSkeletonResourcePath;
        private bool _devSpineOverride;

        // RegisterUI ต้องการ instance จริง (VContainer RegisterInstance(null) โยน NRE)
        private GameObject _uiRootGo;
        private UIRoot _uiRoot;
        private UIPanelCatalog _uiPanelCatalog;

        [SetUp]
        public void SetUp()
        {
            VisualRuntimeConfig cfg = VisualRuntimeConfig.Instance;
            _spriteSheetEnabled = cfg.SpriteSheetEnabled;
            _spineActivationRequested = cfg.SpineActivationRequested;
            _spineSkeletonResourcePath = cfg.SpineSkeletonResourcePath;
            _devSpineOverride = cfg.DevSpineOverride;

            _uiRootGo = new GameObject("DiCtorSelectionTest_UIRoot");
            _uiRoot = _uiRootGo.AddComponent<UIRoot>();
            _uiPanelCatalog = ScriptableObject.CreateInstance<UIPanelCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            VisualRuntimeConfig cfg = VisualRuntimeConfig.Instance;
            cfg.SpriteSheetEnabled = _spriteSheetEnabled;
            cfg.SpineActivationRequested = _spineActivationRequested;
            cfg.SpineSkeletonResourcePath = _spineSkeletonResourcePath;
            cfg.DevSpineOverride = _devSpineOverride;

            if (_uiRootGo != null) UnityEngine.Object.DestroyImmediate(_uiRootGo);
            if (_uiPanelCatalog != null) UnityEngine.Object.DestroyImmediate(_uiPanelCatalog);
        }

        // ---- the guard ----

        [Test]
        public void RegisteredTypes_WithMultipleConstructors_AreOnlyTheDocumentedOnes()
        {
            IContainerBuilder builder = BuildRootRegistrationSet();

            var offenders = RegisteredConcreteTypes(builder)
                .Where(HasMoreThanOneConstructor)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var unexpected = offenders.Where(n => !AllowedMultiCtorTypes.ContainsKey(n)).ToList();
            CollectionAssert.IsEmpty(unexpected,
                "Type เหล่านี้ถูก register และมี ctor มากกว่า 1 ตัว — VContainer จะเลือก ctor ที่มี " +
                "parameters มากที่สุดแล้วพยายาม resolve ทุกตัว (ctor ที่เป็น test seam จึงชนะ) " +
                "ถ้าไม่ได้ตั้งใจให้เป็นแบบนั้น ให้ pin ctor ด้วย factory ที่ composition root " +
                "เหมือน BuildingGrid/SaveSlotRepository ถ้าปลอดภัยจริง ให้เพิ่มเข้า AllowedMultiCtorTypes " +
                "พร้อมเหตุผล. พบ: " + string.Join(", ", unexpected));

            var stale = AllowedMultiCtorTypes.Keys.Where(n => !offenders.Contains(n)).ToList();
            CollectionAssert.IsEmpty(stale,
                "Allowlist ยังลิสต์ type ที่ไม่มี ctor หลายตัวแล้ว (หรือไม่ถูก register) — " +
                "เอาออกจาก AllowedMultiCtorTypes: " + string.Join(", ", stale));
        }

        [Test]
        public void MultiCtorType_NotPinnedByAFactory_ResolvesEveryParameterOfItsChosenConstructor()
        {
            // ตัวที่พึ่งการ resolve (ไม่มี factory) ต้องมี parameter ทุกตัว register ไว้จริง
            // — ไม่งั้นก็ล้มตอน build scope เหมือนกัน แค่คนละทาง
            IContainerBuilder builder = BuildRootRegistrationSet();

            Type target = RegisteredConcreteTypes(builder).Single(t => t.Name == "ViewerMembershipPersistenceSystem");
            ConstructorInfo chosen = ChosenConstructor(target);

            Assert.IsNotNull(chosen, "ViewerMembershipPersistenceSystem ต้องมี ctor ที่ VContainer เลือกได้");

            foreach (ParameterInfo p in chosen.GetParameters())
            {
                Assert.IsTrue(IsRegistered(builder, p.ParameterType),
                    "ctor ที่ VContainer เลือกต้อง resolve '" + p.ParameterType.Name + "' ได้ " +
                    "แต่ type นี้ไม่ได้ register ไว้ที่ root scope");
            }
        }

        // ---- helpers ----

        /// <summary>ชุด registration ของ root scope — 4 installer ที่ GameLifetimeScope.Configure เรียก
        /// (ข้าม RegisterInterprocess: TCP transport ไม่เกี่ยวกับ ctor selection และเราไม่ Build อยู่แล้ว)</summary>
        private IContainerBuilder BuildRootRegistrationSet()
        {
            var builder = new ContainerBuilder();
            builder.RegisterBuildingSystem();
            builder.RegisterVisualSystem();
            builder.RegisterUI(_uiRoot, _uiPanelCatalog);
            builder.RegisterGameplaySystems();
            return builder;
        }

        /// <summary>Every concrete (non-abstract) type we own that the real installers registered.</summary>
        private static IEnumerable<Type> RegisteredConcreteTypes(IContainerBuilder builder)
        {
            return CandidateTypes()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && !t.IsInterface)
                .Where(t => builder.Exists(t, includeInterfaceTypes: false));
        }

        /// <summary>
        /// Types ใน namespace ของเราเอง (`Xianxia.*`). โปรเจกต์ใช้ asmdef (runtime assembly ชื่อ
        /// `Xianxia.Sect.Runtime` ไม่ใช่ Assembly-CSharp) และการ filter ด้วย namespace ครอบ
        /// type ที่ installer register ได้จริงโดยไม่ต้องรู้ชื่อ assembly
        /// </summary>
        private static IEnumerable<Type> CandidateTypes()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic);

            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (Type type in types)
                    if (type.Namespace != null && type.Namespace.StartsWith("Xianxia", StringComparison.Ordinal))
                        yield return type;
            }
        }

        /// <summary>VContainer's own scan shape: declared-only instance ctors, public + non-public.</summary>
        private static bool HasMoreThanOneConstructor(Type type)
        {
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance |
                                       BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetConstructors(flags).Length > 1;
        }

        /// <summary>
        /// Same rule as VContainer's TypeAnalyzer: a single [Inject] ctor wins, otherwise the
        /// constructor with the most parameters.
        /// </summary>
        private static ConstructorInfo ChosenConstructor(Type type)
        {
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance |
                                       BindingFlags.Public | BindingFlags.NonPublic;

            ConstructorInfo[] ctors = type.GetConstructors(flags);
            ConstructorInfo[] annotated = ctors
                .Where(c => c.IsDefined(typeof(InjectAttribute), false))
                .ToArray();

            if (annotated.Length == 1) return annotated[0];
            if (annotated.Length > 1) return null; // VContainer itself throws for this

            return ctors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        }

        private static bool IsRegistered(IContainerBuilder builder, Type type)
            => builder.Exists(type, includeInterfaceTypes: true);
    }
}
