using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

// UnityEditor has its own BodyPart (avatar rigging), so ours has to be named explicitly.
using BodyPart = VelkhanaSlice.Combat.BodyPart;

namespace VelkhanaSlice.EditorTools
{
    /// <summary>
    /// Regenerates the graybox arena and its placeholder attack data from scratch.
    /// The scene is disposable: change this script and rebuild rather than hand-editing the scene,
    /// so the setup stays reviewable in a diff.
    /// </summary>
    public static class GrayboxSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Graybox.unity";
        const string AttackFolder = "Assets/Data/Attacks";
        const string MaterialFolder = "Assets/Data/Materials";
        const string HurtboxLayer = "Hurtbox";
        const string HunterLayer = "Hunter";

        [MenuItem("Velkhana/Rebuild Graybox Scene")]
        public static void Build()
        {
            int hurtboxLayer = EnsureLayer(HurtboxLayer);
            int hunterLayer = EnsureLayer(HunterLayer);
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Data");
            EnsureFolder(AttackFolder);
            EnsureFolder(MaterialFolder);

            var gs = BuildGreatSwordAttacks();
            var velk = BuildVelkhanaAttacks();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildGround();

            var hunter = BuildHunter(gs, hurtboxLayer, hunterLayer);
            var camera = BuildCamera(hunter);
            var velkhana = BuildVelkhana(hunter.transform, velk, hurtboxLayer, hunterLayer);

            // One telegraph component serves both sides, since both play AttackDefinitions.
            var telegraphMaterial = Mat("M_Telegraph", new Color(1f, 0.7f, 0.2f));
            telegraphMaterial.EnableKeyword("_EMISSION");
            hunter.AddComponent<AttackTelegraph>().material = telegraphMaterial;
            velkhana.AddComponent<AttackTelegraph>().material = telegraphMaterial;

            // Centre between the two so a 11 m separation stays inside a 55 degree vertical FOV.
            var rig = camera.AddComponent<CameraRig>();
            rig.target = hunter.transform;
            rig.secondaryTarget = velkhana.transform;
            rig.secondaryBias = 0.5f;
            rig.offset = new Vector3(0f, 22f, -13.5f);

            // Inert unless the player is launched with -autoshots, so it costs nothing in a normal run.
            new GameObject("ScriptedPlaythrough").AddComponent<DebugTools.ScriptedPlaythrough>();

            var hud = new GameObject("CombatHud").AddComponent<DebugTools.CombatHud>();
            hud.health = hunter.GetComponent<HunterHealth>();
            hud.hunterController = hunter.GetComponent<HunterController>();
            hud.brain = velkhana.GetComponent<VelkhanaBrain>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Graybox scene rebuilt at {ScenePath}");
        }

        [MenuItem("Velkhana/Rebuild Graybox + Windows Player")]
        public static void BuildWindowsPlayer()
        {
            Build();

            string output = Path.GetFullPath("Build/VelkhanaSlice.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Windows player build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors)");

            Debug.Log($"Windows player rebuilt at {output}");
        }

        // ---------- attack data ----------

        /// <summary>
        /// Placeholder frame counts. These are the numbers to overwrite from frame-stepped
        /// reference footage; nothing else has to change when they do.
        /// </summary>
        static AttackDefinition Attack(
            string name, int startup, int active, int recovery, int cutoff,
            float damage, float stagger, Vector3 hitboxCenter, Vector3 hitboxSize,
            Action<AttackDefinition> tweak = null)
        {
            string path = $"{AttackFolder}/{name}.asset";
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>(path);
            if (attack == null)
            {
                attack = ScriptableObject.CreateInstance<AttackDefinition>();
                AssetDatabase.CreateAsset(attack, path);
            }

            attack.startupFrames = startup;
            attack.activeFrames = active;
            attack.recoveryFrames = recovery;
            attack.trackingCutoffFrame = cutoff;
            attack.cancelWindowStart = -1;
            attack.damage = damage;
            attack.staggerDamage = stagger;
            attack.chargeMultipliers = new[] { 1f, 1.4f, 1.8f, 2.4f };
            attack.hitboxCenter = hitboxCenter;
            attack.hitboxSize = hitboxSize;
            attack.hyperArmor = false;
            attack.incomingDamageReduction = 0f;
            attack.forwardMotion = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            attack.forwardMotionScale = 0f;
            attack.followUps = Array.Empty<AttackDefinition>();
            attack.requiresPreviousHitConnected = false;

            tweak?.Invoke(attack);
            EditorUtility.SetDirty(attack);
            return attack;
        }

        class GreatSword
        {
            public AttackDefinition StationaryDraw;
            public AttackDefinition DrawSlash;
            public AttackDefinition ChargedSlash;
            public AttackDefinition StrongCharged;
            public AttackDefinition TrueChargedFirstHit;
            public AttackDefinition TrueChargedFinishNormal;
            public AttackDefinition TrueChargedFinishLevel1;
            public AttackDefinition TrueChargedFinishLevel2;
            public AttackDefinition TrueChargedFinishLevel3;
            public AttackDefinition WideSlash;
            public AttackDefinition StrongWideSlash;
            public AttackDefinition LeapingWideSlash;
            public AttackDefinition WideSlashPostStrong;
            public AttackDefinition RisingSlash;
            public AttackDefinition RisingSlashPostStrong;
            public AttackDefinition SideBlow;
            public AttackDefinition SideBlowPostStrong;
            public AttackDefinition Tackle;
            public AttackDefinition TackleLevel2;
            public AttackDefinition Kick;
        }

        static GreatSword BuildGreatSwordAttacks()
        {
            var gs = new GreatSword();

            // WP00 node 22 / ActionNo 5 only draws the weapon. It intentionally has no active
            // frames or hitbox; holding Triangle routes to node 3 after this presentation action.
            gs.StationaryDraw = Attack(
                "GS_StationaryDraw", 18, 0, 4, 18, 0f, 0f,
                Vector3.zero, Vector3.zero);

            // N021 / ActionNo 7 is WP_00::WALK_ON, a moving draw/link state. The decoded graph
            // continues through N031 LinkMotion 104 to N001 / WP_00::VSLASH; it is not itself
            // the draw attack. The serialized DrawSlash field/asset name is retained.
            gs.DrawSlash = Attack("GS_DrawSlash", 22, 4, 34, 14, 90f, 35f,
                new Vector3(0f, 1f, 1.7f), new Vector3(2.6f, 2f, 2.8f),
                a =>
                {
                    a.forwardMotionScale = 1.4f;
                    a.cancelWindowStart = 38;
                });

            gs.WideSlash = Attack("GS_WideSlash", 18, 5, 30, 12, 70f, 45f,
                new Vector3(0f, 1f, 1.4f), new Vector3(3.6f, 2f, 2.4f),
                a => a.cancelWindowStart = 34);

            gs.Tackle = Attack("GS_Tackle", 10, 6, 22, 6, 30f, 20f,
                new Vector3(0f, 1f, 1.2f), new Vector3(1.6f, 2f, 2.2f), a =>
            {
                a.hyperArmor = true;
                a.incomingDamageReduction = 0.5f;
                a.forwardMotionScale = 2.2f;
                a.cancelWindowStart = 16;
            });

            gs.TackleLevel2 = Attack("GS_TackleLevel2", 9, 6, 23, 5, 38f, 28f,
                new Vector3(0f, 1f, 1.3f), new Vector3(1.8f, 2.1f, 2.4f), a =>
                {
                    a.hyperArmor = true;
                    a.incomingDamageReduction = 0.5f;
                    a.forwardMotionScale = 2.4f;
                    a.cancelWindowStart = 16;
                });

            gs.StrongCharged = Attack("GS_StrongChargedSlash", 26, 5, 44, 15, 130f, 70f,
                new Vector3(0f, 1f, 1.9f), new Vector3(3.0f, 2f, 3.2f), a =>
            {
                a.forwardMotionScale = 1.6f;
                a.cancelWindowStart = 40;
            });

            gs.ChargedSlash = Attack("GS_ChargedSlash", 24, 5, 40, 14, 100f, 55f,
                new Vector3(0f, 1f, 1.8f), new Vector3(2.8f, 2f, 3.0f), a =>
            {
                a.forwardMotionScale = 1.5f;
                a.cancelWindowStart = 36;
            });

            // WP00 node 39 is the TCS opening hit. A miss continues through ActionNo 78's normal
            // second hit; a connected opening diverts into FinishEx 111/112/113 by hold power.
            gs.TrueChargedFirstHit = Attack(
                "GS_TrueChargedSlash_FirstHit", 22, 3, 14, 12, 48f, 25f,
                new Vector3(0f, 1f, 1.7f), new Vector3(2.6f, 2f, 3f),
                a => a.forwardMotionScale = 0.7f);
            gs.TrueChargedFinishNormal = Attack(
                "GS_TrueChargedSlash_Finish_Normal", 12, 5, 34, 8, 130f, 70f,
                new Vector3(0f, 1f, 2.1f), new Vector3(3.3f, 2.2f, 3.8f),
                a => a.forwardMotionScale = 1.6f);
            gs.TrueChargedFinishLevel1 = Attack(
                "GS_TrueChargedSlash_Finish_L1", 12, 5, 36, 8, 150f, 80f,
                new Vector3(0f, 1f, 2.2f), new Vector3(3.5f, 2.2f, 4f),
                a =>
                {
                    a.requiresPreviousHitConnected = true;
                    a.chargeMultipliers = new[] { 1f, 1f, 1f, 1f };
                    a.forwardMotionScale = 1.7f;
                });
            gs.TrueChargedFinishLevel2 = Attack(
                "GS_TrueChargedSlash_Finish_L2", 12, 5, 38, 8, 205f, 100f,
                new Vector3(0f, 1f, 2.3f), new Vector3(3.7f, 2.3f, 4.2f),
                a =>
                {
                    a.requiresPreviousHitConnected = true;
                    a.chargeMultipliers = new[] { 1f, 1f, 1f, 1f };
                    a.forwardMotionScale = 1.8f;
                });
            gs.TrueChargedFinishLevel3 = Attack(
                "GS_TrueChargedSlash_Finish_L3", 13, 6, 42, 8, 270f, 130f,
                new Vector3(0f, 1f, 2.5f), new Vector3(4f, 2.5f, 4.6f),
                a =>
                {
                    a.requiresPreviousHitConnected = true;
                    a.chargeMultipliers = new[] { 1f, 1f, 1f, 1f };
                    a.forwardMotionScale = 2f;
                });

            gs.StrongWideSlash = Attack(
                "GS_StrongWideSlash", 20, 5, 34, 13, 88f, 55f,
                new Vector3(0f, 1f, 1.5f), new Vector3(3.9f, 2.1f, 2.7f),
                a => a.cancelWindowStart = 38);
            gs.LeapingWideSlash = Attack(
                "GS_LeapingWideSlash", 16, 6, 32, 10, 92f, 62f,
                new Vector3(0f, 1f, 1.9f), new Vector3(4.2f, 2.2f, 3.4f),
                a =>
                {
                    a.forwardMotionScale = 2.6f;
                    a.cancelWindowStart = 34;
                });
            gs.WideSlashPostStrong = Attack(
                "GS_WideSlash_PostStrong", 17, 5, 32, 11, 96f, 64f,
                new Vector3(0f, 1f, 1.5f), new Vector3(4f, 2.1f, 2.8f),
                a => a.cancelWindowStart = 34);
            gs.RisingSlash = Attack(
                "GS_RisingSlash", 21, 5, 34, 13, 78f, 50f,
                new Vector3(0f, 1.4f, 1.5f), new Vector3(3f, 3.2f, 2.8f),
                a => a.cancelWindowStart = 38);
            gs.RisingSlashPostStrong = Attack(
                "GS_RisingSlash_PostStrong", 19, 5, 34, 12, 98f, 66f,
                new Vector3(0f, 1.5f, 1.6f), new Vector3(3.2f, 3.5f, 3f),
                a => a.cancelWindowStart = 36);
            gs.SideBlow = Attack(
                "GS_SideBlow", 12, 4, 26, 8, 44f, 30f,
                new Vector3(0f, 1.1f, 1.3f), new Vector3(2.7f, 1.8f, 2.2f),
                a => a.cancelWindowStart = 24);
            gs.SideBlowPostStrong = Attack(
                "GS_SideBlow_PostStrong", 11, 4, 27, 7, 58f, 38f,
                new Vector3(0f, 1.1f, 1.4f), new Vector3(2.9f, 1.9f, 2.3f),
                a => a.cancelWindowStart = 24);
            gs.Kick = Attack(
                "GS_Kick", 8, 4, 18, 5, 20f, 24f,
                new Vector3(0f, 0.8f, 1f), new Vector3(1.2f, 1.4f, 1.5f),
                a => a.cancelWindowStart = 14);

            // These arrays document the same readable graph for inspectors and generic tools.
            gs.StationaryDraw.followUps = new[] { gs.ChargedSlash };
            gs.DrawSlash.followUps = new[] { gs.ChargedSlash };
            gs.ChargedSlash.followUps =
                new[] { gs.StrongCharged, gs.WideSlash, gs.SideBlow, gs.RisingSlash };
            gs.WideSlash.followUps =
                new[] { gs.ChargedSlash, gs.Tackle, gs.RisingSlash };
            gs.SideBlow.followUps =
                new[] { gs.ChargedSlash, gs.WideSlash, gs.RisingSlash };
            gs.StrongCharged.followUps =
                new[]
                {
                    gs.TrueChargedFirstHit,
                    gs.StrongWideSlash,
                    gs.SideBlowPostStrong,
                    gs.RisingSlashPostStrong,
                };
            gs.StrongWideSlash.followUps =
                new[] { gs.StrongCharged, gs.WideSlashPostStrong };
            gs.SideBlowPostStrong.followUps =
                new[] { gs.StrongCharged, gs.WideSlashPostStrong, gs.RisingSlashPostStrong };
            gs.WideSlashPostStrong.followUps =
                new[] { gs.StrongCharged, gs.TackleLevel2, gs.RisingSlashPostStrong };
            gs.RisingSlash.followUps = new[] { gs.ChargedSlash, gs.WideSlash };
            gs.RisingSlashPostStrong.followUps =
                new[] { gs.StrongCharged, gs.WideSlashPostStrong };
            gs.Tackle.followUps = new[] { gs.StrongCharged, gs.LeapingWideSlash };
            gs.TackleLevel2.followUps =
                new[] { gs.TrueChargedFirstHit, gs.LeapingWideSlash };
            gs.LeapingWideSlash.followUps = new[] { gs.SideBlow };
            gs.Kick.followUps = new[] { gs.Tackle };
            gs.TrueChargedFirstHit.followUps =
                new[]
                {
                    gs.TrueChargedFinishNormal,
                    gs.TrueChargedFinishLevel1,
                    gs.TrueChargedFinishLevel2,
                    gs.TrueChargedFinishLevel3,
                };

            foreach (var attack in new[]
                      {
                          gs.StationaryDraw,
                          gs.DrawSlash,
                         gs.ChargedSlash,
                         gs.StrongCharged,
                         gs.TrueChargedFirstHit,
                         gs.TrueChargedFinishNormal,
                         gs.TrueChargedFinishLevel1,
                         gs.TrueChargedFinishLevel2,
                         gs.TrueChargedFinishLevel3,
                         gs.WideSlash,
                         gs.StrongWideSlash,
                         gs.LeapingWideSlash,
                         gs.WideSlashPostStrong,
                         gs.RisingSlash,
                         gs.RisingSlashPostStrong,
                         gs.SideBlow,
                         gs.SideBlowPostStrong,
                         gs.Tackle,
                         gs.TackleLevel2,
                         gs.Kick,
                     })
            {
                EditorUtility.SetDirty(attack);
            }

            return gs;
        }

        class VelkhanaMoves
        {
            public AttackDefinition AdjustBite;
            public AttackDefinition Rush;
            public AttackDefinition Rush2;
            public AttackDefinition BackStepPierce;
            public AttackDefinition TailThrust;
            public AttackDefinition TailSwing;
            public AttackDefinition StraightBreath;
            public AttackDefinition Sweep90Breath;
            public AttackDefinition Sweep180Breath;
            public AttackDefinition IceWave;
            public AttackDefinition AreaBreath;
            public AttackDefinition FreezeBreath;
            public AttackDefinition IceSpires;
            public AttackDefinition VerticalBreathFly;
            public AttackDefinition VerticalBreathFlyToGround;
            public AttackDefinition IceWaveStartFly;
            public AttackDefinition FlyTailStingToGround;
        }

        static VelkhanaMoves BuildVelkhanaAttacks()
        {
            // Names and sequence roles come from em124_55.nack. Frame counts remain original
            // placeholder timing because LMT motion IDs have not yet been mapped to semantic names.
            // Velkhana's local +Z points at the hunter, so reach is the Z extent of each box.
            return new VelkhanaMoves
            {
                AdjustBite = Attack("VK_AdjustBite", 22, 6, 26, 16, 34f, 18f,
                    new Vector3(0f, 1.4f, 3.3f), new Vector3(3.0f, 2.4f, 3.2f),
                    a => a.forwardMotionScale = 1.2f),

                Rush = Attack("VK_Rush", 34, 16, 46, 20, 64f, 34f,
                    new Vector3(0f, 1.2f, 4.5f), new Vector3(3.4f, 2.6f, 8.0f),
                    a => a.forwardMotionScale = 9.5f),

                Rush2 = Attack("VK_Rush2", 28, 13, 38, 18, 58f, 30f,
                    new Vector3(0f, 1.2f, 4.0f), new Vector3(3.2f, 2.5f, 7.0f),
                    a => a.forwardMotionScale = 7.0f),

                BackStepPierce = Attack("VK_BackStepPierce", 30, 7, 34, 18, 52f, 28f,
                    new Vector3(0f, 1.2f, 5.0f), new Vector3(2.2f, 2.2f, 7.0f),
                    a => a.forwardMotionScale = -2.4f),

                TailThrust = Attack("VK_TailThrust", 28, 6, 34, 20, 22f, 0f,
                    new Vector3(0f, 1f, 5.5f), new Vector3(1.8f, 2f, 6f),
                    a => a.forwardMotionScale = 1.0f),

                TailSwing = Attack("VK_TailSwing", 32, 13, 38, 19, 48f, 26f,
                    new Vector3(0f, 1.1f, 1.2f), new Vector3(10f, 2.2f, 8f)),

                StraightBreath = Attack("VK_StraightBreath", 46, 20, 50, 30, 30f, 18f,
                    new Vector3(0f, 1f, 11f), new Vector3(2.6f, 2f, 18f)),

                Sweep90Breath = Attack("VK_Sweep90Breath", 52, 26, 56, 34, 28f, 18f,
                    new Vector3(0f, 1f, 8f), new Vector3(14f, 2f, 10f)),

                Sweep180Breath = Attack("VK_Sweep180Breath", 58, 32, 62, 34, 30f, 20f,
                    new Vector3(0f, 1f, 7.5f), new Vector3(19f, 2f, 12f)),

                IceWave = Attack("VK_IceWave", 44, 18, 48, 28, 36f, 24f,
                    new Vector3(0f, 1f, 8.5f), new Vector3(6f, 2f, 15f)),

                AreaBreath = Attack("VK_AreaBreath", 68, 14, 58, 26, 42f, 28f,
                    new Vector3(0f, 1f, 6f), new Vector3(15f, 2f, 15f)),

                FreezeBreath = Attack("VK_FreezeBreath", 62, 18, 64, 28, 44f, 32f,
                    new Vector3(0f, 1f, 8f), new Vector3(3.2f, 2.2f, 14f)),

                IceSpires = Attack("VK_IceSpires", 60, 10, 62, 24, 34f, 24f,
                    new Vector3(0f, 1f, 9f), new Vector3(10f, 2f, 10f)),

                VerticalBreathFly = Attack("VK_VerticalBreathFly", 40, 18, 28, 20, 38f, 24f,
                    new Vector3(0f, 0.8f, 3f), new Vector3(8f, 2f, 8f)),

                // Semantic placeholders for Combat_Main.node_006. Timings and poses are original
                // graybox work; only the decoded action identities come from the reference table.
                VerticalBreathFlyToGround = Attack(
                    "VK_VerticalBreathFlyToGround", 42, 18, 30, 20, 40f, 24f,
                    new Vector3(0f, 0.8f, 3f), new Vector3(8f, 2f, 8f)),

                IceWaveStartFly = Attack(
                    "VK_IceWaveStartFly", 38, 16, 26, 20, 36f, 24f,
                    new Vector3(0f, 0.8f, 7f), new Vector3(7f, 2f, 12f)),

                FlyTailStingToGround = Attack(
                    "VK_FlyTailStingToGround", 36, 8, 34, 19, 56f, 34f,
                    new Vector3(0f, 1f, 5f), new Vector3(2.4f, 2.5f, 7f),
                    a => a.forwardMotionScale = 6.5f),
            };
        }

        // ---------- scene ----------

        /// <summary>
        /// An empty scene carries no lighting settings and every primitive shares one white
        /// material, which renders as a white-on-white blowout. Both have to be set explicitly.
        /// </summary>
        static void BuildLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = new Color(0.85f, 0.92f, 1f);
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.26f, 0.33f);
            RenderSettings.fog = false;
        }

        static Material Mat(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            material.SetFloat("_Glossiness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Paint(GameObject go, Material material)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Arena";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 m, wider than the camera sees
            ground.isStatic = true;
            Paint(ground, Mat("M_Arena", new Color(0.30f, 0.34f, 0.40f)));

            // Metre markers, so movement and attack reach are readable in a screenshot.
            var stripe = Mat("M_Marker", new Color(0.38f, 0.43f, 0.50f));
            for (int z = -20; z <= 20; z += 5)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = $"Marker{z}";
                line.transform.position = new Vector3(0f, 0.01f, z);
                line.transform.localScale = new Vector3(40f, 0.02f, 0.12f);
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
                Paint(line, stripe);
            }
        }

        static GameObject BuildHunter(GreatSword gs, int hurtboxLayer, int hunterLayer)
        {
            var hunter = new GameObject("Hunter");
            hunter.layer = hunterLayer;
            hunter.transform.position = new Vector3(0f, 1f, -5f);

            var cc = hunter.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = Vector3.zero;

            // Everything under VisualRoot is presentation-only. Rolling and charging can deform
            // this hierarchy without moving the CharacterController or gameplay hitboxes.
            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(hunter.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(visualRoot.transform, false);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            Paint(body, Mat("M_Hunter", new Color(0.92f, 0.55f, 0.18f)));

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(visualRoot.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.6f);
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            Paint(nose, Mat("M_HunterFacing", new Color(1f, 0.85f, 0.3f)));

            var handSocket = new GameObject("HandSocket");
            handSocket.transform.SetParent(visualRoot.transform, false);
            handSocket.transform.localPosition = new Vector3(0.42f, 0.08f, 0.92f);
            handSocket.transform.localRotation = Quaternion.Euler(-8f, 0f, -5f);

            var backSocket = new GameObject("BackSocket");
            backSocket.transform.SetParent(visualRoot.transform, false);
            backSocket.transform.localPosition = new Vector3(0f, 0.05f, -0.42f);
            backSocket.transform.localRotation =
                Quaternion.LookRotation(new Vector3(0.58f, 0.8f, 0.12f).normalized, Vector3.up);

            // Stands in for the sword arc. It starts sheathed across the hunter's back and the
            // presentation component moves it to the hand when the combat state draws it.
            var sword = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sword.name = "SwordVisual";
            sword.transform.SetParent(visualRoot.transform, false);
            sword.transform.localPosition = backSocket.transform.localPosition;
            sword.transform.localRotation = backSocket.transform.localRotation;
            sword.transform.localScale = new Vector3(0.12f, 0.12f, 1.8f);
            UnityEngine.Object.DestroyImmediate(sword.GetComponent<Collider>());
            Paint(sword, Mat("M_Sword", new Color(0.75f, 0.78f, 0.85f)));

            var blade = new GameObject("BladePoint");
            blade.transform.SetParent(hunter.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0f, 1.6f);

            var controller = hunter.AddComponent<HunterController>();
            controller.stationaryDraw = gs.StationaryDraw;
            controller.drawSlash = gs.DrawSlash;
            controller.chargedSlash = gs.ChargedSlash;
            controller.strongChargedSlash = gs.StrongCharged;
            controller.trueChargedSlash = gs.TrueChargedFirstHit;
            controller.trueChargedFinishNormal = gs.TrueChargedFinishNormal;
            controller.trueChargedFinishLevel1 = gs.TrueChargedFinishLevel1;
            controller.trueChargedFinishLevel2 = gs.TrueChargedFinishLevel2;
            controller.trueChargedFinishLevel3 = gs.TrueChargedFinishLevel3;
            controller.wideSlash = gs.WideSlash;
            controller.strongWideSlash = gs.StrongWideSlash;
            controller.leapingWideSlash = gs.LeapingWideSlash;
            controller.wideSlashPostStrong = gs.WideSlashPostStrong;
            controller.risingSlash = gs.RisingSlash;
            controller.risingSlashPostStrong = gs.RisingSlashPostStrong;
            controller.sideBlow = gs.SideBlow;
            controller.sideBlowPostStrong = gs.SideBlowPostStrong;
            controller.tackle = gs.Tackle;
            controller.tackleLevel2 = gs.TackleLevel2;
            controller.kick = gs.Kick;
            controller.bladePoint = blade.transform;
            controller.hurtboxLayers = 1 << hurtboxLayer;

            var presentation = hunter.AddComponent<HunterPresentation>();
            presentation.visualRoot = visualRoot.transform;
            presentation.body = body.transform;
            presentation.sword = sword.transform;
            presentation.handSocket = handSocket.transform;
            presentation.backSocket = backSocket.transform;

            // The technical-demo script intentionally watches several complete monster sequences.
            // Extra health keeps that observation window alive without weakening individual hits.
            hunter.AddComponent<HunterHealth>().maxHealth = 500f;

            return hunter;
        }

        static GameObject BuildCamera(GameObject hunter)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.18f);

            // 58 degrees: inside the 55-65 band the plan calls for, so ground hazards stay readable.
            go.transform.rotation = Quaternion.Euler(58f, 0f, 0f);
            go.transform.position = new Vector3(0f, 17f, -12f);

            go.AddComponent<AudioListener>();
            hunter.GetComponent<HunterController>().aimCamera = cam;
            return go;
        }

        readonly struct PartSpec
        {
            public readonly string Name;
            public readonly BodyPart Part;
            public readonly Vector3 Position;
            public readonly Vector3 Size;
            public readonly float Multiplier;
            public readonly float BreakThreshold;
            public readonly bool Armored;

            public PartSpec(string name, BodyPart part, Vector3 position, Vector3 size,
                float multiplier, float breakThreshold, bool armored)
            {
                Name = name; Part = part; Position = position; Size = size;
                Multiplier = multiplier; BreakThreshold = breakThreshold; Armored = armored;
            }
        }

        /// <summary>Head reads hottest because it is the Great Sword's punish target.</summary>
        static Color PartColor(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return new Color(0.86f, 0.30f, 0.26f);
                case BodyPart.Torso: return new Color(0.44f, 0.51f, 0.62f);
                case BodyPart.Wing: return new Color(0.56f, 0.76f, 0.86f);
                case BodyPart.Tail: return new Color(0.34f, 0.62f, 0.60f);
                default: return new Color(0.37f, 0.44f, 0.57f);
            }
        }

        sealed class VelkhanaVisuals
        {
            public Transform Root, Torso, Neck, Head, WingL, WingR;
            public Transform FrontLegL, FrontLegR, RearLegL, RearLegR;
            public Transform TailRoot, TailMiddle, TailTip, BreathBeam;
            public Light BreathLight, PhaseLight;
        }

        static Transform Pivot(Transform parent, string name, Vector3 localPosition)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPosition;
            return pivot;
        }

        static Transform VisualPrimitive(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var visual = GameObject.CreatePrimitive(primitive);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localScale = localScale;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            Paint(visual, material);
            return visual.transform;
        }

        /// <summary>
        /// Original primitive rig whose proportions follow the extracted EM124 model/motion
        /// inventory. It never contains gameplay colliders; all damage volumes stay under
        /// GameplayHurtboxes and continue to use Velkhana's root transform.
        /// </summary>
        static VelkhanaVisuals BuildVelkhanaVisuals(Transform parent)
        {
            var visuals = new VelkhanaVisuals();
            visuals.Root = Pivot(parent, "VisualRoot", Vector3.zero);
            visuals.Torso = Pivot(visuals.Root, "TorsoPivot", new Vector3(0f, 2f, 0.5f));
            VisualPrimitive(
                visuals.Torso, "TorsoVisual", PrimitiveType.Cube, Vector3.zero,
                new Vector3(2.6f, 2.4f, 4.5f), Mat("M_Torso", PartColor(BodyPart.Torso)));

            visuals.Neck = Pivot(visuals.Torso, "NeckPivot", new Vector3(0f, 0.2f, 2.2f));
            VisualPrimitive(
                visuals.Neck, "NeckVisual", PrimitiveType.Cube, new Vector3(0f, 0f, 0.9f),
                new Vector3(0.75f, 0.75f, 1.8f), Mat("M_Neck", new Color(0.48f, 0.62f, 0.72f)));

            visuals.Head = Pivot(visuals.Neck, "HeadPivot", new Vector3(0f, 0f, 1.85f));
            VisualPrimitive(
                visuals.Head, "HeadVisual", PrimitiveType.Cube, Vector3.zero,
                new Vector3(1.2f, 1.1f, 1.65f), Mat("M_Head", PartColor(BodyPart.Head)));
            VisualPrimitive(
                visuals.Head, "HeadCrest", PrimitiveType.Cube, new Vector3(0f, 0.75f, -0.2f),
                new Vector3(0.25f, 1.1f, 1.2f), Mat("M_IceCrest", new Color(0.5f, 0.9f, 1f)));

            visuals.WingL = Pivot(visuals.Torso, "WingLPivot", new Vector3(-1.1f, 0.75f, 0.15f));
            VisualPrimitive(
                visuals.WingL, "WingLVisual", PrimitiveType.Cube, new Vector3(-1.5f, 0f, 0f),
                new Vector3(3f, 0.25f, 2.7f), Mat("M_Wing", PartColor(BodyPart.Wing)));
            visuals.WingR = Pivot(visuals.Torso, "WingRPivot", new Vector3(1.1f, 0.75f, 0.15f));
            VisualPrimitive(
                visuals.WingR, "WingRVisual", PrimitiveType.Cube, new Vector3(1.5f, 0f, 0f),
                new Vector3(3f, 0.25f, 2.7f), Mat("M_Wing", PartColor(BodyPart.Wing)));

            Material legMaterial = Mat("M_Legs", new Color(0.37f, 0.44f, 0.57f));
            visuals.FrontLegL = Pivot(visuals.Torso, "FrontLegLPivot", new Vector3(-1.1f, -1.15f, 1.75f));
            VisualPrimitive(
                visuals.FrontLegL, "FrontLegLVisual", PrimitiveType.Cube,
                new Vector3(0f, -0.7f, 0f), new Vector3(0.65f, 1.65f, 0.65f), legMaterial);
            visuals.FrontLegR = Pivot(visuals.Torso, "FrontLegRPivot", new Vector3(1.1f, -1.15f, 1.75f));
            VisualPrimitive(
                visuals.FrontLegR, "FrontLegRVisual", PrimitiveType.Cube,
                new Vector3(0f, -0.7f, 0f), new Vector3(0.65f, 1.65f, 0.65f), legMaterial);
            visuals.RearLegL = Pivot(visuals.Torso, "RearLegLPivot", new Vector3(-1.2f, -1.1f, -1.65f));
            VisualPrimitive(
                visuals.RearLegL, "RearLegLVisual", PrimitiveType.Cube,
                new Vector3(0f, -0.7f, 0f), new Vector3(0.75f, 1.7f, 0.75f), legMaterial);
            visuals.RearLegR = Pivot(visuals.Torso, "RearLegRPivot", new Vector3(1.2f, -1.1f, -1.65f));
            VisualPrimitive(
                visuals.RearLegR, "RearLegRVisual", PrimitiveType.Cube,
                new Vector3(0f, -0.7f, 0f), new Vector3(0.75f, 1.7f, 0.75f), legMaterial);

            Material tailMaterial = Mat("M_Tail", PartColor(BodyPart.Tail));
            visuals.TailRoot = Pivot(visuals.Torso, "TailRoot", new Vector3(0f, 0f, -2.25f));
            VisualPrimitive(
                visuals.TailRoot, "TailRootVisual", PrimitiveType.Cube,
                new Vector3(0f, 0f, -1.1f), new Vector3(0.9f, 0.9f, 2.2f), tailMaterial);
            visuals.TailMiddle = Pivot(visuals.TailRoot, "TailMiddle", new Vector3(0f, 0f, -2.2f));
            VisualPrimitive(
                visuals.TailMiddle, "TailMiddleVisual", PrimitiveType.Cube,
                new Vector3(0f, 0f, -1f), new Vector3(0.68f, 0.68f, 2f), tailMaterial);
            visuals.TailTip = Pivot(visuals.TailMiddle, "TailTip", new Vector3(0f, 0f, -2f));
            VisualPrimitive(
                visuals.TailTip, "TailTipVisual", PrimitiveType.Cube,
                new Vector3(0f, 0f, -0.8f), new Vector3(0.38f, 0.38f, 1.6f),
                Mat("M_TailTip", new Color(0.55f, 0.9f, 1f)));

            Material beamMaterial = Mat("M_IceBreath", new Color(0.32f, 0.9f, 1f));
            beamMaterial.EnableKeyword("_EMISSION");
            beamMaterial.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f) * 2.2f);
            visuals.BreathBeam = VisualPrimitive(
                visuals.Head, "BreathBeam", PrimitiveType.Cube,
                new Vector3(0f, 0f, 6f), new Vector3(0.28f, 0.28f, 11f), beamMaterial);
            visuals.BreathBeam.GetComponent<Renderer>().enabled = false;

            var breathGlow = new GameObject("BreathGlow");
            breathGlow.transform.SetParent(visuals.Head, false);
            breathGlow.transform.localPosition = new Vector3(0f, 0f, 1.1f);
            visuals.BreathLight = breathGlow.AddComponent<Light>();
            visuals.BreathLight.type = LightType.Point;
            visuals.BreathLight.range = 7f;
            visuals.BreathLight.shadows = LightShadows.None;
            visuals.BreathLight.enabled = false;

            var phaseGlow = new GameObject("PhaseGlow");
            phaseGlow.transform.SetParent(visuals.Torso, false);
            phaseGlow.transform.localPosition = new Vector3(0f, 1f, 0f);
            visuals.PhaseLight = phaseGlow.AddComponent<Light>();
            visuals.PhaseLight.type = LightType.Point;
            visuals.PhaseLight.range = 9f;
            visuals.PhaseLight.shadows = LightShadows.None;
            visuals.PhaseLight.enabled = false;

            return visuals;
        }

        static GameObject BuildVelkhana(Transform hunter, VelkhanaMoves moves, int hurtboxLayer, int hunterLayer)
        {
            var root = new GameObject("Velkhana");
            root.transform.position = new Vector3(0f, 0f, 6f);
            root.transform.rotation = Quaternion.LookRotation(hunter.position - root.transform.position, Vector3.up);
            VelkhanaVisuals visuals = BuildVelkhanaVisuals(root.transform);

            // Head takes the most damage, which is what makes it the Great Sword's punish target.
            var specs = new[]
            {
                new PartSpec("Head",     BodyPart.Head,     new Vector3(0f, 2.2f, 4.2f),    new Vector3(1.2f, 1.2f, 1.8f), 1.7f, 320f, true),
                new PartSpec("Torso",    BodyPart.Torso,    new Vector3(0f, 2.0f, 0.5f),    new Vector3(2.6f, 2.4f, 4.5f), 1.0f, 900f, false),
                new PartSpec("WingL",    BodyPart.Wing,     new Vector3(-2.6f, 2.8f, 0.5f), new Vector3(3.0f, 0.3f, 2.6f), 0.8f, 400f, true),
                new PartSpec("WingR",    BodyPart.Wing,     new Vector3(2.6f, 2.8f, 0.5f),  new Vector3(3.0f, 0.3f, 2.6f), 0.8f, 400f, true),
                new PartSpec("FrontLegL",BodyPart.FrontLeg, new Vector3(-1.3f, 0.9f, 2.4f), new Vector3(0.7f, 1.8f, 0.7f), 0.9f, 350f, false),
                new PartSpec("FrontLegR",BodyPart.FrontLeg, new Vector3(1.3f, 0.9f, 2.4f),  new Vector3(0.7f, 1.8f, 0.7f), 0.9f, 350f, false),
                new PartSpec("RearLegL", BodyPart.RearLeg,  new Vector3(-1.4f, 0.9f, -1.4f),new Vector3(0.8f, 1.8f, 0.8f), 0.85f, 380f, false),
                new PartSpec("RearLegR", BodyPart.RearLeg,  new Vector3(1.4f, 0.9f, -1.4f), new Vector3(0.8f, 1.8f, 0.8f), 0.85f, 380f, false),
                new PartSpec("Tail",     BodyPart.Tail,     new Vector3(0f, 1.8f, -4.5f),   new Vector3(0.8f, 0.8f, 5.0f), 0.75f, 420f, true),
            };

            // Hurtboxes are triggers, so without a solid body the hunter walks straight through her.
            var blocker = new GameObject("BodyBlocker");
            blocker.transform.SetParent(root.transform, false);
            var blockerCollider = blocker.AddComponent<BoxCollider>();
            blockerCollider.center = new Vector3(0f, 1.5f, 0.5f);
            blockerCollider.size = new Vector3(3.0f, 3f, 6f);

            var armored = new List<BodyPartHurtbox>();
            Transform gameplayHurtboxes = Pivot(root.transform, "GameplayHurtboxes", Vector3.zero);

            foreach (var spec in specs)
            {
                // These are invisible, stationary gameplay volumes. Procedural animation only
                // touches the separate VisualRoot hierarchy created above.
                var go = new GameObject($"{spec.Name}Hurtbox");
                go.layer = hurtboxLayer;
                go.transform.SetParent(gameplayHurtboxes, false);
                go.transform.localPosition = spec.Position;
                go.transform.localScale = spec.Size;
                go.AddComponent<BoxCollider>().isTrigger = true;

                var hurtbox = go.AddComponent<BodyPartHurtbox>();
                hurtbox.part = spec.Part;
                hurtbox.damageMultiplier = spec.Multiplier;
                hurtbox.breakThreshold = spec.BreakThreshold;
                hurtbox.iceArmorHealth = 0f;

                if (spec.Armored) armored.Add(hurtbox);
            }

            var brain = root.AddComponent<VelkhanaBrain>();
            brain.hunter = hunter;
            brain.hunterLayers = 1 << hunterLayer;
            brain.armoredParts = armored.ToArray();
            brain.closeRange = 8.5f;
            brain.mediumRange = 17f;
            brain.combatEntryVerticalRange = 7.5f;
            brain.neutralFrames = 30;
            brain.automaticEnrage = true;
            brain.rageDamageThreshold = 360f;
            brain.automaticPhaseProgression = true;
            brain.completedSequencesPerStage = 3;
            brain.maxHealth = 3000f;
            brain.ResetVitality();
            brain.options = new List<MonsterAttackOption>
            {
                // Global.node_004: adjust_bite. Common inside 3-7 m in nodes 093/094.
                EmOption(
                    "Global.node_004", moves.AdjustBite, RangeBand.Close,
                    0f, 7f, 0f, 90f, 20f, 90,
                    modeWeights: new Vector3(1f, 1f, 1f), enragedMultiplier: 0.8f),

                // Global.node_005/006: long rush and rush_2 approach actions.
                EmOption(
                    "Global.node_005", moves.Rush, RangeBand.Far,
                    16f, 28f, 0f, 90f, 18f, 260,
                    modeWeights: new Vector3(1f, 0.7f, 0.7f), enragedMultiplier: 1.25f),
                EmOption(
                    "Global.node_006", moves.Rush2, RangeBand.Medium,
                    7f, 16f, 0f, 100f, 24f, 190,
                    modeWeights: new Vector3(1f, 1.15f, 1f), enragedMultiplier: 1.2f),

                // Global.node_009 is retained as a Global.node_087 lookup leaf. It does not
                // independently compete in the flat table and has no invented facing predicate.
                EmOption(
                    "Global.node_009", moves.BackStepPierce, RangeBand.Close,
                    0f, 13f, 0f, 180f, 20f, 170,
                    modeWeights: new Vector3(1f, 1f, 1.15f), enragedMultiplier: 1.2f,
                    flatGroundSelection: false),

                // Global.node_020 selects single tail attacks while calm and triple strings enraged.
                EmOption(
                    "Global.node_020", moves.TailThrust, RangeBand.Close,
                    2f, 9f, 0f, 180f, 42f, 150,
                    modeWeights: new Vector3(1f, 1f, 1f), enragedMultiplier: 1.35f,
                    calmFollowUps: Array.Empty<AttackDefinition>(),
                    enragedFollowUps: new[] { moves.TailThrust, moves.TailSwing }),

                // Global.node_013: tail_swing covers hunters who commit behind or beside her.
                EmOption(
                    "Global.node_013", moves.TailSwing, RangeBand.Close,
                    0f, 9f, 55f, 180f, 22f, 170,
                    modeWeights: new Vector3(1f, 1f, 1.2f), enragedMultiplier: 1.15f),

                // Global.node_035: straight_breath is common across all three mode buckets.
                EmOption(
                    "Global.node_035", moves.StraightBreath, RangeBand.Medium,
                    6f, 28f, 0f, 90f, 36f, 230,
                    modeWeights: new Vector3(1f, 1f, 1f), enragedMultiplier: 1.25f),

                // Global.node_036/037: function#101 mode-specific 90/180 degree breath sweeps.
                EmOption(
                    "Global.node_036", moves.Sweep90Breath, RangeBand.Medium,
                    6f, 16f, 0f, 135f, 18f, 280,
                    modeWeights: new Vector3(0f, 1f, 0f), enragedMultiplier: 1.35f,
                    minimumStage: ArmorStage.IceArmorStage1,
                    modes: VelkhanaCombatModeMask.Mode1),
                EmOption(
                    "Global.node_037", moves.Sweep180Breath, RangeBand.Medium,
                    6f, 16f, 0f, 180f, 20f, 310,
                    modeWeights: new Vector3(0f, 0f, 1f), enragedMultiplier: 1.45f,
                    minimumStage: ArmorStage.IceArmorStage2,
                    modes: VelkhanaCombatModeMask.Mode2),

                // Global.node_024/023: ice wave and area breath control mid/far space.
                EmOption(
                    "Global.node_024", moves.IceWave, RangeBand.Medium,
                    3.5f, 13f, 0f, 150f, 24f, 250,
                    modeWeights: new Vector3(0.75f, 1.2f, 1.35f), enragedMultiplier: 1.25f),
                EmOption(
                    "Global.node_023", moves.AreaBreath, RangeBand.Far,
                    8f, 23f, 0f, 120f, 18f, 340,
                    modeWeights: new Vector3(0.55f, 1.1f, 1.35f), enragedMultiplier: 1.35f,
                    minimumStage: ArmorStage.IceArmorStage1),

                // Global.node_040: narrow forward freeze branch, favoured in critical/enraged play.
                EmOption(
                    "Global.node_040", moves.FreezeBreath, RangeBand.Medium,
                    3f, 13f, 0f, 30f, 14f, 360,
                    modeWeights: new Vector3(0.4f, 0.8f, 1.3f), enragedMultiplier: 1.6f,
                    criticalMultiplier: 1.75f,
                    minimumStage: ArmorStage.IceArmorStage2),

                // ice_drop/ice control semantic placeholder from the recovered ground inventory.
                EmOption(
                    "Global.node_025", moves.IceSpires, RangeBand.Far,
                    10f, 28f, 0f, 180f, 16f, 380,
                    modeWeights: new Vector3(0.45f, 1.15f, 1.4f), enragedMultiplier: 1.3f,
                    minimumStage: ArmorStage.IceArmorStage1),

                // Explicit ground gateway into Combat_Main.node_006. Its pending action is not
                // played: takeoff completes in airborne Observe and the exact chooser dispatches.
                EmOption(
                    "Combat_Main.node_006.entry", moves.VerticalBreathFly, RangeBand.Medium,
                    4f, 17f, 0f, 180f, 6f, 520,
                    modeWeights: new Vector3(0f, 0.8f, 1.2f), enragedMultiplier: 1.5f,
                    minimumStage: ArmorStage.IceArmorStage1,
                    modes: VelkhanaCombatModeMask.Mode1 | VelkhanaCombatModeMask.Mode2,
                    takeOff: true,
                    enterAerialChooser: true),

                // Global.node_063 remains its independently inferred takeoff/aerial/landing string.
                EmOption(
                    "Global.node_063", moves.VerticalBreathFly, RangeBand.Medium,
                    4f, 17f, 0f, 180f, 10f, 520,
                    modeWeights: new Vector3(0f, 0.8f, 1.2f), enragedMultiplier: 1.5f,
                    minimumStage: ArmorStage.IceArmorStage1,
                    modes: VelkhanaCombatModeMask.Mode1 | VelkhanaCombatModeMask.Mode2,
                    calmFollowUps: new[] { moves.IceWave, moves.FlyTailStingToGround },
                    enragedFollowUps: new[]
                    {
                        moves.VerticalBreathFly, moves.IceWave, moves.FlyTailStingToGround,
                    },
                    takeOff: true,
                    landAfter: true),

                // Combat_Main.node_006 target families. Generic weights/cooldowns/history are
                // bypassed while airborne; the decoded selector chooses only by family.
                EmOption(
                    "Global.node_051", moves.VerticalBreathFlyToGround, RangeBand.Medium,
                    0f, 28f, 0f, 180f, 1f, 0,
                    modeWeights: Vector3.one, enragedMultiplier: 1f,
                    airRequirement: VelkhanaAirRequirement.Airborne,
                    aerialFamily: VelkhanaAerialOptionFamily.Global051,
                    landAfter: true),
                EmOption(
                    "Global.node_052", moves.IceWaveStartFly, RangeBand.Medium,
                    0f, 28f, 0f, 180f, 1f, 0,
                    modeWeights: Vector3.one, enragedMultiplier: 1f,
                    airRequirement: VelkhanaAirRequirement.Airborne,
                    aerialFamily: VelkhanaAerialOptionFamily.Global052),
            };
            brain.RefreshHurtboxBindings();

            var presentation = root.AddComponent<VelkhanaPresentation>();
            presentation.visualRoot = visuals.Root;
            presentation.torsoPivot = visuals.Torso;
            presentation.neckPivot = visuals.Neck;
            presentation.headPivot = visuals.Head;
            presentation.wingLPivot = visuals.WingL;
            presentation.wingRPivot = visuals.WingR;
            presentation.frontLegLPivot = visuals.FrontLegL;
            presentation.frontLegRPivot = visuals.FrontLegR;
            presentation.rearLegLPivot = visuals.RearLegL;
            presentation.rearLegRPivot = visuals.RearLegR;
            presentation.tailRoot = visuals.TailRoot;
            presentation.tailMiddle = visuals.TailMiddle;
            presentation.tailTip = visuals.TailTip;
            presentation.breathBeam = visuals.BreathBeam;
            presentation.breathLight = visuals.BreathLight;
            presentation.phaseLight = visuals.PhaseLight;
            presentation.adjustBite = moves.AdjustBite;
            presentation.rush = moves.Rush;
            presentation.rush2 = moves.Rush2;
            presentation.backStepPierce = moves.BackStepPierce;
            presentation.tailThrust = moves.TailThrust;
            presentation.tailSwing = moves.TailSwing;
            presentation.straightBreath = moves.StraightBreath;
            presentation.sweep90Breath = moves.Sweep90Breath;
            presentation.sweep180Breath = moves.Sweep180Breath;
            presentation.iceWave = moves.IceWave;
            presentation.areaBreath = moves.AreaBreath;
            presentation.freezeBreath = moves.FreezeBreath;
            presentation.iceSpires = moves.IceSpires;
            presentation.verticalBreathFly = moves.VerticalBreathFly;
            presentation.verticalBreathFlyToGround = moves.VerticalBreathFlyToGround;
            presentation.iceWaveStartFly = moves.IceWaveStartFly;
            presentation.flyTailStingToGround = moves.FlyTailStingToGround;

            return root;
        }

        static MonsterAttackOption EmOption(
            string thkNode,
            AttackDefinition attack,
            RangeBand band,
            float minimumDistance,
            float maximumDistance,
            float minimumFacingAngle,
            float maximumFacingAngle,
            float weight,
            int cooldown,
            Vector3 modeWeights,
            float enragedMultiplier,
            float criticalMultiplier = 1f,
            ArmorStage minimumStage = ArmorStage.Neutral,
            VelkhanaCombatModeMask modes = VelkhanaCombatModeMask.All,
            AttackDefinition[] calmFollowUps = null,
            AttackDefinition[] enragedFollowUps = null,
            bool takeOff = false,
            bool landAfter = false,
            bool enterAerialChooser = false,
            VelkhanaAirRequirement airRequirement = VelkhanaAirRequirement.Grounded,
            VelkhanaAerialOptionFamily aerialFamily = VelkhanaAerialOptionFamily.None,
            bool flatGroundSelection = true)
        {
            return new MonsterAttackOption
            {
                attack = attack,
                band = band,
                minimumStage = minimumStage,
                weight = weight,
                cooldownFrames = cooldown,
                enragedWeightMultiplier = enragedMultiplier,
                criticalWeightMultiplier = criticalMultiplier,
                useEm124Conditions = true,
                thkNode = thkNode,
                minimumDistance = minimumDistance,
                maximumDistance = maximumDistance,
                maximumVerticalDistance = 7.5f,
                minimumFacingAngle = minimumFacingAngle,
                maximumFacingAngle = maximumFacingAngle,
                modes = modes,
                airRequirement = airRequirement,
                aerialFamily = aerialFamily,
                useInFlatGroundSelector = flatGroundSelection,
                mode0WeightMultiplier = modeWeights.x,
                mode1WeightMultiplier = modeWeights.y,
                mode2WeightMultiplier = modeWeights.z,
                calmFollowUps = calmFollowUps ?? Array.Empty<AttackDefinition>(),
                enragedFollowUps = enragedFollowUps ?? Array.Empty<AttackDefinition>(),
                takeOffBeforeSequence = takeOff,
                enterAerialChooserAfterTakeoff = enterAerialChooser,
                landAfterSequence = landAfter,
            };
        }

        // ---------- project settings ----------

        static int EnsureLayer(string name)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var tagManager = new SerializedObject(asset);
            var layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            throw new InvalidOperationException($"No free user layer slot for '{name}'.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
