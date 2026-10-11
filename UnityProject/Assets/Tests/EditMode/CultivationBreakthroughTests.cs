using System;
using System.Collections.Generic;
using MessagePack;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P14B — the explicit, validated breakthrough operation: ISectStateProvider.CanBreakthrough
    /// (read-only) + TryBreakthrough (one atomic mutation), its in-memory notification and the
    /// ข้อมูล tab's cultivation row / ทะลวงขั้น button.
    ///
    /// Covered here:
    ///   - success advances exactly one stage and resets progress to 0;
    ///   - every rejection (not ready, final stage, unknown/empty ids, unknown stage id,
    ///     non-finite progress, a different viewer, the SectMaster against an ACTIVE
    ///     viewer-owned disciple) changes nothing and publishes nothing;
    ///   - the SectMaster is allowed once the owner has been inactive for STRICTLY more than
    ///     the 10-minute window (fake clock), and a valid owner command refreshes that
    ///     requester's own activity the same way a task change does while a SectMaster
    ///     command does not;
    ///   - a double request applies once; rank/stamina/skill XP are untouched;
    ///   - CanBreakthrough agrees with TryBreakthrough in every state and is read-only;
    ///   - a committed stage survives a save → restore (MessagePack snapshot round trip);
    ///   - the message is Unity-only (never in Shared/, never on the interprocess broker) while
    ///     the OPERATION is bridged: exactly one write pair + tool, carrying SECT_MASTER through
    ///     the same permission authority, so the AI GM gains no bypass a human does not have;
    ///   - the real DI container resolves the panel presenter with the new publisher
    ///     (Common Rule 16), and the generated prefab really carries the new row/button.
    ///
    /// Same provider construction / publisher pattern as CultivationProgressTests.
    /// </summary>
    public class CultivationBreakthroughTests
    {
        private const string PanelPrefabPath = "Assets/Prefabs/UI/DiscipleDetailPanel.prefab";
        private const float Tol = 1e-4f;
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);

        private MutableClock _clock;
        private SectStateProvider _provider;
        private BufferPublisher<DiscipleCultivationChangedMessage> _cultivationChanged;

        // ---- minimal real view hierarchy for the UI cases ----
        private DiscipleDetailPresenter _presenter;
        private DiscipleDetailView _view;
        private GameObject _rootGo;
        private RectTransform _cultivationBar;
        private Image _cultivationFill;
        private TMP_Text _cultivationStageText, _cultivationProgressText, _breakthroughReasonText;
        private Button _breakthroughButton;
        private readonly List<Button> _tabButtons = new List<Button>();

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _cultivationChanged = new BufferPublisher<DiscipleCultivationChangedMessage>();
            _provider = NewProvider(_clock);
            _tabButtons.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _presenter?.Dispose();
            if (_rootGo != null) UnityEngine.Object.DestroyImmediate(_rootGo);
        }

        // ---------------------------------------------------------------- helpers

        private SectStateProvider NewProvider(IClock clock)
        {
            return new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>(),
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                clock,
                null,                 // P9A control-mode publisher (not exercised here)
                _cultivationChanged); // P14B
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d != null && d.DiscipleId == id) return d;
            return null;
        }

        private DiscipleState Find(SectStateProvider provider, string id)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
                if (d != null && d.DiscipleId == id) return d;
            return null;
        }

        private string Stage(string id) => Find(id).Attributes.CultivationStageId;
        private float Progress(string id) => Find(id).Attributes.CultivationProgress;

        private void SetCultivation(string id, string stageId, float progress)
        {
            var a = Find(id).Attributes;
            a.CultivationStageId = stageId;
            a.CultivationProgress = progress;
        }

        /// <summary>Put the disciple exactly at the first stage's requirement (the normal ready state).</summary>
        private void ReadyUp(string id)
            => SetCultivation(id, CultivationConfig.StageQiCondensation,
                              CultivationConfig.Requirement(CultivationConfig.StageQiCondensation));

        private void BindViewerOwner(string discipleId, string viewerId)
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner(discipleId, DiscipleOwnerType.Viewer, viewerId, out var reason),
                          reason);
        }

        private DateTime OwnerLastActive(string viewerId)
            => State.ViewerRegistry.Find(viewerId).LastActiveAtUtc;

        // =============================================== 1. the happy path

        [Test]
        public void TryBreakthrough_Ready_AdvancesExactlyOneStage_AndResetsProgress()
        {
            ReadyUp("d001");

            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"), "advances to the NEXT stage");
            Assert.AreEqual(0f, Progress("d001"), Tol, "progress resets for the new stage");
        }

        [Test]
        public void TryBreakthrough_PublishesOneMessage_WithOldAndNewStageIds()
        {
            ReadyUp("d001");

            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);

            Assert.AreEqual(1, _cultivationChanged.Messages.Count, "exactly one notification per commit");
            var message = _cultivationChanged.Messages[0];
            Assert.AreEqual("d001", message.DiscipleId);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, message.OldStageId);
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, message.NewStageId);
        }

        [Test]
        public void Breakthrough_ThenMeditationAccruesTowardTheNextStage()
        {
            ReadyUp("d001");
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);

            _provider.TickGathering(10f); // d001 starts on meditation → 1.0/s accrual

            Assert.AreEqual(10f, Progress("d001"), Tol, "accrual continues against the new stage's requirement");
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"), "and does not re-advance");
        }

        [Test]
        public void TryBreakthrough_ThenFinalStage_IsRejected()
        {
            ReadyUp("d001");
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out _));
            SetCultivation("d001", CultivationConfig.StageCoreFormation,
                           CultivationConfig.Requirement(CultivationConfig.StageCoreFormation));
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out _));
            Assert.AreEqual(CultivationConfig.StageNascentSoul, Stage("d001"));

            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var reason),
                           "the final stage has no next stage");
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(CultivationConfig.StageNascentSoul, Stage("d001"), "nothing changed");
        }

        // =============================================== 2. validation order / fail-closed

        [Test]
        public void TryBreakthrough_NotReady_IsRejected_AndChangesNothing()
        {
            var requirement = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);
            SetCultivation("d001", CultivationConfig.StageQiCondensation, requirement - 0.5f);

            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var reason),
                           "clearly short of the requirement must fail");
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
            Assert.AreEqual(requirement - 0.5f, Progress("d001"), Tol, "no partial mutation");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count, "a rejection publishes nothing");
        }

        [Test]
        public void TryBreakthrough_EpsilonTolerance_AcceptsTheExactRequirement()
        {
            var requirement = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);
            SetCultivation("d001", CultivationConfig.StageQiCondensation, requirement);

            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          "accrual clamps AT the requirement, so an exact match is the normal ready state: " + failReason);

            // …and the tolerance is tiny: one thousandth of the requirement is still short.
            ReadyUp("d002");
            SetCultivation("d002", CultivationConfig.StageQiCondensation, requirement - requirement * 0.001f);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d002", out _),
                           "the epsilon must not let a meaningfully unfinished stage through");
        }

        [Test]
        public void TryBreakthrough_EmptyRequesterOrDiscipleId_IsRejected()
        {
            ReadyUp("d001");

            Assert.IsFalse(_provider.TryBreakthrough(null, "d001", out var nullRequester));
            Assert.IsNotEmpty(nullRequester);
            Assert.IsFalse(_provider.TryBreakthrough("   ", "d001", out var blankRequester));
            Assert.IsNotEmpty(blankRequester);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "", out var emptyDisciple));
            Assert.IsNotEmpty(emptyDisciple);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, null, out var nullDisciple));
            Assert.IsNotEmpty(nullDisciple);

            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void TryBreakthrough_UnknownDisciple_IsRejected()
        {
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d999", out var reason));
            StringAssert.Contains("d999", reason);
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void TryBreakthrough_UnknownStageId_IsRejected()
        {
            ReadyUp("d001");
            SetCultivation("d001", "not_a_stage", 10f);

            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var reason));
            StringAssert.Contains("not_a_stage", reason);
            Assert.AreEqual("not_a_stage", Stage("d001"), "corruption is reported, never silently downgraded");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void TryBreakthrough_NonFiniteProgress_IsRejected()
        {
            ReadyUp("d001");
            Find("d001").Attributes.CultivationProgress = float.NaN;

            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var reason));
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
        }

        [Test]
        public void TryBreakthrough_InvalidRequesterIdentity_IsRejected()
        {
            ReadyUp("d001");

            // Same identity convention as task assignment (fail closed, no second policy).
            Assert.IsFalse(_provider.TryBreakthrough("not an identity", "d001", out var reason));
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
        }

        // =============================================== 3. permission (Hybrid, identical rules)

        [Test]
        public void TryBreakthrough_AnotherViewer_IsRejected()
        {
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");
            SetCultivation("d002", CultivationConfig.StageQiCondensation,
                           CultivationConfig.Requirement(CultivationConfig.StageQiCondensation));

            Assert.IsFalse(_provider.TryBreakthrough("viewer_test_02", "d001", out var reason),
                           "a viewer may not break through someone else's disciple");
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void TryBreakthrough_Owner_IsAllowed_AndRefreshesOnlyItsOwnActivity()
        {
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");
            var otherBefore = OwnerLastActive("viewer_test_01");

            _clock.UtcNow = T0.AddMinutes(20);
            Assert.IsTrue(_provider.TryBreakthrough("viewer_test_01", "d001", out var failReason), failReason);

            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"));
            Assert.AreEqual(_clock.UtcNow, OwnerLastActive("viewer_test_01"),
                            "a valid owner command refreshes the requester's own activity, like a task change");
            Assert.AreNotEqual(otherBefore, OwnerLastActive("viewer_test_01"));
        }

        [Test]
        public void TryBreakthrough_SectMaster_IsBlockedWhileTheOwnerIsActive_AndAllowedAfterTheWindow()
        {
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");

            // exactly 10 minutes is still "active" → protected (strictly more is required)
            _clock.UtcNow = T0.AddMinutes(10);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var blocked),
                           "the Hybrid protection window applies to breakthroughs too");
            Assert.IsNotEmpty(blocked);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"), "nothing changed while protected");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);

            _clock.UtcNow = T0.AddMinutes(11);
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var allowed),
                          allowed);
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"));
        }

        [Test]
        public void TryBreakthrough_SectMasterCommand_DoesNotRefreshViewerActivity()
        {
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");
            var boundAt = OwnerLastActive("viewer_test_01");

            _clock.UtcNow = T0.AddMinutes(11);
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);

            Assert.AreEqual(boundAt, OwnerLastActive("viewer_test_01"),
                            "SECT_MASTER has no membership record — its command refreshes nobody");
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"));
        }

        [Test]
        public void TryBreakthrough_InconsistentViewerMembership_FailsClosed()
        {
            ReadyUp("d001");
            // A Viewer-owned row with no membership record (fixture style, as in TaskObservabilityTests).
            Find("d001").OwnerType = DiscipleOwnerType.Viewer;
            Find("d001").OwnerId = "viewer_ghost";

            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var reason),
                           "a consistency error never grants permission");
            Assert.IsNotEmpty(reason);
            Assert.IsFalse(_provider.TryBreakthrough("viewer_ghost", "d001", out var ownerReason));
            Assert.IsNotEmpty(ownerReason);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"));
        }

        // =============================================== 4. independence / idempotence

        [Test]
        public void TryBreakthrough_DoubleRequest_AppliesOnce()
        {
            ReadyUp("d001");

            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var first), first);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var second),
                           "the second request finds the new stage at progress 0 — a valid rejection");
            Assert.IsNotEmpty(second);

            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"));
            Assert.AreEqual(0f, Progress("d001"), Tol);
            Assert.AreEqual(1, _cultivationChanged.Messages.Count, "one commit, one notification");
        }

        [Test]
        public void TryBreakthrough_LeavesRankStaminaSkillXpAndTaskUntouched()
        {
            var before = Find("d001");
            var rank = before.Rank;
            var stamina = before.Attributes.Stamina;
            var gatheringXp = DiscipleAttributes.GetSkillXp(before.Attributes, DiscipleAttributesConfig.CategoryGathering);
            var task = before.CurrentTask;

            ReadyUp("d001");
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);

            var after = Find("d001");
            Assert.AreEqual(rank, after.Rank, "DiscipleRank never changes on a breakthrough");
            Assert.AreEqual(stamina, after.Attributes.Stamina, Tol, "no stat bonus");
            Assert.AreEqual(gatheringXp, DiscipleAttributes.GetSkillXp(after.Attributes, DiscipleAttributesConfig.CategoryGathering));
            Assert.AreEqual(task, after.CurrentTask, "the current task is untouched");
        }

        [Test]
        public void TryBreakthrough_IsNotCalledByTheAutoScheduler()
        {
            // P14B scope guard: the utility AI must never break through for anyone.
            var scheduler = ReadSource("Assets/Scripts/Systems/AutoTaskScheduler.cs");
            var scoring = ReadSource("Assets/Scripts/Systems/AutoTaskScoring.cs");
            Assert.IsFalse(scheduler.Contains("Breakthrough"), "the Auto scheduler must not call TryBreakthrough");
            Assert.IsFalse(scoring.Contains("Breakthrough"), "and scoring must not consider cultivation at all");
        }

        // =============================================== 5. CanBreakthrough == TryBreakthrough

        [Test]
        public void CanBreakthrough_AgreesWithTryBreakthrough_InEveryState()
        {
            var requirement = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);

            var cases = new List<Action>
            {
                () => SetCultivation("d001", CultivationConfig.StageQiCondensation, requirement),      // ready
                () => SetCultivation("d001", CultivationConfig.StageQiCondensation, requirement - 1f), // not ready
                () => SetCultivation("d001", CultivationConfig.StageNascentSoul, 0f),                  // final
                () => SetCultivation("d001", "not_a_stage", 5f),                                       // unknown stage
                () => SetCultivation("d001", CultivationConfig.StageQiCondensation, float.NaN),        // non-finite
            };

            foreach (var configure in cases)
            {
                configure();
                string queryReason;
                var can = _provider.CanBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out queryReason);

                var stageBefore = Stage("d001");
                var progressBefore = Progress("d001");
                string commitReason;
                var committed = _provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out commitReason);

                Assert.AreEqual(can, committed, "the query and the commit must agree (stage='" + stageBefore + "')");
                if (!can)
                {
                    Assert.IsNotEmpty(queryReason, "a rejection always explains itself");
                    Assert.IsNotEmpty(commitReason);
                    Assert.AreEqual(stageBefore, Stage("d001"), "a rejected query/commit changes nothing");
                }
            }

            // unknown disciple agrees too
            Assert.IsFalse(_provider.CanBreakthrough(SectStateProvider.SectMasterRequesterId, "d999", out var missing));
            Assert.IsNotEmpty(missing);
            Assert.IsFalse(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d999", out _));
            Assert.IsFalse(_provider.CanBreakthrough("", "d001", out var noRequester));
            Assert.IsNotEmpty(noRequester);
        }

        [Test]
        public void CanBreakthrough_IsReadOnly_AndNeverRefreshesActivity()
        {
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");
            var boundAt = OwnerLastActive("viewer_test_01");

            _clock.UtcNow = T0.AddMinutes(20);
            string reason;
            Assert.IsTrue(_provider.CanBreakthrough("viewer_test_01", "d001", out reason));

            Assert.AreEqual(boundAt, OwnerLastActive("viewer_test_01"), "a read query must not refresh activity");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"), "…and must not mutate state");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        // =============================================== 6. persistence

        [Test]
        public void CommittedStage_SurvivesSaveAndRestore()
        {
            ReadyUp("d001");
            Assert.IsTrue(_provider.TryBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out var failReason),
                          failReason);

            // save: serialize the detached snapshot exactly like the save slot does
            var bytes = MessagePackSerializer.Serialize(_provider.CaptureSessionSnapshot());

            // restore into a brand-new session (fresh mock start state)
            var next = NewProvider(new MutableClock(T0.AddDays(1)));
            var snapshot = MessagePackSerializer.Deserialize<SectSessionSnapshot>(bytes);
            Assert.IsTrue(next.TryValidateSessionSnapshot(snapshot, out var validateFailure), validateFailure);
            Assert.IsTrue(next.TryApplySessionSnapshot(snapshot), "a committed breakthrough must restore");

            var restored = Find(next, "d001");
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, restored.Attributes.CultivationStageId);
            Assert.AreEqual(0f, restored.Attributes.CultivationProgress, Tol);
        }

        // =============================================== 7. Unity-only + bridge guard (rule 15 / §4)

        [Test]
        public void CultivationMessage_IsUnityOnly_NotShared_AndNotOnTheInterprocessBroker()
        {
            Assert.IsTrue(ReadSource("Assets/Scripts/Core/CultivationMessages.cs")
                              .Contains("DiscipleCultivationChangedMessage"),
                          "the message must be declared in the Unity-only file");
            // The prohibition is that the message TYPE lives outside a wire contract — a comment in
            // Shared/ may legitimately name it (the breakthrough RPC pair's comment does, which is
            // how the follow-up kept the rule documented where the pair is declared).
            Assert.IsFalse(ReadSource("../Shared/GameMessages.cs").Contains("class DiscipleCultivationChangedMessage"),
                           "the message type must not be declared in the canonical Shared/ contract");
            Assert.IsFalse(ReadSource("Assets/Scripts/Shared/GameMessages.cs").Contains("class DiscipleCultivationChangedMessage"),
                           "the Unity Shared copy must stay a byte-for-byte sync of the canonical Shared/");

            var interprocess = ReadSource("Assets/Scripts/Core/Installers/InterprocessInstaller.cs");
            Assert.IsFalse(interprocess.Contains("DiscipleCultivationChangedMessage"),
                           "in-memory only — never registered on the interprocess broker");

            // The message stays local; the OPERATION is what the bridge reaches, and reaching it
            // needs BOTH registrations (missing either fails silently at runtime).
            StringAssert.Contains("RegisterTcpRemoteRequestHandler<AttemptBreakthroughRequest, AttemptBreakthroughResponse>",
                                  interprocess,
                                  "the breakthrough pair must be wired to the TCP worker");
            StringAssert.Contains("RegisterAsyncRequestHandler<AttemptBreakthroughRequest, AttemptBreakthroughResponse, AttemptBreakthroughHandler>",
                                  interprocess,
                                  "…and the handler itself must be registered");
            Assert.IsFalse(interprocess.Contains("AttemptBreakthroughQuery"),
                           "no read pair — get_sect_state already carries stage + progress");
        }

        [Test]
        public void McpBridge_ExposesBreakthroughAsASecondWriteTool_WithNoBypass()
        {
            var program = ReadSource("../McpBridge/Program.cs");

            int queryTools = program.IndexOf("class SectQueryTools", StringComparison.Ordinal);
            int actionTools = program.IndexOf("class SectActionTools", StringComparison.Ordinal);
            int tool = program.IndexOf("AttemptBreakthrough", StringComparison.Ordinal);
            Assert.Greater(queryTools, -1);
            Assert.Greater(actionTools, queryTools, "the read/write split must still exist");
            Assert.Greater(tool, actionTools, "attempt_breakthrough must ship as a WRITE tool (SectActionTools)");
            Assert.IsFalse(program.Substring(queryTools, actionTools - queryTools).Contains("AttemptBreakthrough"),
                           "…and never as a query tool");

            // Request-response (the caller needs the verdict), not fire-and-forget.
            StringAssert.Contains("IRemoteRequestHandler<AttemptBreakthroughRequest, AttemptBreakthroughResponse>",
                                  program);

            // The tool declaration itself: one disciple argument and a hard-coded SECT_MASTER —
            // exposing a requester parameter would hand the AI GM an identity the Hybrid
            // protection window does not govern, i.e. a bypass a human caller never gets.
            int declStart = program.LastIndexOf("[McpServerTool", tool, StringComparison.Ordinal);
            int declEnd = program.IndexOf("[McpServerTool", tool, StringComparison.Ordinal);
            Assert.Greater(declStart, -1, "the tool must carry its [McpServerTool] declaration");
            var declaration = declEnd < 0
                ? program.Substring(declStart)
                : program.Substring(declStart, declEnd - declStart);
            Assert.IsFalse(declaration.ToLowerInvariant().Contains("string requester"),
                           "the caller may not supply a requester id (only the hard-coded SECT_MASTER)");
            StringAssert.Contains("RequesterId = \"SECT_MASTER\"", declaration,
                                  "the only policy the tool may carry is the sect master's");
        }

        [Test]
        public void BreakthroughHandler_DelegatesToTheProvider_WithoutASecondPolicy()
        {
            var handler = ReadSource("Assets/Scripts/Core/AttemptBreakthroughHandler.cs");

            StringAssert.Contains("TryBreakthrough(", handler,
                                  "the operation is the same call the UI button goes through");
            StringAssert.Contains("MainThreadDispatch.RunAsync", handler,
                                  "live state is mutated only after the main-thread hop (T1)");
            StringAssert.Contains("_session.IsPlaying", handler,
                                  "P12A — a mutation outside Playing is refused");
            Assert.IsFalse(handler.Contains("EvaluateTaskPermission("),
                           "permission is evaluated ONCE, by the provider — the handler must not re-run it");
            Assert.IsFalse(handler.Contains("CultivationStageId"),
                           "the handler must not touch cultivation state itself");
            Assert.IsFalse(handler.Contains("CultivationProgress"),
                           "the single atomic mutation stays in SectStateProvider.TryBreakthrough");
        }

        [Test]
        public void AttemptBreakthroughHandler_SectMasterAgainstAnActiveViewer_IsRefused_AndChangesNothing()
        {
            // End-to-end policy inheritance: the bridge path carries SECT_MASTER, and this is
            // exactly what that identity gets against a viewer-owned disciple whose owner is
            // still active — the same refusal assign_task returns (same fixture as section 3,
            // which proves the identity, not the wire, decides).
            ReadyUp("d001");
            BindViewerOwner("d001", "viewer_test_01");

            var handler = new AttemptBreakthroughHandler(_provider);
            var response = handler.InvokeAsync(new AttemptBreakthroughRequest
            {
                RequesterId = "SECT_MASTER",
                DiscipleId = "d001",
            }).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success, "an active viewer owner still protects the disciple");
            Assert.IsNotEmpty(response.FailReason, "the authority's reason is handed back verbatim");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"), "nothing advanced");
            Assert.IsEmpty(_cultivationChanged.Messages, "and nothing was published");
        }

        // =============================================== 8. DI (Common Rule 16)

        [Test]
        public void RealContainer_ResolvesTheDetailPresenter_WithTheCultivationPublisherDependency()
        {
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.RegisterInstance<ISectStateProvider>(_provider);
            builder.RegisterInstance(new AvatarPartPool());
            // The exact registration UIInstaller uses for the panel presenter.
            builder.Register<DiscipleDetailPresenter>(Lifetime.Transient);
            var container = builder.Build();
            try
            {
                var presenter = container.Resolve<DiscipleDetailPresenter>();
                Assert.IsNotNull(presenter, "the real container must resolve the panel presenter");
                Assert.IsNotNull(container.Resolve<IPublisher<DiscipleCultivationChangedMessage>>(),
                                 "MessagePipe must supply the in-memory publisher the presenter subscribes to");
                Assert.IsNotNull(container.Resolve<ISubscriber<DiscipleCultivationChangedMessage>>());
            }
            finally
            {
                container.Dispose();
            }

            // …and the production registration really is that line.
            StringAssert.Contains("Register<DiscipleDetailPresenter>",
                                  ReadSource("Assets/Scripts/Core/Installers/UIInstaller.cs"));
        }

        // =============================================== 9. the generated prefab

        [Test]
        public void GeneratedPanel_WiresTheCultivationRowAndTheBreakthroughButton()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            Assert.IsNotNull(prefab, "the generated DiscipleDetailPanel prefab must exist");

            var view = prefab.GetComponent<DiscipleDetailView>();
            var so = new SerializedObject(view);
            foreach (var field in new[]
                     {
                         "cultivationStageText", "cultivationProgressText", "cultivationBarFill",
                         "breakthroughButton", "breakthroughReasonText",
                     })
            {
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue,
                                 "the generator must wire '" + field + "' (never hand-edit the prefab YAML)");
            }

            var info = prefab.transform.Find("Window/ContentHost/InfoContent");
            foreach (var band in new[]
                     {
                         "NameRow", "RankRow", "TaskRow", "WalletRow", "StaminaRow",
                         "CultivationRow", "SkillRowGathering", "SkillRowAlchemy", "SkillRowForging",
                         "BreakthroughReasonText", "BreakthroughButton",
                     })
            {
                Assert.IsNotNull(info.Find(band), "the ข้อมูล tab must still contain '" + band + "'");
            }

            // Every band must fit inside the 522 px tab (the tab is full height — spacing was
            // tightened, not the host grown) and no band may overlap the next one.
            float previousBottom = 0f;
            foreach (var band in new[]
                     {
                         "NameRow", "RankRow", "TaskRow", "WalletRow", "StaminaRow", "CultivationRow",
                         "SkillRowGathering", "SkillRowAlchemy", "SkillRowForging",
                         "BreakthroughReasonText", "BreakthroughButton",
                     })
            {
                var rt = (RectTransform)info.Find(band);
                float top = -rt.anchoredPosition.y;
                float bottom = top + rt.sizeDelta.y;
                Assert.LessOrEqual(bottom, 522f, "'" + band + "' must fit inside the ข้อมูล tab");
                if (band != "BreakthroughButton") // the reason text and the button share a row
                    Assert.GreaterOrEqual(top, previousBottom, "'" + band + "' must not overlap the band above");
                previousBottom = bottom;
            }

            Assert.AreEqual("ทะลวงขั้น",
                            info.Find("BreakthroughButton/Label").GetComponent<TMP_Text>().text);
        }

        // =============================================== 10. the ข้อมูล tab behaviour

        /// <summary>
        /// Minimal real view hierarchy wired through SerializedObject — the same mechanism the
        /// panel generator uses (pattern from DiscipleDetailAttributeTests).
        /// </summary>
        private void BuildInfoTab()
        {
            _rootGo = new GameObject("CultivationBreakthroughTestRoot", typeof(RectTransform));
            _rootGo.SetActive(false); // defer Awake until the refs are wired

            _view = _rootGo.AddComponent<DiscipleDetailView>();

            var rail = InkWidgets.Rect(_rootGo.transform, "Rail", 0, 0, 720, 64);
            var tabs = InkWidgets.Rect(_rootGo.transform, "Tabs", 0, 0, 1230, 52);
            for (int i = 0; i < 6; i++)
            {
                var tab = InkWidgets.Rect(tabs, "Tab_" + i, i * 202, 0, 190, 52);
                var image = InkWidgets.Fill(tab, UiPalette.Paper, true);
                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                _tabButtons.Add(button);
            }

            var host = InkWidgets.Rect(_rootGo.transform, "ContentHost", 0, 0, 924, 522);
            var info = InkWidgets.Rect(host, "InfoContent", 0, 0, 924, 522);
            for (int i = 1; i < 6; i++) InkWidgets.Rect(host, "Tab" + i, 0, 0, 924, 522);

            var cultivationRow = InkWidgets.Rect(info, "CultivationRow", 0, 255, 924, 48);
            _cultivationStageText = InkWidgets.Text(cultivationRow, "Label", "", null, 26, 28, 0, 300, 26);
            _cultivationBar = InkWidgets.Rect(cultivationRow, "CultivationBar", 28, 33, 300, 9);
            InkWidgets.Fill(_cultivationBar, UiPalette.Disabled);
            _cultivationFill = InkWidgets.Fill(InkWidgets.Rect(_cultivationBar, "Fill", 0, 0, 300, 9), UiPalette.Jade);
            _cultivationProgressText = InkWidgets.Text(cultivationRow, "Value", "", null, 26, 536, 0, 378, 48,
                                                       UiPalette.Text, TextAlignmentOptions.MidlineRight);
            _breakthroughReasonText = InkWidgets.Text(info, "BreakthroughReasonText", "", null, 26, 0, 462, 612, 48);
            _breakthroughButton = InkWidgets.OutlineButton(info, "BreakthroughButton", "ทะลวงขั้น", null, 624, 462, 300, 48);

            var closeGo = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_rootGo.transform, false);

            var so = new SerializedObject(_view);
            so.FindProperty("railContent").objectReferenceValue = rail;
            so.FindProperty("tabRail").objectReferenceValue = tabs;
            so.FindProperty("contentHost").objectReferenceValue = host;
            so.FindProperty("closeButton").objectReferenceValue = closeGo.GetComponent<Button>();
            so.FindProperty("cultivationStageText").objectReferenceValue = _cultivationStageText;
            so.FindProperty("cultivationProgressText").objectReferenceValue = _cultivationProgressText;
            so.FindProperty("cultivationBarFill").objectReferenceValue = _cultivationFill;
            so.FindProperty("breakthroughButton").objectReferenceValue = _breakthroughButton;
            so.FindProperty("breakthroughReasonText").objectReferenceValue = _breakthroughReasonText;
            so.ApplyModifiedPropertiesWithoutUndo();

            _rootGo.SetActive(true);
            _view.Show();
        }

        private void OpenInfoTab(string discipleId, ISubscriber<DiscipleCultivationChangedMessage> subscriber = null)
        {
            BuildInfoTab();
            _presenter = new DiscipleDetailPresenter(_provider, new AvatarPartPool(), subscriber);
            _presenter.Bind(_view);
            _presenter.OnOpen(discipleId);
        }

        [Test]
        public void InfoTab_ShowsTheStageAndProgress_AndDisablesTheButtonWhenNotReady()
        {
            var requirement = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);
            SetCultivation("d001", CultivationConfig.StageQiCondensation, requirement / 2f);

            OpenInfoTab("d001");

            StringAssert.Contains(CultivationConfig.DisplayName(CultivationConfig.StageQiCondensation),
                                  _cultivationStageText.text);
            Assert.AreEqual(Mathf.RoundToInt(requirement / 2f) + " / " + Mathf.RoundToInt(requirement),
                            _cultivationProgressText.text, "progress is shown as 'progress / requirement'");
            Assert.AreEqual(0.5f, _cultivationFill.rectTransform.sizeDelta.x / _cultivationBar.rect.width, 0.01f,
                            "the bar fill is proportional to progress");
            Assert.IsFalse(_breakthroughButton.interactable, "not ready → the button is disabled");
            Assert.IsNotEmpty(_breakthroughReasonText.text, "and the reason is shown inline");
        }

        [Test]
        public void InfoTab_Ready_EnablesTheButton_AndClickingCommitsThroughThePresenter()
        {
            ReadyUp("d001");
            OpenInfoTab("d001");

            Assert.IsTrue(_breakthroughButton.interactable, "ready → the button is enabled");
            Assert.IsEmpty(_breakthroughReasonText.text);
            StringAssert.Contains("พร้อมทะลวงขั้น", _cultivationProgressText.text, "ready is highlighted");

            _breakthroughButton.onClick.Invoke();

            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, Stage("d001"),
                            "the click committed through the authoritative provider");
            Assert.AreEqual(0f, Progress("d001"), Tol);
            StringAssert.Contains(CultivationConfig.DisplayName(CultivationConfig.StageFoundationEstablishment),
                                  _cultivationStageText.text, "the row shows the new stage");
            Assert.AreEqual("0 / " + Mathf.RoundToInt(CultivationConfig.Requirement(CultivationConfig.StageFoundationEstablishment)),
                            _cultivationProgressText.text);
            Assert.IsFalse(_breakthroughButton.interactable, "the new stage is not ready yet");
            Assert.AreEqual(1, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void InfoTab_RejectedClick_ShowsTheFailReasonInline()
        {
            // Ready when the tab was rendered, no longer ready when the button is pressed —
            // the click must REVALIDATE (never trust the cached display state).
            ReadyUp("d001");
            OpenInfoTab("d001");
            Assert.IsTrue(_breakthroughButton.interactable);

            SetCultivation("d001", CultivationConfig.StageQiCondensation, 1f);
            _breakthroughButton.onClick.Invoke();

            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d001"), "a stale click changes nothing");
            Assert.IsFalse(_breakthroughButton.interactable);
            Assert.IsNotEmpty(_breakthroughReasonText.text, "the rejection reason is shown inline");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        [Test]
        public void InfoTab_FinalStage_ShowsTheFinalLabel_AndDisablesTheButton()
        {
            SetCultivation("d001", CultivationConfig.StageNascentSoul, 0f);
            OpenInfoTab("d001");

            Assert.AreEqual("ขั้นสุดท้าย", _cultivationProgressText.text);
            Assert.IsFalse(_breakthroughButton.interactable, "there is nothing beyond the final stage");
            Assert.IsNotEmpty(_breakthroughReasonText.text);
        }

        [Test]
        public void InfoTab_RefreshesOnTheCultivationMessage_ForTheOpenDiscipleOnly()
        {
            SetCultivation("d001", CultivationConfig.StageQiCondensation, 10f);
            var subscriber = new ForwardingSubscriber<DiscipleCultivationChangedMessage>();
            OpenInfoTab("d001", subscriber);
            Assert.AreEqual("10 / 300", _cultivationProgressText.text);

            // a live tick re-renders from authoritative state (the existing ≤4/s refresh)
            SetCultivation("d001", CultivationConfig.StageQiCondensation, 20f);
            _view.TickAttributeRefresh(1f);
            Assert.AreEqual("20 / 300", _cultivationProgressText.text);

            // a breakthrough message for ANOTHER disciple is ignored
            SetCultivation("d001", CultivationConfig.StageQiCondensation, 30f);
            subscriber.Deliver(new DiscipleCultivationChangedMessage
            {
                DiscipleId = "d002",
                OldStageId = CultivationConfig.StageQiCondensation,
                NewStageId = CultivationConfig.StageFoundationEstablishment,
            });
            Assert.AreEqual("20 / 300", _cultivationProgressText.text,
                            "another disciple's breakthrough must not re-render this panel");

            // …and the open disciple's own message refreshes through the same bind path
            subscriber.Deliver(new DiscipleCultivationChangedMessage
            {
                DiscipleId = "d001",
                OldStageId = CultivationConfig.StageQiCondensation,
                NewStageId = CultivationConfig.StageFoundationEstablishment,
            });
            Assert.AreEqual("30 / 300", _cultivationProgressText.text);
        }

        [Test]
        public void InfoTab_DoesNotTouchTheStatusRadarTab()
        {
            // The สเตตัส tab stays the untouched placeholder: the cultivation row lives only on ข้อมูล
            // and switching tabs must not rewrite it or publish anything.
            ReadyUp("d001");
            OpenInfoTab("d001");
            var rendered = _cultivationProgressText.text;

            _tabButtons[(int)DiscipleTab.Status].onClick.Invoke();

            Assert.AreEqual(rendered, _cultivationProgressText.text,
                            "the สเตตัส tab never rewrites the cultivation row");
            Assert.AreEqual(0, _cultivationChanged.Messages.Count);
        }

        // ---------------------------------------------------------------- test doubles

        private static string ReadSource(string relativePath)
        {
            return System.IO.File.ReadAllText(System.IO.Path.Combine(
                UnityEngine.Application.dataPath + "/..", relativePath));
        }

        private sealed class MutableClock : IClock
        {
            public MutableClock(DateTime now) { UtcNow = now; }
            public DateTime UtcNow { get; set; }
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        /// <summary>MessagePipe's real shape: captures the handler so the test can deliver a message.</summary>
        private sealed class ForwardingSubscriber<T> : ISubscriber<T>
        {
            private IMessageHandler<T> _handler;

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                _handler = handler;
                return new SubscriptionStub();
            }

            public void Deliver(T message) => _handler.Handle(message);

            private sealed class SubscriptionStub : IDisposable { public void Dispose() { } }
        }
    }
}
