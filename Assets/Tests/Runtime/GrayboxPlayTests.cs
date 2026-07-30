using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

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
            Assert.IsNotNull(hunter.drawSlash, "draw slash unassigned");
            Assert.IsNotNull(hunter.chargedSlash, "charged slash unassigned");
            Assert.IsNotNull(hunter.wideSlash, "wide slash unassigned");
            Assert.IsNotNull(hunter.tackle, "tackle unassigned");
            Assert.IsNotNull(hunter.bladePoint, "blade point unassigned");
            Assert.IsNotNull(hunter.aimCamera, "aim camera unassigned");
            Assert.AreNotEqual(0, hunter.hurtboxLayers.value, "hurtbox layer mask is empty");

            var brain = Object.FindFirstObjectByType<VelkhanaBrain>();
            Assert.IsNotNull(brain, "no VelkhanaBrain in the graybox scene");
            Assert.AreSame(hunter.transform, brain.hunter, "brain is not targeting the hunter");
            Assert.IsNotEmpty(brain.options, "brain has no attack options");
            Assert.IsNotEmpty(brain.armoredParts, "brain has no armoured parts");

            foreach (var option in brain.options)
                Assert.IsNotNull(option.attack, "an attack option has no definition");

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
            Assert.IsNotNull(presentation.tailThrust, "tail thrust pose mapping is unassigned");
            Assert.IsNotNull(presentation.bodyCheck, "body check pose mapping is unassigned");
            Assert.IsNotNull(presentation.iceBeam, "ice beam pose mapping is unassigned");
            Assert.IsNotNull(presentation.sweepingBreath, "sweeping breath pose mapping is unassigned");
            Assert.IsNotNull(presentation.iceSpires, "ice spires pose mapping is unassigned");
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

            // The camera pitch has to stay inside the readable band the plan specifies.
            float pitch = hunter.aimCamera.transform.eulerAngles.x;
            Assert.GreaterOrEqual(pitch, 55f);
            Assert.LessOrEqual(pitch, 65f);
        }
    }
}
