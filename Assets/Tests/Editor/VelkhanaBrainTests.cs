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
    }
}
