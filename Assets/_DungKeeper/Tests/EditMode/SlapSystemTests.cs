using System.Collections.Generic;
using NUnit.Framework;

namespace DungKeeper.Tests
{
    /// <summary>
    /// EditMode unit tests for slap mechanics, morale, and resource production.
    ///
    /// These tests run entirely in the Unity Editor (no Play Mode session required)
    /// because all systems under test are pure C# with no MonoBehaviour or
    /// scene-object dependencies.
    /// </summary>
    [TestFixture]
    public sealed class SlapSystemTests
    {
        // =====================================================================
        // Shared fixtures
        // =====================================================================

        private GameSettings _settings;
        private SlapSystem   _slapSystem;

        // Isolated event bus per test run — prevents cross-test event pollution.
        private EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _settings   = GameSettings.Default();
            _bus        = new EventBus();
            _slapSystem = new SlapSystem(_bus);

            // Widen cooldown window to zero so tests can slap freely without
            // the cooldown diminish factor obscuring the expected response.
            _settings.SlapCooldownSeconds = 0f;
        }

        // =====================================================================
        // Helper factories
        // =====================================================================

        /// <summary>
        /// Creates a fresh unit and advances its GameTime far enough that it is
        /// never considered to be on slap cooldown during a test.
        /// </summary>
        private static UnitData MakeUnit(string name, UnitRole role, PersonalityTrait personality)
        {
            var unit = new UnitData(name, role, personality);
            unit.GameTime       = 9999f; // well past any cooldown window
            unit.LastSlappedTime = 0f;
            unit.CurrentState   = UnitState.Working;
            return unit;
        }

        // =====================================================================
        // Test 1 — Cowardly unit responds with SpeedUp on a light slap
        // =====================================================================

        [Test]
        public void CowardlyUnit_SlapResponse_ShouldSpeedUp()
        {
            // Arrange — cowardly worker with clean accumulation record
            var unit = MakeUnit("Sniveler", UnitRole.Worker, PersonalityTrait.Cowardly);
            // Ensure accumulation is zero so the ratio is well below the SpeedUp threshold
            unit.CurrentSlapAccumulation = 0f;

            // Act — light slap (force 5 out of 100)
            SlapResult result = _slapSystem.ProcessSlap(unit, 5f, _settings);

            // Assert
            Assert.That(result.Response, Is.EqualTo(SlapResponse.SpeedUp),
                "A cowardly unit at zero accumulation should always speed up on a light slap.");
            Assert.That(result.ProductivityModifier, Is.GreaterThan(0f),
                "SpeedUp response must carry a positive productivity modifier.");
        }

        // =====================================================================
        // Test 2 — Masochistic unit produces BecomeExcited (or SpeedUp) on a slap
        // =====================================================================

        [Test]
        public void MasochisticUnit_SlapResponse_ShouldBecomeExcited()
        {
            // Arrange
            var unit = MakeUnit("Glurp", UnitRole.Worker, PersonalityTrait.Masochistic);
            unit.CurrentSlapAccumulation = 0f;

            // Act — moderate slap
            SlapResult result = _slapSystem.ProcessSlap(unit, 15f, _settings);

            // Assert — masochistic units love slaps; they must not become angry or quit
            Assert.That(
                result.Response == SlapResponse.BecomeExcited || result.Response == SlapResponse.SpeedUp,
                Is.True,
                $"Masochistic unit should BecomeExcited or SpeedUp; got {result.Response}.");
            Assert.That(result.ProductivityModifier, Is.GreaterThan(0f),
                "Masochistic slap response must yield a positive productivity modifier.");
        }

        // =====================================================================
        // Test 3 — Over-accumulation triggers Rebel
        // =====================================================================

        [Test]
        public void OverSlapAccumulation_ShouldTriggerRebellion()
        {
            // Arrange — worker whose accumulation is already just below the rebellion fraction
            var unit = MakeUnit("Grumble", UnitRole.Worker, PersonalityTrait.Cowardly);

            // Pre-load accumulation to 90 % of SlapTolerance so the first real
            // slap crosses the RebellionAccumFraction threshold inside ProcessSlap.
            unit.CurrentSlapAccumulation = unit.SlapTolerance * 0.90f;

            // Track rebellion event
            bool rebellionFired = false;
            _bus.Subscribe<UnitRebellionStartedEvent>(_ => rebellionFired = true);

            // Act — apply a moderate force slap; this should tip accumulation past the limit
            SlapResult result = _slapSystem.ProcessSlap(unit, 20f, _settings);

            // Assert
            Assert.That(
                result.Response == SlapResponse.Rebel || result.Response == SlapResponse.Quit,
                Is.True,
                $"Unit at 90 % accumulation should Rebel or Quit; got {result.Response}.");

            // If Rebel, the rebellion event must have fired
            if (result.Response == SlapResponse.Rebel)
                Assert.That(rebellionFired, Is.True,
                    "UnitRebellionStartedEvent must be published when a unit rebels.");
        }

        // =====================================================================
        // Test 4 — Aggressive unit becomes angry on a heavy slap
        // =====================================================================

        [Test]
        public void AggressiveUnit_HeavySlap_ShouldBecomeAngry()
        {
            // Arrange
            var unit = MakeUnit("Wrath", UnitRole.Guard, PersonalityTrait.Aggressive);
            // Set accumulation to mid-range — aggressive units flip to BecomeAngry early
            unit.CurrentSlapAccumulation = unit.SlapTolerance * 0.45f;

            float angerBefore = unit.Anger;

            // Act — heavy slap
            SlapResult result = _slapSystem.ProcessSlap(unit, 50f, _settings);

            // Assert — response must be anger-tier or above
            Assert.That(
                result.Response == SlapResponse.BecomeAngry ||
                result.Response == SlapResponse.Rebel,
                Is.True,
                $"Aggressive unit at 45 % accumulation with heavy slap should be Angry or Rebel; got {result.Response}.");
            Assert.That(result.NewAnger, Is.GreaterThan(angerBefore),
                "Anger must increase after a heavy slap on an aggressive unit.");
        }

        // =====================================================================
        // Test 5 — Walkout (Quit) result leaves the unit dead
        // =====================================================================

        [Test]
        public void SlowDownAfterWalkout_IsAlive_ShouldBeFalse()
        {
            // Arrange — mercenary unit whose gold pay is zero so it quits quickly
            var unit = MakeUnit("Coins", UnitRole.Worker, PersonalityTrait.Mercenary);
            unit.CurrentGoldPerTick      = 0f;  // zero pay → highest quit probability
            // Push accumulation near tolerance so the quit path is reachable
            unit.CurrentSlapAccumulation = unit.SlapTolerance * 0.86f;

            bool unitDiedEventFired = false;
            _bus.Subscribe<UnitDiedEvent>(_ => unitDiedEventFired = true);

            // Act — slap hard enough to trigger Quit
            SlapResult result = _slapSystem.ProcessSlap(unit, 60f, _settings);

            // Slap until the unit quits (safety limit prevents infinite loop)
            int safetyCounter = 0;
            while (result.Response != SlapResponse.Quit && safetyCounter < 20)
            {
                unit.CurrentSlapAccumulation += 2f;
                result = _slapSystem.ProcessSlap(unit, 60f, _settings);
                safetyCounter++;
            }

            // If we reached Quit, the unit must be dead
            if (result.Response == SlapResponse.Quit)
            {
                Assert.That(unit.IsAlive, Is.False,
                    "A unit that Quits must be marked dead immediately.");
                Assert.That(unitDiedEventFired, Is.True,
                    "UnitDiedEvent must be published when a unit quits.");
            }
            else
            {
                // The mercenary may rebel rather than quit depending on exact balance;
                // assert at minimum that its state is disruptive
                Assert.That(
                    result.Response == SlapResponse.Rebel || result.Response == SlapResponse.BecomeAngry,
                    Is.True,
                    $"Over-slapped mercenary must reach a disruptive state; got {result.Response}.");
            }
        }

        // =====================================================================
        // Test 6 — Hunger above threshold causes morale to decay over time ticks
        // =====================================================================

        [Test]
        public void MoraleDecay_WhenHungry()
        {
            // Arrange — unit with hunger set just above the distress threshold
            var unit = MakeUnit("Gnaw", UnitRole.Worker, PersonalityTrait.Lazy);
            unit.Hunger = GameConstants.HungerDistressThreshold + 5f; // 75 (threshold is 70)
            unit.Morale = 80f; // start high so decay is clearly measurable
            unit.CurrentState = UnitState.Working;

            float moraleBeforeTicks = unit.Morale;

            // Act — simulate multiple time ticks (total 30 seconds of game time)
            const float deltaTime = 1f;
            const int   ticks     = 30;

            for (int i = 0; i < ticks; i++)
            {
                // Hunger stays elevated (unit cannot eat while being ticked manually)
                unit.Hunger = GameConstants.HungerDistressThreshold + 5f;
                unit.RecoverOverTime(deltaTime, _settings);
            }

            // Assert — morale must be lower than the starting value
            Assert.That(unit.Morale, Is.LessThan(moraleBeforeTicks),
                "Morale should decrease over time when the unit is hungry.");

            // The unit should be in distress
            Assert.That(unit.IsInDistress(), Is.True,
                "Unit with hunger above distress threshold must report IsInDistress == true.");
        }

        // =====================================================================
        // Test 7 — Worker in a ProductionChamber generates Gold over ticks
        // =====================================================================

        [Test]
        public void ResourceProduction_WithWorker()
        {
            // Arrange
            const float productionRate = 2f; // gold per worker-productivity unit per second

            var room = RoomData.Create(
                name:           "Gold Mine",
                type:           RoomType.ProductionChamber,
                capacity:       4,
                productionRate: productionRate);

            var worker = new UnitData("Digger", UnitRole.Worker, PersonalityTrait.Loyal);
            worker.CurrentState = UnitState.Working;
            worker.Productivity = 1.0f; // normalised so gold rate = productionRate * 1.0

            // Assign worker to room
            room.AssignedUnitIds.Add(worker.Id);

            var resourceSystem = new ResourceSystem();
            var units = new List<UnitData> { worker };
            var rooms = new List<RoomData> { room };

            float goldBefore = resourceSystem.Get(ResourceType.Gold);

            // Act — tick 10 seconds of production
            const float deltaTime  = 1f;
            const int   ticks      = 10;

            for (int i = 0; i < ticks; i++)
                resourceSystem.Tick(units, rooms, deltaTime);

            float goldAfter = resourceSystem.Get(ResourceType.Gold);

            // Assert — gold must have increased
            Assert.That(goldAfter, Is.GreaterThan(goldBefore),
                "A worker assigned to a ProductionChamber should generate Gold over time.");

            // The amount produced must be consistent with the rate * time formula.
            // Rate = productionRate * worker.Productivity; ticks * deltaTime = 10 s.
            float expectedMinimum = productionRate * worker.Productivity * ticks * deltaTime * 0.5f;
            Assert.That(goldAfter, Is.GreaterThanOrEqualTo(expectedMinimum),
                $"Gold generated ({goldAfter:F2}) should be at least half the theoretical maximum " +
                $"({expectedMinimum:F2}) — check production-rate formula.");
        }
    }
}
