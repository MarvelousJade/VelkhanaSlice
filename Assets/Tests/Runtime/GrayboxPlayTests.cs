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
