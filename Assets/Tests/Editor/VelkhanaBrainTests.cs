using NUnit.Framework;
using UnityEngine;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.Tests
{
    public class VelkhanaBrainTests
    {
        [Test]
        public void Em124RangeBandsUseReadableMetricCentres()
        {
            Assert.AreEqual(6.12f,
                VelkhanaBrain.DesiredDistanceForBand(RangeBand.Close, 8.5f, 17f), 0.001f);
            Assert.AreEqual(12.75f,
                VelkhanaBrain.DesiredDistanceForBand(RangeBand.Medium, 8.5f, 17f), 0.001f);
            Assert.AreEqual(20f,
                VelkhanaBrain.DesiredDistanceForBand(RangeBand.Far, 8.5f, 17f), 0.001f);
        }

        [Test]
        public void RepositionSteeringMovesInWhenFarAndOutWhenCrowded()
        {
            Vector3 moveIn = VelkhanaBrain.CalculateRepositionDirection(
                Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 10f),
                5f, 0.5f, 1f, 0.55f);
            Vector3 moveOut = VelkhanaBrain.CalculateRepositionDirection(
                Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 2f),
                5f, 0.5f, 1f, 0.55f);

            Assert.Greater(Vector3.Dot(moveIn, Vector3.forward), 0.9f);
            Assert.Less(Vector3.Dot(moveOut, Vector3.forward), -0.9f);
            Assert.AreEqual(0f, moveIn.y, 0.001f);
            Assert.AreEqual(0f, moveOut.y, 0.001f);
        }

        [Test]
        public void RepositionSteeringOrbitsWhenDistanceIsAlreadyCorrect()
        {
            Vector3 direction = VelkhanaBrain.CalculateRepositionDirection(
                Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -10f),
                10f, 1f, 1f, 0.55f);

            Assert.Greater(Mathf.Abs(direction.x), 0.99f,
                "a target behind the monster should produce a lateral orbit at the desired range");
            Assert.AreEqual(0f, direction.z, 0.001f);
        }

        [Test]
        public void ArmorStageGlowStrengthProgressesAndNeutralIsOff()
        {
            Assert.AreEqual(0f, VelkhanaPresentation.StageGlowStrength(ArmorStage.Neutral), 0.001f);
            Assert.Greater(
                VelkhanaPresentation.StageGlowStrength(ArmorStage.IceArmorStage2),
                VelkhanaPresentation.StageGlowStrength(ArmorStage.IceArmorStage1));
            Assert.Greater(
                VelkhanaPresentation.StageGlowStrength(ArmorStage.Ultimate),
                VelkhanaPresentation.StageGlowStrength(ArmorStage.IceArmorStage2));
        }

        [Test]
        public void DetailedConditionsRespectDecodedDistanceFacingVerticalAndModeGates()
        {
            var option = new MonsterAttackOption
            {
                useEm124Conditions = true,
                minimumDistance = 6f,
                maximumDistance = 16f,
                maximumVerticalDistance = 7.5f,
                minimumFacingAngle = 0f,
                maximumFacingAngle = 30f,
                modes = VelkhanaCombatModeMask.Mode2,
                airRequirement = VelkhanaAirRequirement.Grounded,
            };

            Assert.IsTrue(VelkhanaBrain.DetailedConditionsMatch(
                option, 12f, 2f, 20f, VelkhanaCombatMode.Mode2, false));
            Assert.IsFalse(VelkhanaBrain.DetailedConditionsMatch(
                option, 17f, 2f, 20f, VelkhanaCombatMode.Mode2, false), "distance gate");
            Assert.IsFalse(VelkhanaBrain.DetailedConditionsMatch(
                option, 12f, 8f, 20f, VelkhanaCombatMode.Mode2, false), "vertical gate");
            Assert.IsFalse(VelkhanaBrain.DetailedConditionsMatch(
                option, 12f, 2f, 45f, VelkhanaCombatMode.Mode2, false), "facing gate");
            Assert.IsFalse(VelkhanaBrain.DetailedConditionsMatch(
                option, 12f, 2f, 20f, VelkhanaCombatMode.Mode1, false), "mode gate");
            Assert.IsFalse(VelkhanaBrain.DetailedConditionsMatch(
                option, 12f, 2f, 20f, VelkhanaCombatMode.Mode2, true), "air gate");
        }

        [Test]
        public void Function101BucketsRemainExplicitAndFollowIcePresentationStages()
        {
            Assert.AreEqual(
                VelkhanaCombatMode.Mode0,
                VelkhanaBrain.ModeForStage(ArmorStage.Neutral));
            Assert.AreEqual(
                VelkhanaCombatMode.Mode1,
                VelkhanaBrain.ModeForStage(ArmorStage.IceArmorStage1));
            Assert.AreEqual(
                VelkhanaCombatMode.Mode2,
                VelkhanaBrain.ModeForStage(ArmorStage.IceArmorStage2));
            Assert.AreEqual(
                VelkhanaCombatMode.Mode2,
                VelkhanaBrain.ModeForStage(ArmorStage.Ultimate));
        }

        [Test]
        public void ExactEm124OptionUsesItsOwnDistanceTierCentre()
        {
            var option = new MonsterAttackOption
            {
                useEm124Conditions = true,
                minimumDistance = 3.5f,
                maximumDistance = 13f,
            };

            Assert.AreEqual(
                8.725f,
                VelkhanaBrain.DesiredDistanceForOption(option, 8.5f, 17f),
                0.001f);
        }

        [Test]
        public void CombatMainNode006UsesExactForcedAndFiftyFiftyBoundaries()
        {
            Assert.AreEqual(
                VelkhanaAerialOptionFamily.Global051,
                VelkhanaBrain.SelectCombatMainNode006(true, 99),
                "the unresolved predicate's true branch forces Global051");
            Assert.AreEqual(
                VelkhanaAerialOptionFamily.Global051,
                VelkhanaBrain.SelectCombatMainNode006(false, 0));
            Assert.AreEqual(
                VelkhanaAerialOptionFamily.Global051,
                VelkhanaBrain.SelectCombatMainNode006(false, 49));
            Assert.AreEqual(
                VelkhanaAerialOptionFamily.Global052,
                VelkhanaBrain.SelectCombatMainNode006(false, 50));
            Assert.AreEqual(
                VelkhanaAerialOptionFamily.Global052,
                VelkhanaBrain.SelectCombatMainNode006(false, 99));
        }

        [TestCase(VelkhanaCombatMode.Mode0, false, 59, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode0, false, 60, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode0, false, 74, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode0, false, 75, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 54, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 55, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 64, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 65, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 74, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 75, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 79, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode0, true, 80, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 49, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 50, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 59, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 60, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 69, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode1, false, 70, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 34, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 35, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 44, VelkhanaGroundOpenerParent.Global105)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 45, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 49, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 50, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 64, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 65, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 74, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode1, true, 75, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, false, 44, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, false, 45, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode2, false, 64, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode2, false, 65, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 39, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 40, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 49, VelkhanaGroundOpenerParent.Global106)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 50, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 69, VelkhanaGroundOpenerParent.None)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 70, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 79, VelkhanaGroundOpenerParent.Global108)]
        [TestCase(VelkhanaCombatMode.Mode2, true, 80, VelkhanaGroundOpenerParent.None)]
        public void GroundOpenerParentUsesDecodedSourceOrderBoundaries(
            VelkhanaCombatMode mode,
            bool enraged,
            int roll,
            VelkhanaGroundOpenerParent expected)
        {
            Assert.AreEqual(
                expected,
                VelkhanaBrain.SelectGroundOpenerParent(mode, enraged, roll));
        }

        [TestCase(3f, 19, VelkhanaNode087Leaf.Global004)]
        [TestCase(3f, 20, VelkhanaNode087Leaf.Global009)]
        [TestCase(3.001f, 49, VelkhanaNode087Leaf.Global004)]
        [TestCase(3.001f, 50, VelkhanaNode087Leaf.Global006)]
        [TestCase(7f, 74, VelkhanaNode087Leaf.Global006)]
        [TestCase(7f, 75, VelkhanaNode087Leaf.Global009)]
        [TestCase(7.001f, 19, VelkhanaNode087Leaf.Global004)]
        [TestCase(7.001f, 20, VelkhanaNode087Leaf.Global006)]
        [TestCase(13f, 99, VelkhanaNode087Leaf.Global006)]
        [TestCase(13.001f, 0, VelkhanaNode087Leaf.None)]
        [TestCase(13.001f, 99, VelkhanaNode087Leaf.None)]
        public void GlobalNode087UsesExactDistanceAndRollBoundaries(
            float distance,
            int roll,
            VelkhanaNode087Leaf expected)
        {
            Assert.AreEqual(expected, VelkhanaBrain.SelectNode087Leaf(distance, roll));
        }

        [TestCase(VelkhanaNode087Leaf.Global009, VelkhanaGroundContinuationNode.Global088)]
        [TestCase(VelkhanaNode087Leaf.Global006, VelkhanaGroundContinuationNode.Global089)]
        [TestCase(VelkhanaNode087Leaf.Global004, VelkhanaGroundContinuationNode.Global090)]
        [TestCase(VelkhanaNode087Leaf.None, VelkhanaGroundContinuationNode.None)]
        public void Node087LeafMapsToItsDecodedContinuation(
            VelkhanaNode087Leaf opener,
            VelkhanaGroundContinuationNode expected)
        {
            Assert.AreEqual(expected, VelkhanaBrain.ContinuationNodeFor(opener));
        }

        [TestCase(VelkhanaNode087Leaf.Global009, 3f, 64,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global009, 3.001f, 65,
            VelkhanaGroundContinuationTarget.Global006)]
        [TestCase(VelkhanaNode087Leaf.Global009, 9f, 64,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global009, 9.001f, 65,
            VelkhanaGroundContinuationTarget.Global006)]
        [TestCase(VelkhanaNode087Leaf.Global009, 13f, 65,
            VelkhanaGroundContinuationTarget.Global006)]
        [TestCase(VelkhanaNode087Leaf.Global009, 13.001f, -1,
            VelkhanaGroundContinuationTarget.None)]
        [TestCase(VelkhanaNode087Leaf.Global006, 3f, 64,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global006, 3.001f, 65,
            VelkhanaGroundContinuationTarget.Global009)]
        [TestCase(VelkhanaNode087Leaf.Global006, 9f, 65,
            VelkhanaGroundContinuationTarget.Global009)]
        [TestCase(VelkhanaNode087Leaf.Global006, 9.001f, 99,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global006, 13f, 99,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global006, 13.001f, 100,
            VelkhanaGroundContinuationTarget.None)]
        [TestCase(VelkhanaNode087Leaf.Global004, 3f, 49,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global004, 3.001f, 50,
            VelkhanaGroundContinuationTarget.Global079)]
        [TestCase(VelkhanaNode087Leaf.Global004, 9f, 50,
            VelkhanaGroundContinuationTarget.Global079)]
        [TestCase(VelkhanaNode087Leaf.Global004, 9.001f, 99,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global004, 13f, 99,
            VelkhanaGroundContinuationTarget.Global004)]
        [TestCase(VelkhanaNode087Leaf.Global004, 13.001f, -1,
            VelkhanaGroundContinuationTarget.None)]
        public void GroundOpenerContinuationUsesPostMotionDistanceAndExactRollBoundaries(
            VelkhanaNode087Leaf opener,
            float postMotionDistance,
            int roll,
            VelkhanaGroundContinuationTarget expected)
        {
            Assert.AreEqual(
                expected,
                VelkhanaBrain.SelectGroundOpenerContinuation(
                    opener, postMotionDistance, roll));
        }

        [Test]
        public void GroundOpenerContinuationValidatesOnlyDecodedRandomBlocks()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                VelkhanaBrain.SelectGroundOpenerContinuation(
                    VelkhanaNode087Leaf.Global009, 13f, -1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                VelkhanaBrain.SelectGroundOpenerContinuation(
                    VelkhanaNode087Leaf.Global004, 3f, 100));
            Assert.DoesNotThrow(() =>
                VelkhanaBrain.SelectGroundOpenerContinuation(
                    VelkhanaNode087Leaf.Global009, 13.001f, -1),
                "the >13 m source branch has no random block");
        }

        [TestCase(49, VelkhanaNode087Leaf.Global006)]
        [TestCase(50, VelkhanaNode087Leaf.Global009)]
        public void GlobalNode079NearArenaCenterUsesExactFiftyFiftyBoundary(
            int roll,
            VelkhanaNode087Leaf expected)
        {
            Assert.AreEqual(expected, VelkhanaBrain.SelectNode079NearLeaf(roll));
        }

        [TestCase(true, true, VelkhanaNode087Leaf.Global006)]
        [TestCase(true, false, VelkhanaNode087Leaf.Global009)]
        [TestCase(false, true, VelkhanaNode087Leaf.Global009)]
        [TestCase(false, false, VelkhanaNode087Leaf.Global006)]
        public void GlobalNode079BeyondArenaCenterGateUsesSectorParityWithoutRng(
            bool arenaCenterInSector,
            bool hunterInSector,
            VelkhanaNode087Leaf expected)
        {
            Assert.AreEqual(
                expected,
                VelkhanaBrain.SelectNode079FarLeaf(
                    arenaCenterInSector, hunterInSector));
        }

        [Test]
        public void GlobalNode079ClockwiseSectorWrapsInclusivelyFrom270Through90()
        {
            Assert.IsTrue(VelkhanaBrain.DirectionIsInClockwiseSector270To90(
                Vector3.forward, Vector3.right));
            Assert.IsTrue(VelkhanaBrain.DirectionIsInClockwiseSector270To90(
                Vector3.forward, Vector3.left));
            Assert.IsTrue(VelkhanaBrain.DirectionIsInClockwiseSector270To90(
                Vector3.forward, Vector3.forward));
            Assert.IsFalse(VelkhanaBrain.DirectionIsInClockwiseSector270To90(
                Vector3.forward, Vector3.back));
        }

        [Test]
        public void ProjectGroundResetPacingNeverDropsBelowReadabilityFloor()
        {
            Assert.AreEqual(
                VelkhanaBrain.ProjectMinimumGroundResetFrames,
                VelkhanaBrain.ProjectGroundResetPacingFrames(
                    1, false, false, false, 0.65f, 0.72f, 0.78f));
            Assert.AreEqual(
                VelkhanaBrain.ProjectMinimumGroundResetFrames,
                VelkhanaBrain.ProjectGroundResetPacingFrames(
                    42, true, true, true, 0.65f, 0.72f, 0.78f));
            Assert.AreEqual(
                60,
                VelkhanaBrain.ProjectGroundResetPacingFrames(
                    60, false, false, false, 0.65f, 0.72f, 0.78f));
        }

        [Test]
        public void VelkhanaPresentationPhaseWeightsCarryAndReturnToRest()
        {
            var firstActive = VelkhanaPresentation.EvaluateAttackPhase(12, 6, 18, 12);
            var midActive = VelkhanaPresentation.EvaluateAttackPhase(12, 6, 18, 14);
            var firstRecovery = VelkhanaPresentation.EvaluateAttackPhase(12, 6, 18, 18);
            var lastRecovery = VelkhanaPresentation.EvaluateAttackPhase(12, 6, 18, 35);
            var singleActive = VelkhanaPresentation.EvaluateAttackPhase(4, 1, 3, 4);

            Assert.Greater(firstActive.Anticipation, 0.55f);
            Assert.Greater(firstActive.Impact, 0.45f);
            Assert.Greater(
                midActive.Impact,
                firstActive.Impact,
                "impact should peak inside the active window");
            Assert.Greater(
                firstRecovery.FollowThrough,
                0.85f,
                "recovery should begin with lingering follow-through");
            Assert.Greater(firstRecovery.RecoveryEase, 0f);
            Assert.AreEqual(0f, lastRecovery.Anticipation, 0.0001f);
            Assert.AreEqual(0f, lastRecovery.Impact, 0.0001f);
            Assert.AreEqual(0f, lastRecovery.FollowThrough, 0.0001f);
            Assert.AreEqual(1f, lastRecovery.RecoveryEase, 0.0001f);
            Assert.AreEqual(1f, singleActive.Impact, 0.0001f);
        }
    }
}
