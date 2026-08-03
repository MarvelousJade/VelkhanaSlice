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
    public class GreatSwordPlayTests : InputTestFixture
    {
        readonly List<AttackDefinition> _attacks = new List<AttackDefinition>();
        Gamepad _pad;
        Keyboard _keyboard;
        Mouse _mouse;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _pad = InputSystem.AddDevice<Gamepad>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad.MakeCurrent();
            _keyboard.MakeCurrent();
            _mouse.MakeCurrent();
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                if (_pad != null && _pad.added)
                    InputSystem.RemoveDevice(_pad);
                if (_keyboard != null && _keyboard.added)
                    InputSystem.RemoveDevice(_keyboard);
                if (_mouse != null && _mouse.added)
                    InputSystem.RemoveDevice(_mouse);

                foreach (var attack in _attacks)
                    Object.DestroyImmediate(attack);
                _attacks.Clear();
                _pad = null;
                _keyboard = null;
                _mouse = null;
            }
            finally
            {
                base.TearDown();
            }
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
        public IEnumerator AttackMovementInputSteersRootMotionUntilTrackingCutoffWithoutAddingDistance()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                hunter.turnDegreesPerSecond = 36000f;
                hunter.wideSlash.forwardMotion = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                hunter.wideSlash.forwardMotionScale = 2f;
                hunter.wideSlash.trackingCutoffFrame = 2;

                yield return DrawWeapon(hunter);
                yield return Step(secondary: true);
                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreSame(hunter.wideSlash, hunter.CurrentAttack);

                Vector3 start = root.transform.position;
                yield return Step(move: Vector2.right);

                Assert.Greater(root.transform.position.x, start.x,
                    "held direction should steer the attack's authored forward step");
                Assert.Less(Vector3.Angle(Vector3.right, root.transform.forward), 0.1f);

                yield return Step(move: Vector2.right);
                Assert.AreEqual(hunter.wideSlash.trackingCutoffFrame, hunter.AttackFrame);
                Vector3 committedHeading = root.transform.forward;

                // Once the cutoff is reached, changing the stick cannot turn or redirect the
                // remaining root motion. It also must not add free analog displacement.
                yield return Step(move: Vector2.up);
                yield return Step(move: Vector2.up);

                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.Less(Vector3.Angle(committedHeading, root.transform.forward), 0.1f);

                Vector3 displacement = root.transform.position - start;
                Vector2 horizontal = new Vector2(displacement.x, displacement.z);
                Assert.AreEqual(
                    hunter.wideSlash.forwardMotionScale,
                    horizontal.magnitude,
                    0.05f,
                    "analog input selects heading but the authored root-motion scale owns distance");
                Assert.Greater(displacement.x, 1.95f);
                Assert.AreEqual(0f, displacement.z, 0.05f,
                    "post-cutoff input must not redirect the committed attack");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        IEnumerator CompleteMovingDrawOvercharge(HunterController hunter)
        {
            yield return Step(primary: true, move: Vector2.up);
            Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
            Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);

            int budget = hunter.chargeThresholds[hunter.chargeThresholds.Length - 1] +
                         hunter.overchargeFrames + hunter.chargedSlash.TotalFrames + 12;
            while (!(hunter.CurrentState == HunterController.State.Free &&
                     hunter.CurrentNode == HunterController.Wp00Node.Idle) &&
                   budget-- > 0)
            {
                yield return Step(primary: true);
            }

            Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
            Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);
        }

        [Test]
        public void InputPressLatchRejectsSameUpdateAndAcceptsNextUpdate()
        {
            uint lastLatchedUpdate = uint.MaxValue;

            Assert.IsTrue(HunterController.LatchPressForInputUpdate(
                true, 100u, ref lastLatchedUpdate));
            Assert.IsFalse(HunterController.LatchPressForInputUpdate(
                true, 100u, ref lastLatchedUpdate));
            Assert.IsFalse(HunterController.LatchPressForInputUpdate(
                false, 101u, ref lastLatchedUpdate));
            Assert.IsTrue(HunterController.LatchPressForInputUpdate(
                true, 101u, ref lastLatchedUpdate));
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
        public IEnumerator HeldMovingDrawRemainsN021AndDoesNotTakeTheN003TackleEdge()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                _keyboard.MakeCurrent();
                _mouse.MakeCurrent();
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);
                Assert.IsNull(hunter.CurrentAttack);
                int before = hunter.ChargeFrames;

                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState()
                        .WithButton(MouseButton.Left)
                        .WithButton(MouseButton.Right));
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.Greater(hunter.ChargeFrames, before);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);
                Assert.IsNull(hunter.CurrentAttack);
                Assert.AreNotEqual(HunterController.Wp00Node.Tackle, hunter.CurrentNode,
                    "N021 has no Circle transition to N041");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator HeldVirtualMouseMovingDrawChargesAndReleaseEntersN001Once()
        {
            var hunter = MakeHunter(out var root);

            try
            {
                hunter.chargeThresholds = new[] { 4, 12, 24 };
                hunter.overchargeFrames = 20;
                hunter.chargedSlash.startupFrames = 5;
                hunter.chargedSlash.activeFrames = 3;
                hunter.chargedSlash.recoveryFrames = 8;

                _keyboard.MakeCurrent();
                _mouse.MakeCurrent();
                Assert.AreSame(_keyboard, Keyboard.current);
                Assert.AreSame(_mouse, Mouse.current);
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));

                int startBudget = 12;
                while (hunter.CurrentNode != HunterController.Wp00Node.MovingDrawToVerticalSlash &&
                       startBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);
                Assert.AreEqual(HunterController.ChargeStage.Basic, hunter.CurrentChargeStage);
                Assert.IsNull(hunter.CurrentAttack);
                Assert.IsTrue(_mouse.leftButton.isPressed);

                int startingChargeFrames = hunter.ChargeFrames;
                int chargeBudget = 30;
                while (hunter.ChargeLevel < 2 && chargeBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode,
                    "held N021 must not route through N023/N003");
                Assert.Greater(hunter.ChargeFrames, startingChargeFrames);
                Assert.GreaterOrEqual(hunter.ChargeLevel, 2);
                int heldLevel = hunter.ChargeLevel;

                InputSystem.QueueStateEvent(_mouse, new MouseState());
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.VerticalSlash, hunter.CurrentNode);
                Assert.AreSame(hunter.chargedSlash, hunter.CurrentAttack);
                Assert.GreaterOrEqual(hunter.ChargeLevel, heldLevel,
                    "compressed N031 must not reset N021's charge power entering N001");
                Assert.AreEqual(
                    hunter.ChargeLevelFor(hunter.ChargeFrames), hunter.ChargeLevel);

                int releaseEntries = 1;
                HunterController.Wp00Node previousNode = hunter.CurrentNode;
                int completionBudget = hunter.chargedSlash.TotalFrames + 12;
                while (hunter.CurrentState != HunterController.State.Free &&
                       completionBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();

                    if (hunter.CurrentNode == HunterController.Wp00Node.VerticalSlash &&
                        previousNode != HunterController.Wp00Node.VerticalSlash)
                        releaseEntries++;
                    previousNode = hunter.CurrentNode;
                }

                Assert.AreEqual(1, releaseEntries);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator HeldVirtualMouseMovingDrawOverchargesOnceThenRequiresNewPress()
        {
            var hunter = MakeHunter(out var root);

            try
            {
                hunter.chargeThresholds = new[] { 4, 8, 12 };
                hunter.overchargeFrames = 6;
                hunter.chargedSlash.startupFrames = 5;
                hunter.chargedSlash.activeFrames = 3;
                hunter.chargedSlash.recoveryFrames = 8;
                _keyboard.MakeCurrent();
                _mouse.MakeCurrent();

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));

                int startBudget = 12;
                while (hunter.CurrentNode != HunterController.Wp00Node.MovingDrawToVerticalSlash &&
                       startBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);

                int releaseEntries = 0;
                HunterController.Wp00Node previousNode = hunter.CurrentNode;
                int sequenceBudget =
                    hunter.chargeThresholds[hunter.chargeThresholds.Length - 1] +
                    hunter.overchargeFrames + hunter.chargedSlash.TotalFrames + 20;
                while (!(hunter.CurrentState == HunterController.State.Free &&
                         hunter.CurrentNode == HunterController.Wp00Node.Idle) &&
                       sequenceBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();

                    if (hunter.CurrentNode == HunterController.Wp00Node.VerticalSlash &&
                        previousNode != HunterController.Wp00Node.VerticalSlash)
                    {
                        releaseEntries++;
                        Assert.AreSame(hunter.chargedSlash, hunter.CurrentAttack);
                        Assert.AreEqual(1, hunter.ChargeLevel,
                            "project overcharge compresses N031 into N001 at reduced level one");
                    }
                    previousNode = hunter.CurrentNode;
                }

                Assert.AreEqual(1, releaseEntries);
                Assert.IsTrue(_mouse.leftButton.isPressed);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);

                // No new device event: the same physical press must not manufacture another N021.
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                    Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                    Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);
                    Assert.IsNull(hunter.CurrentAttack);
                }

                // A real release/new press is accepted. N001 left the weapon drawn, so the fresh
                // drawn-idle press correctly enters N074 rather than manufacturing another N021;
                // releasing N074 then enters N001 without passing through N003.
                InputSystem.QueueStateEvent(_mouse, new MouseState());
                yield return null;
                yield return new WaitForFixedUpdate();
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.IsTrue(hunter.WeaponDrawn);
                Assert.AreEqual(HunterController.Wp00Node.IdleToCharge, hunter.CurrentNode);
                InputSystem.QueueStateEvent(_mouse, new MouseState());
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.VerticalSlash, hunter.CurrentNode);
                Assert.AreSame(hunter.chargedSlash, hunter.CurrentAttack);
                Assert.AreNotEqual(HunterController.Wp00Node.ChargeSlashHold, hunter.CurrentNode);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator HeldVirtualMouseStationaryDrawOverchargesOnceThenRequiresNewPress()
        {
            var hunter = MakeHunter(out var root);

            try
            {
                // Stretch every node this test must observe so fixed-step catch-up cannot pass
                // through N022, N003 or N001 between coroutine assertions.
                hunter.stationaryDraw.startupFrames = 5;
                hunter.stationaryDraw.activeFrames = 0;
                hunter.stationaryDraw.recoveryFrames = 8;
                hunter.chargedSlash.startupFrames = 5;
                hunter.chargedSlash.activeFrames = 3;
                hunter.chargedSlash.recoveryFrames = 8;
                hunter.chargeThresholds = new[] { 4, 8, 12 };
                hunter.overchargeFrames = 6;
                _mouse.MakeCurrent();
                Assert.AreSame(_mouse, Mouse.current);

                // One physical LMB press remains held for the complete N022 -> N003 -> N001 path.
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));

                int drawBudget = hunter.stationaryDraw.TotalFrames + 12;
                while (hunter.CurrentAttack != hunter.stationaryDraw && drawBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(HunterController.Wp00Node.DrawStationary, hunter.CurrentNode);
                Assert.AreEqual(0f, hunter.stationaryDraw.damage, 0.001f);
                Assert.IsFalse(hunter.stationaryDraw.HasHitbox);

                bool sawChargeHold = false;
                int chargeReleaseEntries = 0;
                HunterController.Wp00Node previousNode = hunter.CurrentNode;

                int forcedReleaseFrame =
                    hunter.chargeThresholds[hunter.chargeThresholds.Length - 1] +
                    hunter.overchargeFrames;
                int sequenceBudget =
                    hunter.stationaryDraw.TotalFrames +
                    forcedReleaseFrame +
                    hunter.chargedSlash.TotalFrames +
                    16;
                while (!(hunter.CurrentState == HunterController.State.Free &&
                         hunter.CurrentNode == HunterController.Wp00Node.Idle) &&
                       sequenceBudget-- > 0)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();

                    if (hunter.CurrentNode == HunterController.Wp00Node.ChargeSlashHold)
                        sawChargeHold = true;
                    if (hunter.CurrentNode == HunterController.Wp00Node.VerticalSlash &&
                        previousNode != HunterController.Wp00Node.VerticalSlash)
                    {
                        chargeReleaseEntries++;
                        Assert.AreSame(hunter.chargedSlash, hunter.CurrentAttack);
                    }

                    previousNode = hunter.CurrentNode;
                }

                Assert.IsTrue(sawChargeHold,
                    "continuing the original hold must sustain the N003 basic charge");
                Assert.AreEqual(1, chargeReleaseEntries,
                    "forced overcharge must enter N001 exactly once for one physical press");
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);
                Assert.IsNull(hunter.CurrentAttack);

                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();

                    Assert.IsTrue(_mouse.leftButton.isPressed);
                    Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                    Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);
                    Assert.IsNull(hunter.CurrentAttack);
                }

                // Releasing creates no action edge.
                InputSystem.QueueStateEvent(_mouse, new MouseState());
                yield return null;
                yield return new WaitForFixedUpdate();
                Assert.IsFalse(_mouse.leftButton.isPressed);
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.Idle, hunter.CurrentNode);

                // A second physical press is the positive control: it must enter the drawn
                // neutral primary route instead of being suppressed by the dedupe latch.
                InputSystem.QueueStateEvent(
                    _mouse,
                    new MouseState().WithButton(MouseButton.Left));
                yield return null;
                yield return new WaitForFixedUpdate();

                Assert.IsTrue(_mouse.leftButton.isPressed);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.IdleToCharge, hunter.CurrentNode);
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
                yield return CompleteMovingDrawOvercharge(hunter);
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
                yield return CompleteMovingDrawOvercharge(hunter);

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
                    HunterController.Wp00Node.VerticalSlash,
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
                    HunterController.Wp00Node.StrongVerticalSlash,
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
            var hunter = MakeHunter(out var root, true);
            var health = root.GetComponent<HunterHealth>();
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

                Assert.IsTrue(health.TakeDamage(20f, new Vector3(0f, 8f, 6f), 30));
                Assert.AreEqual(HunterController.State.Attacking, hunter.CurrentState);
                Assert.IsTrue(hunter.HasHyperArmor,
                    "tackle hyper armour should resist launch as well as ordinary interruption");

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
        public IEnumerator PendingRunSheatheAttackUsesMovingDrawCharge()
        {
            var hunter = MakeHunter(out var root);
            try
            {
                yield return DrawWeapon(hunter);

                yield return Step(run: true, move: Vector2.up);
                Assert.IsTrue(hunter.IsWeaponTransitioning);
                Assert.IsFalse(hunter.WeaponTransitionDrawn);

                yield return Step(primary: true, run: true, move: Vector2.up);
                Assert.AreEqual(HunterController.State.Charging, hunter.CurrentState);
                Assert.AreEqual(HunterController.Wp00Node.MovingDrawToVerticalSlash, hunter.CurrentNode);
                Assert.AreEqual(HunterController.ChargeStage.Basic, hunter.CurrentChargeStage);
                Assert.IsNull(hunter.CurrentAttack);
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
                Assert.IsTrue(health.TakeDamage(100f, new Vector3(0f, 8f, 6f), 30));
                Assert.AreEqual(
                    before - 100f * hunter.guardDamageMultiplier,
                    health.Current,
                    0.001f);
                Assert.AreEqual(HunterController.State.Guarding, hunter.CurrentState,
                    "a guarded launch hit should not knock the hunter out of guard");

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

        [UnityTest]
        public IEnumerator InvulnerableRollRejectsDamageAndLaunchReaction()
        {
            var hunter = MakeHunter(out var root, true);
            var health = root.GetComponent<HunterHealth>();
            try
            {
                yield return Step(dodge: true, move: Vector2.up);
                while (hunter.StateFrame < hunter.rollInvulnStart)
                    yield return Step(move: Vector2.up);

                Assert.IsTrue(hunter.IsInvulnerable);
                float before = health.Current;
                Assert.IsFalse(health.TakeDamage(50f, new Vector3(0f, 8f, 6f), 30));
                Assert.AreEqual(before, health.Current, 0.001f);
                Assert.AreEqual(HunterController.State.Rolling, hunter.CurrentState,
                    "a rejected hit must not apply its launch metadata");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator OneSwingSelectsOneHitzoneAcrossOverlappingBodyParts()
        {
            const int hurtboxLayer = 31;
            var hunter = MakeHunter(out var hunterRoot);
            var monsterRoot = new GameObject("SharedPartMonster");
            var brain = monsterRoot.AddComponent<VelkhanaSlice.Monster.VelkhanaBrain>();
            brain.enabled = false;

            var leftObject = new GameObject(
                "FrontLegL", typeof(BoxCollider), typeof(BodyPartHurtbox));
            var rightObject = new GameObject(
                "FrontLegR", typeof(BoxCollider), typeof(BodyPartHurtbox));
            var torsoObject = new GameObject(
                "Torso", typeof(BoxCollider), typeof(BodyPartHurtbox));
            leftObject.layer = rightObject.layer = torsoObject.layer = hurtboxLayer;
            leftObject.transform.SetParent(monsterRoot.transform, false);
            rightObject.transform.SetParent(monsterRoot.transform, false);
            torsoObject.transform.SetParent(monsterRoot.transform, false);
            leftObject.transform.position = new Vector3(-0.35f, 1f, 1.5f);
            rightObject.transform.position = new Vector3(0.35f, 1f, 1.5f);
            torsoObject.transform.position = new Vector3(0f, 1f, 2.5f);

            BodyPartHurtbox left = leftObject.GetComponent<BodyPartHurtbox>();
            BodyPartHurtbox right = rightObject.GetComponent<BodyPartHurtbox>();
            BodyPartHurtbox torso = torsoObject.GetComponent<BodyPartHurtbox>();
            left.part = right.part = BodyPart.FrontLeg;
            torso.part = BodyPart.Torso;
            left.breakThreshold = right.breakThreshold = 9999f;
            torso.breakThreshold = 9999f;
            left.staggerThreshold = right.staggerThreshold = 9999f;
            torso.staggerThreshold = 9999f;
            leftObject.GetComponent<BoxCollider>().isTrigger = true;
            rightObject.GetComponent<BoxCollider>().isTrigger = true;
            torsoObject.GetComponent<BoxCollider>().isTrigger = true;

            hunter.hurtboxLayers = 1 << hurtboxLayer;
            hunter.wideSlash.hitboxCenter = new Vector3(0f, 1f, 1.5f);
            hunter.wideSlash.hitboxSize = new Vector3(3f, 3f, 3f);
            hunter.wideSlash.staggerDamage = 60f;
            brain.RefreshHurtboxBindings();

            try
            {
                yield return DrawWeapon(hunter);
                float healthBefore = brain.CurrentHealth;
                yield return Step(secondary: true);
                while (hunter.CurrentState == HunterController.State.Attacking &&
                       hunter.AttackFrame <= hunter.wideSlash.startupFrames)
                    yield return Step();

                Assert.AreEqual(healthBefore - hunter.wideSlash.damage, brain.CurrentHealth, 0.001f,
                    "overlapping left/right colliders must not apply duplicate boss damage");
                Assert.AreEqual(60f, brain.GetAccumulatedStagger(BodyPart.FrontLeg), 0.001f,
                    "one authored swing must feed the shared BodyPart gauge once");
                Assert.AreEqual(0f, brain.GetAccumulatedStagger(BodyPart.Torso), 0.001f,
                    "one authored swing must not advance several decoded part gauges");
            }
            finally
            {
                Object.DestroyImmediate(monsterRoot);
                Object.DestroyImmediate(hunterRoot);
            }
        }

        [UnityTest]
        public IEnumerator GroundedKnockdownRejectsRehitAndRecoversControl()
        {
            var hunter = MakeHunter(out var root, true);
            var health = root.GetComponent<HunterHealth>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "KnockdownFloor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            root.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();

            try
            {
                yield return null;
                hunter.Launch(new Vector3(0f, 3f, 0f), 4);
                float airborneHealth = health.Current;
                Assert.IsTrue(health.TakeDamage(5f, new Vector3(0f, 50f, 0f), 1000));
                Assert.AreEqual(airborneHealth - 5f, health.Current, 0.001f,
                    "an airborne hunter remains damageable");
                Assert.IsFalse(hunter.IsKnockedDown);

                int waited = 0;
                while (!hunter.IsKnockedDown && waited++ < 120)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(hunter.IsKnockedDown,
                    "the second hit restarted the airborne arc instead of preserving the first launch; " +
                    $"state={hunter.CurrentState} frame={hunter.StateFrame} y={root.transform.position.y:0.000} " +
                    $"grounded={root.GetComponent<CharacterController>().isGrounded}");
                float before = health.Current;
                Assert.IsFalse(health.TakeDamage(25f, new Vector3(0f, 6f, 4f), 40));
                Assert.AreEqual(before, health.Current, 0.001f);
                Assert.IsTrue(hunter.IsKnockedDown,
                    "a rehit must not restart the launch or grounded recovery timer");

                waited = 0;
                while (hunter.CurrentState != HunterController.State.Free && waited++ < 10)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(HunterController.State.Free, hunter.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(root);
            }
        }
    }
}
