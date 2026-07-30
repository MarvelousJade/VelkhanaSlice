using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;

namespace VelkhanaSlice.PlayTests
{
    public class GreatSwordPlayTests
    {
        readonly List<AttackDefinition> _attacks = new List<AttackDefinition>();
        Gamepad _pad;

        [SetUp]
        public void SetUp()
        {
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_pad != null && _pad.added)
                InputSystem.RemoveDevice(_pad);

            foreach (var attack in _attacks)
                Object.DestroyImmediate(attack);
            _attacks.Clear();
        }

        AttackDefinition Attack(string name, bool hyperArmor = false)
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.name = name;
            attack.startupFrames = 1;
            attack.activeFrames = 1;
            attack.recoveryFrames = 2;
            attack.trackingCutoffFrame = 1;
            attack.cancelWindowStart = 0;
            attack.damage = 20f;
            attack.hyperArmor = hyperArmor;
            attack.incomingDamageReduction = hyperArmor ? 0.5f : 0f;
            attack.hitboxSize = Vector3.zero;
            attack.forwardMotionScale = 0f;
            _attacks.Add(attack);
            return attack;
        }

        HunterController MakeHunter(out GameObject root, bool withHealth = false)
        {
            root = new GameObject(
                "GreatSwordTestHunter",
                typeof(CharacterController),
                typeof(HunterController));
            if (withHealth) root.AddComponent<HunterHealth>();

            var hunter = root.GetComponent<HunterController>();
            hunter.chargeThresholds = new[] { 2, 4, 6 };
            hunter.overchargeFrames = 3;
            hunter.rollFrames = 8;
            hunter.rollInvulnStart = 2;
            hunter.rollInvulnEnd = 5;
            hunter.stationaryDraw = Attack("StationaryDraw");
            hunter.stationaryDraw.activeFrames = 0;
            hunter.stationaryDraw.damage = 0f;
            hunter.drawSlash = Attack("Draw");
            hunter.chargedSlash = Attack("ChargeRelease");
            hunter.strongChargedSlash = Attack("StrongRelease");
            hunter.trueChargedSlash = Attack("TcsOpening");
            hunter.trueChargedFinishNormal = Attack("TcsFinishNormal");
            hunter.trueChargedFinishLevel1 = Attack("TcsFinish1");
            hunter.trueChargedFinishLevel2 = Attack("TcsFinish2");
            hunter.trueChargedFinishLevel3 = Attack("TcsFinish3");
            hunter.wideSlash = Attack("Wide");
            hunter.strongWideSlash = Attack("StrongWide");
            hunter.leapingWideSlash = Attack("LeapingWide");
            hunter.wideSlashPostStrong = Attack("Wide2");
            hunter.risingSlash = Attack("Rising");
            hunter.risingSlashPostStrong = Attack("Rising2");
            hunter.sideBlow = Attack("Side");
            hunter.sideBlowPostStrong = Attack("Side2");
            hunter.tackle = Attack("Tackle", true);
            hunter.tackleLevel2 = Attack("Tackle2", true);
            hunter.kick = Attack("Kick");
            return hunter;
        }

        IEnumerator Step(
            bool primary = false,
            bool secondary = false,
            bool dodge = false,
            bool run = false,
            bool guard = false,
            Vector2? move = null)
        {
            var state = new GamepadState
            {
                leftStick = move ?? Vector2.zero,
                rightTrigger = guard ? 1f : 0f,
            };
            state = state.WithButton(GamepadButton.West, primary);
            state = state.WithButton(GamepadButton.North, secondary);
            state = state.WithButton(GamepadButton.East, dodge);
            state = state.WithButton(GamepadButton.LeftStick, run);
            InputSystem.QueueStateEvent(_pad, state);
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        IEnumerator DrawWeapon(HunterController hunter)
        {
            yield return Step(guard: true);
            Assert.AreEqual(HunterController.State.Guarding, hunter.CurrentState);
            yield return Step();
            Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
            Assert.IsTrue(hunter.WeaponDrawn);
        }

        [UnityTest]
        public IEnumerator StationaryDrawIsNonDamagingAndHoldRoutesIntoBasicCharge()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                Assert.IsFalse(hunter.WeaponDrawn);
                Assert.IsFalse(hunter.stationaryDraw.HasHitbox);
                Assert.AreEqual(0, hunter.stationaryDraw.activeFrames);
                Assert.AreEqual(0f, hunter.stationaryDraw.damage, 0.001f);

                yield return Step(primary: true);
                Assert.AreEqual(HunterController.Wp00Node.DrawStationary, hunter.CurrentNode);
                Assert.AreSame(hunter.stationaryDraw, hunter.CurrentAttack);
                Assert.AreNotSame(hunter.drawSlash, hunter.CurrentAttack);

                int budget = hunter.stationaryDraw.TotalFrames + 4;
                while (hunter.CurrentState == HunterController.State.Attacking && budget-- > 0)
                    yield return Step(primary: true);

                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.Basic, hunter.CurrentChargeStage);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator HeldPrimaryAfterMovingDrawDoesNotBecomeAFreshNeutralPress()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return Step(primary: true, move: Vector2.up);
                Assert.AreEqual(HunterController.Wp00Node.DrawMoving, hunter.CurrentNode);
                Assert.AreSame(hunter.drawSlash, hunter.CurrentAttack);

                int budget = hunter.drawSlash.TotalFrames + 4;
                while (hunter.CurrentState == HunterController.State.Attacking && budget-- > 0)
                    yield return Step(primary: true);

                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);

                // The same physical hold is still down, but no new press edge occurred.
                yield return Step(primary: true);
                yield return Step(primary: true);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.IsNull(hunter.CurrentAttack);
                Assert.AreEqual(HunterController.ChargeStage.None, hunter.CurrentChargeStage);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator NeutralChordAcceptsFreshSecondaryWhilePrimaryRemainsHeld()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return Step(primary: true, move: Vector2.up);
                Assert.AreEqual(HunterController.Wp00Node.DrawMoving, hunter.CurrentNode);

                int budget = hunter.drawSlash.TotalFrames + 4;
                while (hunter.CurrentState == HunterController.State.Attacking && budget-- > 0)
                    yield return Step(primary: true);

                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                yield return Step(primary: true, secondary: true);

                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.RisingSlash, hunter.CurrentNode);
                Assert.AreSame(hunter.risingSlash, hunter.CurrentAttack);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator NeutralChordAcceptsFreshPrimaryWhileSecondaryRemainsHeld()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);
                yield return Step(secondary: true);
                Assert.AreEqual(HunterController.Wp00Node.WideSlash, hunter.CurrentNode);

                int budget = hunter.wideSlash.TotalFrames + 4;
                while (hunter.CurrentState == HunterController.State.Attacking && budget-- > 0)
                    yield return Step(secondary: true);

                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                yield return Step(primary: true, secondary: true);

                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.RisingSlash, hunter.CurrentNode);
                Assert.AreSame(hunter.risingSlash, hunter.CurrentAttack);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator PendingRunSheatheIgnoresFreshSecondaryWhilePrimaryIsHeld()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                // Establish a continuing primary hold without creating another neutral edge.
                yield return Step(primary: true, move: Vector2.up);
                int budget = hunter.drawSlash.TotalFrames + 4;
                while (hunter.CurrentState == HunterController.State.Attacking && budget-- > 0)
                    yield return Step(primary: true);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);

                yield return Step(primary: true, run: true, move: Vector2.up);
                Assert.IsTrue(hunter.IsWeaponTransitioning);
                Assert.IsFalse(hunter.WeaponTransitionDrawn);

                // Secondary is the only new edge. The pending sheathe makes this effectively
                // sheathed, so neither the drawn chord nor the drawn WideSlash route is legal.
                yield return Step(
                    primary: true,
                    secondary: true,
                    run: true,
                    move: Vector2.up);

                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.AreNotEqual(HunterController.Wp00Node.WideSlash, hunter.CurrentNode);
                Assert.IsNull(hunter.CurrentAttack);
                Assert.IsTrue(hunter.IsWeaponTransitioning);
                Assert.IsFalse(hunter.WeaponTransitionDrawn);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator FreshPrimaryWithHeldSecondaryWhileSheathedUsesStationaryDraw()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                Assert.IsFalse(hunter.WeaponDrawn);

                // Secondary's edge is consumed while sheathed; it remains held for the next step.
                yield return Step(secondary: true);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.IsNull(hunter.CurrentAttack);

                yield return Step(primary: true, secondary: true);

                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.DrawStationary, hunter.CurrentNode);
                Assert.AreSame(hunter.stationaryDraw, hunter.CurrentAttack);
                Assert.AreNotSame(hunter.risingSlash, hunter.CurrentAttack);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator FullChargeChainRechargesAtEveryStage()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);

                yield return Step(primary: true);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.Basic, hunter.CurrentChargeStage);

                yield return Step(primary: true);
                yield return Step();
                Assert.AreEqual(
                    HunterController.Wp00Node.ChargeSlashRelease,
                    hunter.CurrentNode);

                // Buffer Triangle+lever and keep Triangle held so the strong hold persists.
                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.Strong, hunter.CurrentChargeStage);

                yield return Step(primary: true);
                yield return Step();
                Assert.AreEqual(
                    HunterController.Wp00Node.StrongChargeRelease,
                    hunter.CurrentNode);

                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                yield return Step(primary: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.True, hunter.CurrentChargeStage);

                yield return Step(primary: true);
                yield return Step();
                Assert.AreEqual(
                    HunterController.Wp00Node.TrueChargeFirstHit,
                    hunter.CurrentNode);
                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);

                // No hurtbox is present, so ActionNo 78 must continue into its normal second hit
                // rather than disappearing or selecting a connected FinishEx variant.
                yield return Step();
                yield return Step();
                yield return Step();
                yield return Step();
                Assert.AreEqual(
                    HunterController.Wp00Node.TrueChargeNormalFinish,
                    hunter.CurrentNode);
                Assert.AreSame(hunter.trueChargedFinishNormal, hunter.CurrentAttack);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ChargeRejectsRollAndTacklesAdvanceToDifferentTiers()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);
                yield return Step(primary: true);
                Assert.AreEqual(HunterController.ChargeStage.Basic, hunter.CurrentChargeStage);

                yield return Step(primary: true, dodge: true);
                Assert.AreEqual(
                    HunterController.State.Charging,
                    hunter.CurrentState,
                    "WP00 has no direct charge-to-roll edge");

                yield return Step(primary: true, secondary: true);
                Assert.AreEqual(HunterController.Wp00Node.Tackle, hunter.CurrentNode);
                Assert.AreSame(hunter.tackle, hunter.CurrentAttack);
                Assert.IsTrue(hunter.HasHyperArmor);

                // Release and press again to create the Kick/Tackle-style Triangle edge.
                yield return Step();
                yield return Step(primary: true);
                yield return Step(primary: true);
                yield return Step(primary: true);
                yield return Step(primary: true);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.Strong, hunter.CurrentChargeStage);

                yield return Step(primary: true, secondary: true);
                Assert.AreEqual(HunterController.Wp00Node.TackleLevel2, hunter.CurrentNode);
                Assert.AreSame(hunter.tackleLevel2, hunter.CurrentAttack);

                yield return Step();
                yield return Step(primary: true);
                yield return Step(primary: true);
                yield return Step(primary: true);
                yield return Step(primary: true);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.ChargeStage.True, hunter.CurrentChargeStage);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator PendingRunSheatheAttackUsesMovingDrawInsteadOfCharge()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);

                yield return Step(run: true, move: Vector2.up);
                Assert.IsTrue(hunter.IsWeaponTransitioning);
                Assert.IsFalse(hunter.WeaponTransitionDrawn);

                yield return Step(primary: true, run: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.DrawMoving, hunter.CurrentNode);
                Assert.AreSame(hunter.drawSlash, hunter.CurrentAttack);
                Assert.IsFalse(hunter.IsWeaponTransitioning);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator PendingRunSheatheDodgeBecomesAConsistentSheathedEvade()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);
                yield return Step(run: true, move: Vector2.up);
                Assert.IsTrue(hunter.IsWeaponTransitioning);
                Assert.IsFalse(hunter.WeaponTransitionDrawn);

                yield return Step(dodge: true, run: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Rolling, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.EvadeSheathed, hunter.CurrentNode);
                Assert.IsFalse(hunter.WeaponDrawn);
                Assert.IsFalse(hunter.IsWeaponTransitioning);

                for (int i = 0; i < hunter.rollFrames; i++)
                    yield return Step(move: Vector2.up);
                Assert.IsFalse(hunter.WeaponDrawn);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator GuardReducesDamageAndSheathedRollKeepsWeaponPutAway()
        {
            var hunter = MakeHunter(out var root, true);
            var health = root.GetComponent<HunterHealth>();
            try
            {
                yield return Step(guard: true);
                float before = health.Current;
                Assert.IsTrue(health.TakeDamage(100f));
                Assert.AreEqual(
                    before - 100f * hunter.guardDamageMultiplier,
                    health.Current,
                    0.001f);

                yield return Step();
                // Manually sheathe through the configured common-locomotion transition.
                yield return Step(run: true, move: Vector2.up);
                for (int i = 0; i <= hunter.sheatheFrames; i++)
                    yield return Step(run: true, move: Vector2.up);
                Assert.IsFalse(hunter.WeaponDrawn);

                yield return Step(dodge: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Rolling, hunter.CurrentState);
                Assert.IsFalse(hunter.WeaponDrawn);

                for (int i = 0; i < hunter.rollFrames - 1; i++)
                {
                    bool expected =
                        hunter.StateFrame >= hunter.rollInvulnStart &&
                        hunter.StateFrame < hunter.rollInvulnEnd;
                    Assert.AreEqual(expected, hunter.IsInvulnerable);
                    yield return Step(move: Vector2.up);
                }

                Assert.IsFalse(hunter.WeaponDrawn);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
