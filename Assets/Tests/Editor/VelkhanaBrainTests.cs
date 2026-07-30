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
    }
}
