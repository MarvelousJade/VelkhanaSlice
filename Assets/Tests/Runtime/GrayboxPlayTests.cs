using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;
using VelkhanaSlice.DebugTools;

namespace VelkhanaSlice.PlayTests
{
    /// <summary>
    /// Drives the simulation through real fixed frames. The edit-mode tests only cover pure maths;
    /// these are what prove the MonoBehaviours actually run.
    /// </summary>
    public class GrayboxPlayTests
    {
        const int FrameBudget = 2000;

        static AttackDefinition MakeAttack(int startup, int active, int recovery, int cutoff)
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.startupFrames = startup;
            attack.activeFrames = active;
            attack.recoveryFrames = recovery;
            attack.trackingCutoffFrame = cutoff;
            attack.cancelWindowStart = -1;
            attack.damage = 50f;
            attack.staggerDamage = 0f;
            attack.forwardMotion = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            attack.forwardMotionScale = 0f;
            return attack;
        }

        static VelkhanaBrain MakeBrain(out GameObject root, out GameObject hunter, AttackDefinition attack)
        {
            hunter = new GameObject("HunterStandIn");
            hunter.transform.position = new Vector3(0f, 0f, 3f);

            root = new GameObject("Velkhana");
            var brain = root.AddComponent<VelkhanaBrain>();
            brain.hunter = hunter.transform;
            brain.neutralFrames = 5;
            brain.closeRange = 6f;
            brain.mediumRange = 16f;
            brain.options.Add(new MonsterAttackOption
            {
                attack = attack,
                band = RangeBand.Close,
                minimumStage = ArmorStage.Neutral,
                weight = 1f,
                cooldownFrames = 120,
                requiresHunterInFront = false,
            });
            return brain;
        }

        [UnityTest]
        public IEnumerator BrainStartsAnAttackAndPlaysItToCompletion()
        {
            var attack = MakeAttack(12, 4, 18, 8);
            var brain = MakeBrain(out var root, out var hunter, attack);

            try
            {
                int waited = 0;
                while (brain.CurrentAttack == null && waited++ < FrameBudget)
                    yield return new WaitForFixedUpdate();

                Assert.IsNotNull(brain.CurrentAttack, "brain never chose an attack");

                // Velkhana must never cancel: the frame counter climbs to the end, then clears.
                int previous = brain.AttackFrame;
                int steps = 0;
                while (brain.CurrentAttack != null && steps++ < FrameBudget)
                {
                    Assert.GreaterOrEqual(brain.AttackFrame, previous, "attack frame went backwards");
                    Assert.LessOrEqual(brain.AttackFrame, attack.TotalFrames, "attack ran past its length");
                    previous = brain.AttackFrame;
                    yield return new WaitForFixedUpdate();
                }

                Assert.IsNull(brain.CurrentAttack, "attack never finished");
                Assert.AreEqual(attack.TotalFrames, previous + 1,
                    "attack should end exactly on its last frame");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator DebugHitboxUsesTheLastQueriedPhysicsFrameAtPhaseBoundaries()
        {
            var attack = MakeAttack(2, 2, 4, 1);
            var brain = MakeBrain(out var root, out var hunter, attack);
            brain.neutralFrames = 1;

            try
            {
                int waited = 0;
                while (brain.CurrentAttack == null && waited++ < FrameBudget)
                    yield return new WaitForFixedUpdate();

                while (brain.LastSimulatedAttackFrame < attack.startupFrames - 1)
                    yield return new WaitForFixedUpdate();

                int displayed = CombatVolumeDebug.SimulatedFrameForDisplay(
                    brain.AttackFrame, brain.LastSimulatedAttackFrame);
                Assert.IsFalse(attack.IsHitActive(displayed),
                    "the overlay must remain in startup before the first damaging query");
                Assert.IsTrue(attack.IsHitActive(brain.AttackFrame),
                    "this boundary specifically guards against displaying the incremented frame");

                while (brain.LastSimulatedAttackFrame <
                       attack.startupFrames + attack.activeFrames - 1)
                    yield return new WaitForFixedUpdate();

                displayed = CombatVolumeDebug.SimulatedFrameForDisplay(
                    brain.AttackFrame, brain.LastSimulatedAttackFrame);
                Assert.IsTrue(attack.IsHitActive(displayed),
                    "the final queried active frame must remain red until the next fixed tick");
                Assert.IsFalse(attack.IsHitActive(brain.AttackFrame));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator BrainWaitsOutTheCooldownBeforeRepeatingItsOnlyAttack()
        {
            var attack = MakeAttack(6, 2, 6, 4);
            var brain = MakeBrain(out var root, out var hunter, attack);
            brain.options[0].cooldownFrames = 200;

            try
            {
                int waited = 0;
                while (brain.CurrentAttack == null && waited++ < FrameBudget)
                    yield return new WaitForFixedUpdate();
                Assert.IsNotNull(brain.CurrentAttack);

                while (brain.CurrentAttack != null)
                    yield return new WaitForFixedUpdate();

                // Only one option exists and it is on cooldown, so nothing may start yet.
                for (int i = 0; i < 60; i++)
                {
                    Assert.IsNull(brain.CurrentAttack, $"attack repeated on cooldown frame {i}");
                    yield return new WaitForFixedUpdate();
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator BreakingEnoughArmorEndsThePoweredStage()
        {
            var attack = MakeAttack(6, 2, 6, 4);
            var brain = MakeBrain(out var root, out var hunter, attack);

            var parts = new BodyPartHurtbox[2];
            for (int i = 0; i < parts.Length; i++)
            {
                var go = new GameObject($"armored{i}", typeof(BoxCollider), typeof(BodyPartHurtbox));
                go.transform.SetParent(root.transform, false);
                parts[i] = go.GetComponent<BodyPartHurtbox>();
            }

            brain.armoredParts = parts;
            brain.armorPerPart = 100f;
            brain.armorBreaksToInterrupt = 2;

            try
            {
                // OnEnable already ran, so re-subscribe by cycling the component.
                brain.enabled = false;
                brain.enabled = true;
                yield return new WaitForFixedUpdate();

                brain.AdvanceStage();
                Assert.AreEqual(ArmorStage.IceArmorStage1, brain.stage);
                Assert.IsTrue(parts[0].HasIceArmor, "advancing a stage must apply ice armour");

                parts[0].Apply(500f, 0f);
                Assert.AreEqual(ArmorStage.IceArmorStage1, brain.stage, "one break is not enough");

                parts[1].Apply(500f, 0f);
                Assert.AreEqual(ArmorStage.Neutral, brain.stage,
                    "enough armour breaks must interrupt the powered stage");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator TorsoBreakTopplesOnceForTheConfiguredExactDuration()
        {
            var attack = MakeAttack(6, 2, 6, 4);
            var brain = MakeBrain(out var root, out var hunter, attack);
            var partObject = new GameObject("torso", typeof(BoxCollider), typeof(BodyPartHurtbox));
            partObject.transform.SetParent(root.transform, false);
            var torso = partObject.GetComponent<BodyPartHurtbox>();
            torso.part = BodyPart.Torso;
            torso.breakThreshold = 10f;
            torso.toppleOnBreak = true;
            torso.staggerThreshold = 0f;
            brain.partBreakToppleFrames = 4;
            brain.neutralFrames = 1000;
            brain.RefreshHurtboxBindings();

            try
            {
                torso.Apply(10f, 0f);
                Assert.AreEqual(VelkhanaState.Toppled, brain.CurrentState);
                Assert.AreEqual(VelkhanaToppleCause.PartBreak, brain.CurrentToppleCause);
                Assert.AreEqual(4, brain.ToppleFramesRemaining);

                for (int frame = 1; frame < brain.partBreakToppleFrames; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    Assert.AreEqual(VelkhanaState.Toppled, brain.CurrentState,
                        $"topple ended early on fixed frame {frame}");
                }

                yield return new WaitForFixedUpdate();
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);

                torso.Apply(50f, 0f);
                Assert.AreNotEqual(VelkhanaState.Toppled, brain.CurrentState,
                    "an already-broken part must not emit another break topple");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator LeftAndRightHitsShareAndConsumeOneBodyPartStaggerGauge()
        {
            var attack = MakeAttack(6, 2, 6, 4);
            var brain = MakeBrain(out var root, out var hunter, attack);
            brain.neutralFrames = 1000;

            var leftObject = new GameObject("frontLegL", typeof(BoxCollider), typeof(BodyPartHurtbox));
            var rightObject = new GameObject("frontLegR", typeof(BoxCollider), typeof(BodyPartHurtbox));
            leftObject.transform.SetParent(root.transform, false);
            rightObject.transform.SetParent(root.transform, false);
            var left = leftObject.GetComponent<BodyPartHurtbox>();
            var right = rightObject.GetComponent<BodyPartHurtbox>();
            left.part = right.part = BodyPart.FrontLeg;
            left.breakThreshold = right.breakThreshold = 9999f;
            left.staggerThreshold = right.staggerThreshold = 100f;
            left.toppleOnStagger = right.toppleOnStagger = true;
            brain.RefreshHurtboxBindings();
            yield return null;

            try
            {
                left.Apply(1f, 60f);
                Assert.AreEqual(60f, brain.GetAccumulatedStagger(BodyPart.FrontLeg), 0.001f);
                Assert.AreNotEqual(VelkhanaState.Toppled, brain.CurrentState);

                right.Apply(1f, 40f);
                Assert.AreEqual(VelkhanaState.Toppled, brain.CurrentState,
                    "left and right colliders must feed the same decoded BodyPart gauge");
                Assert.AreEqual(VelkhanaToppleCause.StaggerThreshold, brain.CurrentToppleCause);
                Assert.AreEqual(0f, brain.GetAccumulatedStagger(BodyPart.FrontLeg), 0.001f);
                Assert.AreEqual(0f, left.AccumulatedStagger, 0.001f);
                Assert.AreEqual(0f, right.AccumulatedStagger, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        [UnityTest]
        public IEnumerator BrainTurnsAroundInsteadOfDeadlockingWhenTheHunterIsBehindIt()
        {
            var attack = MakeAttack(6, 2, 6, 4);
            var brain = MakeBrain(out var root, out var hunter, attack);

            // Every option needs the hunter in front, and Velkhana starts facing the other way.
            brain.options[0].requiresHunterInFront = true;
            brain.idleTurnDegreesPerSecond = 180f;
            root.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            hunter.transform.position = new Vector3(0f, 0f, 4f);

            try
            {
                int waited = 0;
                while (brain.CurrentAttack == null && waited++ < FrameBudget)
                    yield return new WaitForFixedUpdate();

                Assert.IsNotNull(brain.CurrentAttack,
                    "brain never turned to face the hunter, so no option ever became legal");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
            }
        }

        static HunterHealth MakeHunterTarget(Vector3 position, int layer)
        {
            var go = new GameObject("Hunter", typeof(CharacterController), typeof(HunterController), typeof(HunterHealth));
            go.layer = layer;
            go.transform.position = position;

            // There is no ground in these tests, so stop the controller stepping and falling away
            // from the hitbox. HunterHealth still reads its state.
            go.GetComponent<HunterController>().enabled = false;

            return go.GetComponent<HunterHealth>();
        }

        [UnityTest]
        public IEnumerator VelkhanaDamagesTheHunterOnceOnHerActiveFrames()
        {
            var attack = MakeAttack(4, 3, 6, 2);
            attack.damage = 25f;
            attack.hunterLaunchVelocity = new Vector3(0f, 7f, 8f);
            attack.hunterKnockdownFrames = 30;
            attack.hitboxCenter = new Vector3(0f, 1f, 4f);
            attack.hitboxSize = new Vector3(6f, 3f, 10f);

            // An earlier test loads the graybox scene, so these objects share it. Use an exclusive
            // layer to keep the query off Velkhana's own hurtboxes.
            const int isolatedLayer = 31;

            var health = MakeHunterTarget(new Vector3(0f, 1f, 4f), isolatedLayer);
            var brain = MakeBrain(out var root, out var standIn, attack);
            brain.hunter = health.transform;
            brain.hunterLayers = 1 << isolatedLayer;
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            try
            {
                int waited = 0;
                int attacksStarted = 0;
                int activeFramesSeen = 0;
                int overlapHits = 0;
                var buffer = new Collider[8];
                AttackDefinition previous = null;

                while (health.Current >= health.maxHealth && waited++ < FrameBudget)
                {
                    var current = brain.CurrentAttack;
                    if (current != null && current != previous) attacksStarted++;
                    previous = current;

                    if (current != null && current.IsHitActive(brain.AttackFrame))
                    {
                        activeFramesSeen++;
                        overlapHits += AttackHitbox.Overlap(root.transform, current, brain.hunterLayers, buffer);
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.Less(health.Current, health.maxHealth,
                    $"no damage. attacksStarted={attacksStarted} activeFrames={activeFramesSeen} overlaps={overlapHits}");
                Assert.AreEqual(health.maxHealth - 25f, health.Current, 0.001f,
                    "an attack must land once, not once per active frame");
                Assert.AreEqual(HunterController.State.Launched,
                    health.GetComponent<HunterController>().CurrentState,
                    "the accepted hit should apply its launch reaction once with the damage");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(standIn);
                Object.DestroyImmediate(health.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator HitInterruptsASwingButNotAHyperArmourMove()
        {
            var health = MakeHunterTarget(Vector3.zero, 0);
            yield return null;

            var swing = MakeAttack(4, 2, 8, 2);
            var tackle = MakeAttack(4, 2, 8, 2);
            tackle.hyperArmor = true;
            tackle.incomingDamageReduction = 0.5f;

            try
            {
                float before = health.Current;
                Assert.IsTrue(health.TakeDamage(40f), "a standing hunter should take the hit");
                Assert.AreEqual(before - 40f, health.Current, 0.001f);

                // Reduction only applies while a hyper-armour move is actually playing.
                before = health.Current;
                health.TakeDamage(40f);
                Assert.AreEqual(before - 40f, health.Current, 0.001f,
                    "no reduction outside a hyper-armour move");
            }
            finally
            {
                Object.DestroyImmediate(health.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator GrayboxSceneIsFullyWired()
        {
            SceneManager.LoadScene("Graybox", LoadSceneMode.Single);
            yield return null;
            yield return new WaitForFixedUpdate();

            var hunter = Object.FindFirstObjectByType<HunterController>();
            Assert.IsNotNull(hunter, "no HunterController in the graybox scene");
            Assert.IsNotNull(hunter.stationaryDraw, "stationary non-damaging draw unassigned");
            Assert.IsNotNull(hunter.drawSlash, "draw slash unassigned");
            Assert.IsNotNull(hunter.chargedSlash, "charged slash unassigned");
            Assert.IsNotNull(hunter.strongChargedSlash, "strong charged slash unassigned");
            Assert.IsNotNull(hunter.trueChargedSlash, "TCS opening hit unassigned");
            Assert.IsNotNull(hunter.trueChargedFinishNormal, "normal TCS finisher unassigned");
            Assert.IsNotNull(hunter.trueChargedFinishLevel1, "TCS level-1 finisher unassigned");
            Assert.IsNotNull(hunter.trueChargedFinishLevel2, "TCS level-2 finisher unassigned");
            Assert.IsNotNull(hunter.trueChargedFinishLevel3, "TCS level-3 finisher unassigned");
            Assert.IsNotNull(hunter.wideSlash, "wide slash unassigned");
            Assert.IsNotNull(hunter.strongWideSlash, "strong wide slash unassigned");
            Assert.IsNotNull(hunter.leapingWideSlash, "leaping wide slash unassigned");
            Assert.IsNotNull(hunter.wideSlashPostStrong, "post-strong wide slash unassigned");
            Assert.IsNotNull(hunter.risingSlash, "rising slash unassigned");
            Assert.IsNotNull(hunter.risingSlashPostStrong, "post-strong rising slash unassigned");
            Assert.IsNotNull(hunter.sideBlow, "side blow unassigned");
            Assert.IsNotNull(hunter.sideBlowPostStrong, "post-strong side blow unassigned");
            Assert.IsNotNull(hunter.tackle, "tackle unassigned");
            Assert.IsNotNull(hunter.tackleLevel2, "level-2 tackle unassigned");
            Assert.IsNotNull(hunter.kick, "guard kick unassigned");
            Assert.IsNotNull(hunter.bladePoint, "blade point unassigned");
            Assert.IsNotNull(hunter.aimCamera, "aim camera unassigned");
            Assert.AreNotEqual(0, hunter.hurtboxLayers.value, "hurtbox layer mask is empty");

            Transform hunterVisuals = hunter.transform.Find("VisualRoot");
            Assert.IsNotNull(hunterVisuals, "hunter VisualRoot is missing");
            Assert.IsEmpty(
                hunterVisuals.GetComponentsInChildren<Collider>(true),
                "hunter presentation must never contain gameplay colliders");

            var brain = Object.FindFirstObjectByType<VelkhanaBrain>();
            Assert.IsNotNull(brain, "no VelkhanaBrain in the graybox scene");
            Assert.AreSame(hunter.transform, brain.hunter, "brain is not targeting the hunter");
            Assert.IsNotEmpty(brain.options, "brain has no attack options");
            Assert.IsNotEmpty(brain.armoredParts, "brain has no armoured parts");

            var volumeDebug = Object.FindFirstObjectByType<CombatVolumeDebug>();
            Assert.IsNotNull(volumeDebug, "runtime combat volume overlay is missing");
            Assert.AreSame(hunter, volumeDebug.hunterController);
            Assert.AreSame(brain, volumeDebug.brain);

            MonsterAttackOption node009 = null;
            foreach (var option in brain.options)
            {
                Assert.IsNotNull(option.attack, "an attack option has no definition");
                if (option.thkNode == "Global.node_009")
                    node009 = option;
            }

            Assert.IsNotNull(node009, "Global.node_009 lookup leaf is missing");
            Assert.AreEqual(0f, node009.minimumFacingAngle, 0.001f,
                "node_087 must be able to select the back-step leaf with the hunter in front");
            Assert.IsFalse(node009.useInFlatGroundSelector,
                "Global.node_009 must only enter through the decoded node_087 hierarchy");

            var presentation = Object.FindFirstObjectByType<VelkhanaPresentation>();
            Assert.IsNotNull(presentation, "no procedural VelkhanaPresentation in the graybox scene");
            Assert.IsNotNull(presentation.visualRoot, "Velkhana visual root is unassigned");
            Assert.IsNotNull(presentation.torsoPivot, "Velkhana torso pivot is unassigned");
            Assert.IsNotNull(presentation.neckPivot, "Velkhana neck pivot is unassigned");
            Assert.IsNotNull(presentation.headPivot, "Velkhana head pivot is unassigned");
            Assert.IsNotNull(presentation.wingLPivot, "Velkhana left wing pivot is unassigned");
            Assert.IsNotNull(presentation.wingRPivot, "Velkhana right wing pivot is unassigned");
            Assert.IsNotNull(presentation.tailRoot, "Velkhana tail root is unassigned");
            Assert.IsNotNull(presentation.tailMiddle, "Velkhana tail middle is unassigned");
            Assert.IsNotNull(presentation.tailTip, "Velkhana tail tip is unassigned");
            Assert.IsNotNull(presentation.adjustBite, "bite pose mapping is unassigned");
            Assert.IsNotNull(presentation.rush, "rush pose mapping is unassigned");
            Assert.IsNotNull(presentation.rush2, "second rush pose mapping is unassigned");
            Assert.IsNotNull(presentation.backStepPierce, "back-step pose mapping is unassigned");
            Assert.IsNotNull(presentation.tailThrust, "tail thrust pose mapping is unassigned");
            Assert.IsNotNull(presentation.tailSwing, "tail swing pose mapping is unassigned");
            Assert.IsNotNull(presentation.straightBreath, "straight breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.sweep90Breath, "90 breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.sweep180Breath, "180 breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.iceWave, "ice wave pose mapping is unassigned");
            Assert.IsNotNull(presentation.areaBreath, "area breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.freezeBreath, "freeze breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.iceSpires, "ice spires pose mapping is unassigned");
            Assert.IsNotNull(presentation.verticalBreathFly, "aerial breath mapping is unassigned");
            Assert.IsNotNull(
                presentation.verticalBreathFlyToGround,
                "aerial landing breath mapping is unassigned");
            Assert.IsNotNull(
                presentation.iceWaveStartFly,
                "airborne ice-wave mapping is unassigned");
            Assert.IsNotNull(presentation.flyTailStingToGround, "aerial landing mapping is unassigned");
            Assert.IsEmpty(presentation.visualRoot.GetComponentsInChildren<Collider>(true),
                "presentation hierarchy must never contain gameplay colliders");

            Transform gameplayHurtboxes = brain.transform.Find("GameplayHurtboxes");
            Assert.IsNotNull(gameplayHurtboxes, "stationary GameplayHurtboxes root is missing");
            Assert.AreEqual(9, gameplayHurtboxes.GetComponentsInChildren<BodyPartHurtbox>(true).Length,
                "all nine gameplay hurtboxes must remain outside the animated visual hierarchy");

            var hurtboxes = Object.FindObjectsByType<BodyPartHurtbox>(FindObjectsSortMode.None);
            Assert.AreEqual(9, hurtboxes.Length, "expected nine body part hurtboxes");

            foreach (var hurtbox in hurtboxes)
            {
                Assert.AreEqual(1 << hurtbox.gameObject.layer & hunter.hurtboxLayers.value,
                    1 << hurtbox.gameObject.layer,
                    $"{hurtbox.name} is not on a layer the sword can hit");
                Assert.IsTrue(hurtbox.GetComponent<Collider>().isTrigger,
                    $"{hurtbox.name} collider must be a trigger");
            }

            BodyPartHurtbox torso = System.Array.Find(
                hurtboxes, part => part.part == BodyPart.Torso);
            Assert.IsNotNull(torso);
            Assert.IsTrue(torso.toppleOnBreak, "torso break should drive the configured break topple");
            Assert.AreEqual(280f, torso.staggerThreshold, 0.001f);

            BodyPartHurtbox head = System.Array.Find(
                hurtboxes, part => part.part == BodyPart.Head);
            Assert.IsNotNull(head);
            Assert.IsFalse(head.toppleOnBreak, "head break is not the torso break-topple contract");
            Assert.IsTrue(head.toppleOnStagger);
            Assert.AreEqual(200f, head.staggerThreshold, 0.001f);

            AttackDefinition rush = System.Array.Find(
                brain.options.ToArray(), option => option.attack != null && option.attack.name == "VK_Rush")?.attack;
            Assert.IsNotNull(rush);
            Assert.IsTrue(rush.LaunchesHunter, "rush should carry a launch reaction");

            AttackDefinition straightBreath = System.Array.Find(
                brain.options.ToArray(), option => option.attack != null &&
                                                   option.attack.name == "VK_StraightBreath")?.attack;
            Assert.IsNotNull(straightBreath);
            Assert.IsFalse(straightBreath.LaunchesHunter,
                "launch reactions must remain scoped to the configured physical attacks");

            // The camera pitch has to stay inside the readable band the plan specifies.
            float pitch = hunter.aimCamera.transform.eulerAngles.x;
            Assert.GreaterOrEqual(pitch, 55f);
            Assert.LessOrEqual(pitch, 65f);
        }
    }
}
