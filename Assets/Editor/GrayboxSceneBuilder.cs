using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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
            public AttackDefinition DrawSlash, ChargedSlash, StrongCharged, TrueCharged, WideSlash, Tackle;
        }

        static GreatSword BuildGreatSwordAttacks()
        {
            var gs = new GreatSword();

            gs.DrawSlash = Attack("GS_DrawSlash", 22, 4, 34, 14, 90f, 35f,
                new Vector3(0f, 1f, 1.7f), new Vector3(2.6f, 2f, 2.8f),
                a => a.forwardMotionScale = 1.4f);

            gs.WideSlash = Attack("GS_WideSlash", 18, 5, 30, 12, 70f, 45f,
                new Vector3(0f, 1f, 1.4f), new Vector3(3.6f, 2f, 2.4f));

            gs.Tackle = Attack("GS_Tackle", 10, 6, 22, 6, 30f, 20f,
                new Vector3(0f, 1f, 1.2f), new Vector3(1.6f, 2f, 2.2f), a =>
            {
                a.hyperArmor = true;
                a.incomingDamageReduction = 0.5f;
                a.forwardMotionScale = 2.2f;
                a.cancelWindowStart = 16;
            });

            gs.TrueCharged = Attack("GS_TrueChargedSlash", 30, 6, 52, 16, 180f, 90f,
                new Vector3(0f, 1f, 2f), new Vector3(3.4f, 2f, 3.6f), a =>
            {
                a.requiresPreviousHitConnected = true;
                a.forwardMotionScale = 1.8f;
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

            // The combo graph is these links and nothing else.
            gs.ChargedSlash.followUps = new[] { gs.StrongCharged, gs.Tackle };
            gs.StrongCharged.followUps = new[] { gs.TrueCharged, gs.Tackle };
            gs.Tackle.followUps = new[] { gs.StrongCharged, gs.TrueCharged };
            gs.DrawSlash.followUps = new[] { gs.ChargedSlash };
            gs.WideSlash.followUps = new[] { gs.Tackle };

            EditorUtility.SetDirty(gs.ChargedSlash);
            EditorUtility.SetDirty(gs.StrongCharged);
            EditorUtility.SetDirty(gs.Tackle);
            EditorUtility.SetDirty(gs.DrawSlash);
            EditorUtility.SetDirty(gs.WideSlash);
            return gs;
        }

        class VelkhanaMoves
        {
            public AttackDefinition TailThrust, BodyCheck, IceBeam, SweepingBreath, IceSpires;
        }

        static VelkhanaMoves BuildVelkhanaAttacks()
        {
            // Velkhana's local +Z points at the hunter, so reach is the Z extent of each box.
            return new VelkhanaMoves
            {
                TailThrust = Attack("VK_TailThrust", 28, 6, 34, 20, 22f, 0f,
                    new Vector3(0f, 1f, 5.5f), new Vector3(1.8f, 2f, 6f),
                    a => a.forwardMotionScale = 1.0f),

                BodyCheck = Attack("VK_BodyCheck", 34, 8, 40, 22, 75f, 0f,
                    new Vector3(0f, 1f, 4f), new Vector3(4.5f, 2f, 6f),
                    a => a.forwardMotionScale = 4.5f),

                IceBeam = Attack("VK_IceBeam", 46, 20, 50, 30, 30f, 0f,
                    new Vector3(0f, 1f, 11f), new Vector3(2.6f, 2f, 18f)),

                SweepingBreath = Attack("VK_SweepingBreath", 52, 26, 56, 34, 26f, 0f,
                    new Vector3(0f, 1f, 8f), new Vector3(14f, 2f, 10f)),

                IceSpires = Attack("VK_IceSpires", 60, 10, 62, 24, 34f, 0f,
                    new Vector3(0f, 1f, 9f), new Vector3(10f, 2f, 10f)),
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

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(hunter.transform, false);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            Paint(body, Mat("M_Hunter", new Color(0.92f, 0.55f, 0.18f)));

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(hunter.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.6f);
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            Paint(nose, Mat("M_HunterFacing", new Color(1f, 0.85f, 0.3f)));

            // Stands in for the sword arc so the reach of a swing is visible.
            var sword = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sword.name = "SwordVisual";
            sword.transform.SetParent(hunter.transform, false);
            sword.transform.localPosition = new Vector3(0.35f, 0.2f, 0.9f);
            sword.transform.localScale = new Vector3(0.12f, 0.12f, 1.8f);
            UnityEngine.Object.DestroyImmediate(sword.GetComponent<Collider>());
            Paint(sword, Mat("M_Sword", new Color(0.75f, 0.78f, 0.85f)));

            var blade = new GameObject("BladePoint");
            blade.transform.SetParent(hunter.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0f, 1.6f);

            var controller = hunter.AddComponent<HunterController>();
            controller.drawSlash = gs.DrawSlash;
            controller.chargedSlash = gs.ChargedSlash;
            controller.wideSlash = gs.WideSlash;
            controller.tackle = gs.Tackle;
            controller.bladePoint = blade.transform;
            controller.hurtboxLayers = 1 << hurtboxLayer;

            hunter.AddComponent<HunterHealth>().maxHealth = 150f;

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

        static GameObject BuildVelkhana(Transform hunter, VelkhanaMoves moves, int hurtboxLayer, int hunterLayer)
        {
            var root = new GameObject("Velkhana");
            root.transform.position = new Vector3(0f, 0f, 6f);
            root.transform.rotation = Quaternion.LookRotation(hunter.position - root.transform.position, Vector3.up);

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

            foreach (var spec in specs)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = spec.Name;
                go.layer = hurtboxLayer;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = spec.Position;
                go.transform.localScale = spec.Size;
                go.GetComponent<BoxCollider>().isTrigger = true;
                Paint(go, Mat($"M_{spec.Part}", PartColor(spec.Part)));

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
            brain.options = new List<MonsterAttackOption>
            {
                Option(moves.TailThrust,     RangeBand.Close,  ArmorStage.Neutral,        1.0f, 150, false),
                Option(moves.BodyCheck,      RangeBand.Close,  ArmorStage.Neutral,        0.8f, 240, true),
                Option(moves.IceBeam,        RangeBand.Medium, ArmorStage.Neutral,        1.0f, 300, true),
                Option(moves.SweepingBreath, RangeBand.Medium, ArmorStage.IceArmorStage1, 1.2f, 360, false),
                Option(moves.IceSpires,      RangeBand.Far,    ArmorStage.Neutral,        1.0f, 420, false),
            };

            return root;
        }

        static MonsterAttackOption Option(
            AttackDefinition attack, RangeBand band, ArmorStage stage,
            float weight, int cooldown, bool requiresFront)
        {
            return new MonsterAttackOption
            {
                attack = attack,
                band = band,
                minimumStage = stage,
                weight = weight,
                cooldownFrames = cooldown,
                requiresHunterInFront = requiresFront,
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
