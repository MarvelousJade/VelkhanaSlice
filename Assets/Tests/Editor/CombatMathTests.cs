using NUnit.Framework;
using UnityEngine;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Tests
{
    /// <summary>
    /// Covers the frame-window and damage rules the acceptance criteria depend on.
    /// If these drift, timings no longer match reference footage.
    /// </summary>
    public class CombatMathTests
    {
        static AttackDefinition MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.startupFrames = 10;
            attack.activeFrames = 4;
            attack.recoveryFrames = 20;
            attack.trackingCutoffFrame = 8;
            attack.cancelWindowStart = -1;
            attack.damage = 100f;
            attack.staggerDamage = 40f;
            attack.chargeMultipliers = new[] { 1f, 1.4f, 1.8f, 2.4f };
            return attack;
        }

        [Test]
        public void ActiveFramesAreHalfOpen()
        {
            var attack = MakeAttack();

            Assert.AreEqual(34, attack.TotalFrames);
            Assert.IsFalse(attack.IsHitActive(9), "startup must not deal damage");
            Assert.IsTrue(attack.IsHitActive(10), "first active frame");
            Assert.IsTrue(attack.IsHitActive(13), "last active frame");
            Assert.IsFalse(attack.IsHitActive(14), "recovery must not deal damage");
        }

        [Test]
        public void TrackingStopsAtCutoffSoAttacksCommit()
        {
            var attack = MakeAttack();

            Assert.IsTrue(attack.CanTrack(7));
            Assert.IsFalse(attack.CanTrack(8), "cutoff frame itself is already committed");
            Assert.IsFalse(attack.CanTrack(20));
        }

        [Test]
        public void UncancellableAttackNeverCancels()
        {
            var attack = MakeAttack();

            for (int frame = 0; frame < attack.TotalFrames; frame++)
                Assert.IsFalse(attack.CanCancel(frame));
        }

        [Test]
        public void TrueChargedSlashLosesChargeScalingWithoutItsOpeningHit()
        {
            var attack = MakeAttack();
            attack.requiresPreviousHitConnected = true;

            Assert.AreEqual(240f, attack.DamageAt(3, true), 0.001f);
            Assert.AreEqual(100f, attack.DamageAt(3, false), 0.001f,
                "charge scaling must drop to level 0 when the opening hit missed");
        }

        [Test]
        public void ChargeLevelIsClampedToTheMultiplierTable()
        {
            var attack = MakeAttack();

            Assert.AreEqual(100f, attack.DamageAt(-1, true), 0.001f);
            Assert.AreEqual(240f, attack.DamageAt(99, true), 0.001f);
        }

        [Test]
        public void IceArmorAbsorbsDamageUntilItShatters()
        {
            var go = new GameObject("head", typeof(BoxCollider), typeof(BodyPartHurtbox));
            try
            {
                var hurtbox = go.GetComponent<BodyPartHurtbox>();
                hurtbox.part = BodyPart.Head;
                hurtbox.damageMultiplier = 1f;
                hurtbox.breakThreshold = 150f;
                hurtbox.iceArmorHealth = 120f;

                hurtbox.Apply(100f, 40f);
                Assert.IsTrue(hurtbox.HasIceArmor);
                Assert.AreEqual(0f, hurtbox.AccumulatedDamage, 0.001f, "armour absorbs, the body takes nothing");

                hurtbox.Apply(100f, 40f);
                Assert.IsFalse(hurtbox.HasIceArmor, "armour shatters and does not go negative");
                Assert.AreEqual(0f, hurtbox.iceArmorHealth, 0.001f);

                hurtbox.Apply(200f, 40f);
                Assert.IsTrue(hurtbox.IsBroken, "damage past the threshold breaks the part");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResolverScalesByPartMultiplierAndReportsTheBreak()
        {
            var attack = MakeAttack();
            var go = new GameObject("head", typeof(BoxCollider), typeof(BodyPartHurtbox));
            try
            {
                var hurtbox = go.GetComponent<BodyPartHurtbox>();
                hurtbox.part = BodyPart.Head;
                hurtbox.damageMultiplier = 1.5f;
                hurtbox.breakThreshold = 300f;

                HitResult first = DamageResolver.Resolve(attack, 2, true, hurtbox);
                Assert.AreEqual(270f, first.Damage, 0.001f, "100 base x1.8 charge x1.5 head");
                Assert.IsFalse(first.BrokePart);
                Assert.AreEqual(DamageResolver.BaseHitstopFrames + 4, first.HitstopFrames);

                HitResult second = DamageResolver.Resolve(attack, 0, true, hurtbox);
                Assert.IsTrue(second.BrokePart, "the break is reported on the hit that causes it");

                HitResult third = DamageResolver.Resolve(attack, 0, true, hurtbox);
                Assert.IsFalse(third.BrokePart, "and only on that hit");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComboGraphLinksAreTheFollowUpArray()
        {
            var opener = MakeAttack();
            var follow = MakeAttack();
            opener.followUps = new[] { follow };

            Assert.IsTrue(opener.CanFollowInto(follow));
            Assert.IsFalse(follow.CanFollowInto(opener));
            Assert.IsFalse(opener.CanFollowInto(null));
        }

        [Test]
        public void ChargeLevelClimbsWithHoldLengthAndDropsOnOvercharge()
        {
            var go = new GameObject("hunter");
            try
            {
                var hunter = go.AddComponent<VelkhanaSlice.Hunter.HunterController>();
                hunter.chargeThresholds = new[] { 40, 75, 110 };
                hunter.overchargeFrames = 45;

                Assert.AreEqual(0, hunter.ChargeLevelFor(0));
                Assert.AreEqual(0, hunter.ChargeLevelFor(39), "one frame short of level 1");
                Assert.AreEqual(1, hunter.ChargeLevelFor(40));
                Assert.AreEqual(2, hunter.ChargeLevelFor(75));
                Assert.AreEqual(3, hunter.ChargeLevelFor(110));
                Assert.AreEqual(3, hunter.ChargeLevelFor(154), "still full one frame before overcharge");
                Assert.AreEqual(1, hunter.ChargeLevelFor(155), "overcharge drops the swing back down");
                Assert.AreEqual(1, hunter.ChargeLevelFor(400));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ChargePresentationProgressesFromWhiteToYellowToRed()
        {
            int[] thresholds = { 40, 75, 110 };

            Color white = VelkhanaSlice.Hunter.HunterPresentation.ChargeGlowColor(20, thresholds);
            Color yellow = VelkhanaSlice.Hunter.HunterPresentation.ChargeGlowColor(75, thresholds);
            Color red = VelkhanaSlice.Hunter.HunterPresentation.ChargeGlowColor(110, thresholds);

            Assert.AreEqual(Color.white.r, white.r, 0.001f);
            Assert.AreEqual(Color.white.g, white.g, 0.001f);
            Assert.Greater(yellow.g, red.g, "the middle charge stage should read yellow");
            Assert.Greater(red.r, 0.95f);
            Assert.Less(red.g, 0.1f);
        }

        [Test]
        public void HyperArmorMoveReducesIncomingDamage()
        {
            var tackle = MakeAttack();
            tackle.hyperArmor = true;
            tackle.incomingDamageReduction = 0.5f;

            Assert.AreEqual(50f, DamageResolver.ResolveIncoming(100f, tackle), 0.001f);
            Assert.AreEqual(100f, DamageResolver.ResolveIncoming(100f, null), 0.001f);
        }

        [Test]
        public void ChargeStageAndHoldPowerAreIndependent()
        {
            var go = new GameObject("hunter");
            try
            {
                var hunter = go.AddComponent<VelkhanaSlice.Hunter.HunterController>();
                hunter.chargeThresholds = new[] { 2, 4, 6 };

                Assert.AreEqual(0, hunter.ChargeLevelFor(1));
                Assert.AreEqual(1, hunter.ChargeLevelFor(2));
                Assert.AreEqual(2, hunter.ChargeLevelFor(4));
                Assert.AreEqual(3, hunter.ChargeLevelFor(6));
                Assert.AreEqual(
                    VelkhanaSlice.Hunter.HunterController.ChargeStage.None,
                    hunter.CurrentChargeStage,
                    "reading hold power must never mutate the combo charge tier");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Wp00CoreChargeRoutesMatchDecodedNodes()
        {
            var h = typeof(VelkhanaSlice.Hunter.HunterController);
            Assert.IsNotNull(h);

            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Idle,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.IdleToCharge);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.IdleToCharge,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashHold,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Release,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashRelease);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashHold,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Tackle);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashRelease,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary |
                VelkhanaSlice.Hunter.HunterController.CoreInput.Direction,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeHold,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Release,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary |
                VelkhanaSlice.Hunter.HunterController.CoreInput.Direction,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeHold,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Release,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeFirstHit);
        }

        [Test]
        public void TackleActionNumberControlsProgression()
        {
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Tackle,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TackleLevel2,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Tackle,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.LeapingWideSlash);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TackleLevel2,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.LeapingWideSlash);
        }

        [Test]
        public void PostStrongBranchesStayDistinct()
        {
            var input = VelkhanaSlice.Hunter.HunterController.CoreInput.Primary;
            var secondary = VelkhanaSlice.Hunter.HunterController.CoreInput.Secondary;
            var direction = VelkhanaSlice.Hunter.HunterController.CoreInput.Direction;

            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease,
                input | direction,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeHold);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease,
                secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongWideSlash);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease,
                input,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.SideBlowPostStrong);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeRelease,
                input | secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.RisingSlashPostStrong);
        }

        [Test]
        public void KickOnlyPrimaryRoutesToTackle()
        {
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Kick,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Primary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Tackle);
            AssertRoute(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.Kick,
                VelkhanaSlice.Hunter.HunterController.CoreInput.Secondary,
                VelkhanaSlice.Hunter.HunterController.Wp00Node.NoTransition);
        }

        [Test]
        public void TrueChargedFinishUsesNormalMissOrPoweredConnectedVariant()
        {
            var hunter = typeof(VelkhanaSlice.Hunter.HunterController);
            Assert.IsNotNull(hunter);

            Assert.AreEqual(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeNormalFinish,
                VelkhanaSlice.Hunter.HunterController.ResolveTrueChargeFinish(3, false));
            Assert.AreEqual(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeFinishLevel1,
                VelkhanaSlice.Hunter.HunterController.ResolveTrueChargeFinish(1, true));
            Assert.AreEqual(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeFinishLevel2,
                VelkhanaSlice.Hunter.HunterController.ResolveTrueChargeFinish(2, true));
            Assert.AreEqual(
                VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeFinishLevel3,
                VelkhanaSlice.Hunter.HunterController.ResolveTrueChargeFinish(3, true));
        }

        [Test]
        public void Wp00IdentityConstantsRemainTraceable()
        {
            Assert.AreEqual(
                1013,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.ChargeSlashHold));
            Assert.AreEqual(
                1014,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.StrongChargeHold));
            Assert.AreEqual(
                102,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeHold));
            Assert.AreEqual(
                78,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeFirstHit));
            Assert.AreEqual(
                79,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.Tackle));
            Assert.AreEqual(
                80,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.TackleLevel2));
            Assert.AreEqual(
                81,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.LeapingWideSlash));
            Assert.AreEqual(
                5,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.DrawStationary));
            Assert.AreEqual(
                7,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.DrawMoving));
            Assert.AreEqual(
                1005,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.Evade));
            Assert.AreEqual(
                78,
                VelkhanaSlice.Hunter.HunterController.ActionNumberFor(
                    VelkhanaSlice.Hunter.HunterController.Wp00Node.TrueChargeNormalFinish));
        }

        static void AssertRoute(
            VelkhanaSlice.Hunter.HunterController.Wp00Node from,
            VelkhanaSlice.Hunter.HunterController.CoreInput input,
            VelkhanaSlice.Hunter.HunterController.Wp00Node expected)
        {
            Assert.AreEqual(
                expected,
                VelkhanaSlice.Hunter.HunterController.ResolveCoreTransition(from, input),
                $"{from} + {input}");
        }
    }
}
