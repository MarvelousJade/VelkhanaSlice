using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.PlayTests
{
    public class VelkhanaStatePlayTests
    {
        static AttackDefinition Attack(int startup = 4, int active = 2, int recovery = 8)
        {
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            attack.startupFrames = startup;
            attack.activeFrames = active;
            attack.recoveryFrames = recovery;
            attack.trackingCutoffFrame = 2;
            attack.hitboxSize = Vector3.zero;
            return attack;
        }

        static VelkhanaBrain Brain(
            Vector3 hunterPosition,
            RangeBand band,
            AttackDefinition attack,
            out GameObject root,
            out GameObject hunter)
        {
            hunter = new GameObject("HunterStandIn");
            hunter.transform.position = hunterPosition;
            root = new GameObject("VelkhanaStandIn");

            var brain = root.AddComponent<VelkhanaBrain>();
            brain.hunter = hunter.transform;
            brain.neutralFrames = 1;
            brain.closeRange = 6f;
            brain.mediumRange = 16f;
            brain.repositionDecisionIntervalFrames = 2;
            brain.repositionSpeed = 6f;
            brain.maxRepositionFrames = 500;
            brain.options.Add(new MonsterAttackOption
            {
                attack = attack,
                band = band,
                weight = 1f,
                cooldownFrames = 30,
            });
            return brain;
        }

        [UnityTest]
        public IEnumerator RecoveryIsObservableWhileAttackFrameRemainsAuthoritative()
        {
            AttackDefinition attack = Attack();
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, attack,
                out GameObject root, out GameObject hunter);

            try
            {
                int budget = 200;
                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(attack, brain.CurrentAttack);
                Assert.AreEqual(VelkhanaState.Attacking, brain.CurrentState);

                while (brain.CurrentState != VelkhanaState.Recovery && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Recovery, brain.CurrentState);
                Assert.AreSame(attack, brain.CurrentAttack,
                    "recovery is still part of the authoritative attack timeline");
                Assert.GreaterOrEqual(brain.AttackFrame, attack.startupFrames + attack.activeFrames);
                Assert.Less(brain.AttackFrame, attack.TotalFrames);

                while (brain.CurrentAttack != null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsNull(brain.CurrentAttack);
                Assert.AreEqual(0, brain.AttackFrame);
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(attack);
            }
        }

        [UnityTest]
        public IEnumerator BrainRepositionsIntoTheBandOfAReadyAttack()
        {
            AttackDefinition attack = Attack();
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 20f), RangeBand.Close, attack,
                out GameObject root, out GameObject hunter);

            try
            {
                float startingDistance = Vector3.Distance(root.transform.position, hunter.transform.position);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Reposition, brain.CurrentState);
                Assert.AreEqual(RangeBand.Close, brain.DesiredBand);

                for (int i = 0; i < 30; i++)
                    yield return new WaitForFixedUpdate();

                float movedDistance = Vector3.Distance(root.transform.position, hunter.transform.position);
                Assert.Less(movedDistance, startingDistance - 1f,
                    "direct locomotion should close distance without requiring a NavMesh");

                int budget = 500;
                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(attack, brain.CurrentAttack,
                    "the brain should attack once repositioning enters its usable range band");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(attack);
            }
        }

        [UnityTest]
        public IEnumerator SequenceKeepsIndividualAttackTimelinesAndInterruptBoundaries()
        {
            AttackDefinition first = Attack(2, 1, 2);
            AttackDefinition second = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, first,
                out GameObject root, out GameObject hunter);
            brain.options[0].calmFollowUps = new[] { second };

            try
            {
                int budget = 100;
                while (brain.CurrentAttack != first && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(first, brain.CurrentAttack);
                Assert.AreEqual(2, brain.SequenceLength);
                Assert.AreEqual(0, brain.SequenceStep);

                while (brain.CurrentAttack != second && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(second, brain.CurrentAttack);
                Assert.AreEqual(1, brain.SequenceStep);
                Assert.AreEqual(0, brain.AttackFrame,
                    "each THK action step owns a fresh authoritative frame timeline");

                while (brain.CurrentAttack != null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [UnityTest]
        public IEnumerator AerialSequenceUsesTakeoffAndLandingContexts()
        {
            AttackDefinition attack = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, attack,
                out GameObject root, out GameObject hunter);
            brain.options[0].takeOffBeforeSequence = true;
            brain.options[0].landAfterSequence = true;
            brain.takeoffFrames = 3;
            brain.landingFrames = 3;

            try
            {
                int budget = 100;
                while (brain.CurrentState != VelkhanaState.Takeoff && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Takeoff, brain.CurrentState);
                Assert.IsFalse(brain.IsAirborne);

                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(brain.IsAirborne);
                Assert.AreEqual(VelkhanaContext.AerialCombat, brain.CurrentContext);

                while (brain.CurrentState != VelkhanaState.Landing && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(brain.IsAirborne, "landing remains an aerial gameplay context");

                while (brain.IsAirborne && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(attack);
            }
        }

        [UnityTest]
        public IEnumerator DamageBuildupTriggersAnObservableRageTransition()
        {
            AttackDefinition attack = Attack();
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, attack,
                out GameObject root, out GameObject hunter);
            brain.automaticEnrage = true;
            brain.rageDamageThreshold = 20f;
            brain.rageTransitionFrames = 3;

            try
            {
                brain.ApplyBossDamage(20f);
                Assert.IsTrue(brain.enraged);
                Assert.AreEqual(VelkhanaState.RageTransition, brain.CurrentState);
                Assert.AreEqual(VelkhanaContext.RageTransition, brain.CurrentContext);

                for (int i = 0; i < 4; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreNotEqual(VelkhanaState.RageTransition, brain.CurrentState);
                Assert.IsTrue(brain.enraged, "rage persists after the roar transition");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(attack);
            }
        }
    }
}
