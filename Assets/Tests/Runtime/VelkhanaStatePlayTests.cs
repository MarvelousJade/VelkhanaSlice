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
                for (int i = 0; i < VelkhanaBrain.ProjectMinimumGroundResetFrames; i++)
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
                Assert.AreEqual(VelkhanaContext.AerialCombat, brain.CurrentContext,
                    "Takeoff must expose its aerial context in the entry frame");

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
                Assert.IsTrue(
                    brain.CurrentContext == VelkhanaContext.GroundCombat ||
                    brain.CurrentContext == VelkhanaContext.CombatEntry,
                    "landing completion must expose a grounded context in the completion frame");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(attack);
            }
        }

        [UnityTest]
        public IEnumerator GroundedSelectorExcludesNode006AerialFamilies()
        {
            AttackDefinition groundAttack = Attack(2, 1, 2);
            AttackDefinition aerialAttack = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, groundAttack,
                out GameObject root, out GameObject hunter);
            brain.options.Insert(0, new MonsterAttackOption
            {
                attack = aerialAttack,
                band = RangeBand.Close,
                weight = 10000f,
                aerialFamily = VelkhanaAerialOptionFamily.Global051,
                airRequirement = VelkhanaAirRequirement.Airborne,
            });

            try
            {
                int budget = VelkhanaBrain.ProjectMinimumGroundResetFrames + 20;
                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(groundAttack, brain.CurrentAttack,
                    "ground selection must exclude node_006 family options before weighting");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(groundAttack);
                Object.DestroyImmediate(aerialAttack);
            }
        }

        [UnityTest]
        public IEnumerator ProjectSeed0CloseFrontUsesSourceOrderedNode105Node087Backstep()
        {
            AttackDefinition bite = Attack(3, 1, 5);
            AttackDefinition rush2 = Attack(3, 1, 5);
            AttackDefinition backstep = Attack(4, 2, 8);
            backstep.forwardMotion = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            backstep.forwardMotionScale = -2.4f;

            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 2.5f), RangeBand.Close, bite,
                out GameObject root, out GameObject hunter);
            brain.options.Clear();
            brain.options.Add(new MonsterAttackOption
            {
                attack = bite,
                thkNode = "Global.node_004",
                band = RangeBand.Close,
                weight = 1f,
            });
            brain.options.Add(new MonsterAttackOption
            {
                attack = rush2,
                thkNode = "Global.node_006",
                band = RangeBand.Close,
                weight = 1f,
            });
            var backstepLeaf = new MonsterAttackOption
            {
                attack = backstep,
                thkNode = "Global.node_009",
                band = RangeBand.Close,
                weight = 1f,
                useInFlatGroundSelector = false,
                CooldownRemaining = 999,
            };
            brain.options.Add(backstepLeaf);
            // Project RNG seed 0 produces rolls 72 then 81: the decoded source interval selects
            // Global.node_105, then node_087's <=3 m table selects Global.node_009.
            brain.selectionSeed = 0;

            try
            {
                float startingSeparation =
                    Vector3.Distance(root.transform.position, hunter.transform.position);
                int budget =
                    VelkhanaBrain.ProjectMinimumGroundResetFrames +
                    backstep.TotalFrames +
                    40;
                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(backstep, brain.CurrentAttack);
                Assert.AreEqual("Global.node_009", brain.CurrentThkNode);
                Assert.AreEqual(
                    "Combat_Main.node_002 > Global.node_105 > " +
                    "Global.node_087 > Global.node_009",
                    brain.CurrentThkTrace);
                Assert.IsTrue(brain.IsGroundOpenerSliceActive);
                Assert.Greater(backstepLeaf.CooldownRemaining, 0,
                    "node_087 leaf lookup must bypass generic cooldown eligibility");
                Assert.AreEqual(1, brain.SequenceLength,
                    "this milestone is the opener leaf only, without N088/N089/N090 followups");

                bool sawRecovery = false;
                int maximumAttackFrame = brain.AttackFrame;
                while (brain.CurrentAttack != null && budget-- > 0)
                {
                    sawRecovery |= brain.CurrentState == VelkhanaState.Recovery;
                    maximumAttackFrame = Mathf.Max(maximumAttackFrame, brain.AttackFrame);
                    yield return new WaitForFixedUpdate();
                }

                float endingSeparation =
                    Vector3.Distance(root.transform.position, hunter.transform.position);
                Assert.IsTrue(sawRecovery,
                    "the opener leaf must retain the complete AttackDefinition recovery");
                Assert.GreaterOrEqual(maximumAttackFrame, backstep.TotalFrames - 1);
                Assert.Greater(endingSeparation, startingSeparation + 1f);
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
                Assert.IsFalse(brain.IsGroundOpenerSliceActive);
                Assert.IsEmpty(brain.CurrentThkTrace);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(bite);
                Object.DestroyImmediate(rush2);
                Object.DestroyImmediate(backstep);
            }
        }

        [UnityTest]
        public IEnumerator ProjectSeed124MissFallsThroughAndLookupOnlyNode009StaysFlatDisabled()
        {
            AttackDefinition fallback = Attack(3, 1, 5);
            AttackDefinition backstep = Attack(3, 1, 5);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 2.5f), RangeBand.Close, fallback,
                out GameObject root, out GameObject hunter);
            brain.options[0].thkNode = "Project.flat_fallback";
            brain.options[0].weight = 1f;
            brain.options.Insert(0, new MonsterAttackOption
            {
                attack = backstep,
                thkNode = "Global.node_009",
                band = RangeBand.Close,
                weight = 10000f,
                useInFlatGroundSelector = false,
            });
            // Project deterministic seed; its first roll is 51 and misses node_002's 60..74
            // Global.node_105 source interval.
            brain.selectionSeed = 124;

            try
            {
                int budget = VelkhanaBrain.ProjectMinimumGroundResetFrames + 30;
                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(fallback, brain.CurrentAttack,
                    "close range alone must not guarantee node_087");
                Assert.IsFalse(brain.IsGroundOpenerSliceActive);
                StringAssert.DoesNotContain("Global.node_087", brain.CurrentThkTrace);
                Assert.AreEqual(
                    "Combat_Main.node_002 > flat ground selector > Project.flat_fallback",
                    brain.CurrentThkTrace);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(fallback);
                Object.DestroyImmediate(backstep);
            }
        }

        [UnityTest]
        public IEnumerator GroundGatewayTakesOffChoosesOnlyAerialFamilyAndLands()
        {
            AttackDefinition gatewayMarker = Attack(2, 1, 2);
            AttackDefinition groundTrap = Attack(2, 1, 2);
            AttackDefinition global051 = Attack(2, 1, 2);
            AttackDefinition global052 = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, gatewayMarker,
                out GameObject root, out GameObject hunter);

            MonsterAttackOption gateway = brain.options[0];
            gateway.thkNode = "Combat_Main.node_006.entry";
            gateway.takeOffBeforeSequence = true;
            gateway.enterAerialChooserAfterTakeoff = true;
            brain.options.Add(new MonsterAttackOption
            {
                attack = groundTrap,
                band = RangeBand.Far,
                weight = 10000f,
                thkNode = "GroundTrap",
            });
            brain.options.Add(new MonsterAttackOption
            {
                attack = global051,
                aerialFamily = VelkhanaAerialOptionFamily.Global051,
                airRequirement = VelkhanaAirRequirement.Airborne,
                landAfterSequence = true,
                thkNode = "Global.node_051",
            });
            brain.options.Add(new MonsterAttackOption
            {
                attack = global052,
                aerialFamily = VelkhanaAerialOptionFamily.Global052,
                airRequirement = VelkhanaAirRequirement.Airborne,
                thkNode = "Global.node_052",
            });
            brain.combatMainNode006Predicate101 = true;
            brain.takeoffFrames = 2;
            brain.landingFrames = 2;

            try
            {
                int budget = 100;
                while (brain.CurrentState != VelkhanaState.Takeoff && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Takeoff, brain.CurrentState);

                while (!brain.IsAirborne && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(brain.IsAirborne);
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState,
                    "the gateway must finish takeoff in airborne Observe");
                Assert.IsNull(brain.CurrentAttack,
                    "the gateway marker is not an aerial family attack");

                while (brain.CurrentAttack == null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(global051, brain.CurrentAttack);
                Assert.AreNotSame(groundTrap, brain.CurrentAttack,
                    "airborne node_006 must never fall through to ground options");
                Assert.AreEqual("Global.node_051", brain.CurrentThkNode);

                while (brain.CurrentState != VelkhanaState.Landing && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Landing, brain.CurrentState);
                while (brain.IsAirborne && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsFalse(brain.IsAirborne);
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(gatewayMarker);
                Object.DestroyImmediate(groundTrap);
                Object.DestroyImmediate(global051);
                Object.DestroyImmediate(global052);
            }
        }

        [UnityTest]
        public IEnumerator Global052ReturnsToAirborneObserveAndNode006Redispatches()
        {
            AttackDefinition gatewayMarker = Attack(2, 1, 2);
            AttackDefinition global051 = Attack(2, 1, 2);
            AttackDefinition global052 = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, gatewayMarker,
                out GameObject root, out GameObject hunter);

            brain.options[0].takeOffBeforeSequence = true;
            brain.options[0].enterAerialChooserAfterTakeoff = true;
            brain.options.Add(new MonsterAttackOption
            {
                attack = global051,
                aerialFamily = VelkhanaAerialOptionFamily.Global051,
                airRequirement = VelkhanaAirRequirement.Airborne,
                landAfterSequence = true,
                thkNode = "Global.node_051",
            });
            brain.options.Add(new MonsterAttackOption
            {
                attack = global052,
                aerialFamily = VelkhanaAerialOptionFamily.Global052,
                airRequirement = VelkhanaAirRequirement.Airborne,
                landAfterSequence = false,
                thkNode = "Global.node_052",
            });
            brain.combatMainNode006Predicate101 = false;
            brain.selectionSeed = 0; // first roll chooses the gateway; second is 82 -> Global052.
            brain.takeoffFrames = 2;

            try
            {
                int budget = 100;
                while (brain.CurrentAttack != global052 && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreSame(global052, brain.CurrentAttack,
                    "false predicate with the seeded 50..99 roll must dispatch Global052");

                while (brain.CurrentAttack != null && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(brain.IsAirborne);
                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
                Assert.AreNotEqual(VelkhanaState.Landing, brain.CurrentState,
                    "ice_wave_start_fly remains in aerial combat");

                yield return new WaitForFixedUpdate();

                Assert.IsTrue(brain.IsAirborne);
                Assert.IsNotNull(brain.CurrentAttack,
                    "airborne Observe must immediately re-dispatch node_006");
                Assert.IsTrue(
                    brain.CurrentAttack == global051 || brain.CurrentAttack == global052);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(gatewayMarker);
                Object.DestroyImmediate(global051);
                Object.DestroyImmediate(global052);
            }
        }

        [UnityTest]
        public IEnumerator AirborneObserveWithoutHunterBeginsSafeLanding()
        {
            AttackDefinition gatewayMarker = Attack(2, 1, 2);
            AttackDefinition global051 = Attack(2, 1, 2);
            VelkhanaBrain brain = Brain(
                new Vector3(0f, 0f, 3f), RangeBand.Close, gatewayMarker,
                out GameObject root, out GameObject hunter);

            brain.options[0].takeOffBeforeSequence = true;
            brain.options[0].enterAerialChooserAfterTakeoff = true;
            brain.options.Add(new MonsterAttackOption
            {
                attack = global051,
                aerialFamily = VelkhanaAerialOptionFamily.Global051,
                airRequirement = VelkhanaAirRequirement.Airborne,
                landAfterSequence = true,
                thkNode = "Global.node_051",
            });
            brain.combatMainNode006Predicate101 = true;
            brain.takeoffFrames = 2;
            brain.landingFrames = 2;

            try
            {
                int budget = 50;
                while (!brain.IsAirborne && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
                Assert.AreEqual(VelkhanaContext.AerialCombat, brain.CurrentContext);

                brain.hunter = null;
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Landing, brain.CurrentState);
                Assert.IsNull(brain.CurrentAttack,
                    "a missing hunter must not dispatch an aerial family");

                while (brain.IsAirborne && budget-- > 0)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(VelkhanaState.Observe, brain.CurrentState);
                Assert.AreEqual(VelkhanaContext.CombatEntry, brain.CurrentContext,
                    "landing completion must refresh context in the same frame");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(hunter);
                Object.DestroyImmediate(gatewayMarker);
                Object.DestroyImmediate(global051);
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
