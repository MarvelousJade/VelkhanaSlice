using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.Automation
{
    /// <summary>
    /// Main-thread bridge between the fixed-step combat simulation and loopback automation tools.
    /// Start a player with -automation; the Unity Editor starts the server automatically outside
    /// batch mode. All Unity object access remains on the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class GameplayAutomationBridge : MonoBehaviour
    {
        const int DefaultPort = 47777;
        const int EventCapacity = 2048;
        const int MainThreadCommandTimeoutMilliseconds = 660000;

        public HunterController hunterController;
        public HunterHealth hunterHealth;
        public VelkhanaBrain brain;

        [Header("Server")]
        [Min(1)] public int port = DefaultPort;
        public bool enableInEditor = true;

        public bool IsActive => _active;
        public bool IsPaused => _paused;
        public long SimulationFrame => _simulationFrame;
        public string CurrentStateJson => _latestStateJson;

        static GameplayAutomationBridge _instance;

        sealed class MainThreadCommand
        {
            public string path;
            public string body;
            public readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);
            public int statusCode = 500;
            public string response;

            public void Complete(int status, string json)
            {
                statusCode = status;
                response = json;
                try { completed.Set(); }
                catch (ObjectDisposedException) { }
            }
        }

        sealed class EventJson
        {
            public long sequence;
            public string json;
        }

        sealed class PartObservation
        {
            public float damage;
            public float armor;
            public bool broken;
        }

        readonly ConcurrentQueue<MainThreadCommand> _commands =
            new ConcurrentQueue<MainThreadCommand>();
        readonly object _eventLock = new object();
        readonly List<EventJson> _eventRing = new List<EventJson>(EventCapacity);
        readonly List<AutomationEvent> _frameEvents = new List<AutomationEvent>();
        readonly Dictionary<string, PartObservation> _previousParts =
            new Dictionary<string, PartObservation>(StringComparer.Ordinal);

        AutomationHttpServer _server;
        StreamWriter _telemetry;
        string _telemetryPath;
        volatile string _latestStateJson;
        bool _active;
        bool _shuttingDown;
        bool _paused;
        long _simulationFrame;
        long _eventSequence;
        int _pendingStepFrames;
        MainThreadCommand _pendingStepCommand;
        MainThreadCommand _pendingResetCommand;
        AutomationResetRequest _pendingReset;

        bool _previousStateReady;
        string _previousHunterState;
        string _previousHunterNode;
        string _previousHunterAttack;
        float _previousHunterHealth;
        string _previousMonsterState;
        string _previousMonsterAttack;
        string _previousThkTrace;
        float _previousMonsterHealth;
        ArmorStage _previousArmorStage;
        bool _previousEnraged;
        int _previousSelectionRollCount;
        Vector3 _previousHunterPosition;
        Vector3 _previousMonsterPosition;

        void Awake()
        {
            bool commandLineAutomation = HasCommandLineFlag("-automation");
            _telemetryPath = CommandLineValue("-telemetry");
#if UNITY_EDITOR
            bool editorAutomation = enableInEditor && !Application.isBatchMode;
#else
            bool editorAutomation = false;
#endif
            _active = commandLineAutomation || editorAutomation || !string.IsNullOrEmpty(_telemetryPath);
            if (!_active)
            {
                enabled = false;
                return;
            }

            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            string configuredPort = CommandLineValue("-automation-port");
            if (int.TryParse(configuredPort, out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
                port = parsedPort;

            // Explicit automation launches begin paused so the first observed frame is stable.
            // Editor Play Mode remains live until POST /pause requests deterministic stepping.
            _paused = commandLineAutomation;
            if (_paused) Time.timeScale = 0f;
        }

        void Start()
        {
            if (!_active || _instance != this) return;

            BindActors();
            InitializePreviousState();
            PublishSnapshot(false);
            OpenTelemetry();

            try
            {
                _server = new AutomationHttpServer(port, HandleHttpRequest);
                _server.Start();
                Debug.Log($"Gameplay automation API listening on http://127.0.0.1:{port}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Gameplay automation API failed to start: {exception}");
                _server?.Dispose();
                _server = null;
            }
        }

        void Update()
        {
            if (!_active || _shuttingDown) return;

            while (_commands.TryDequeue(out MainThreadCommand command))
                ExecuteCommand(command);
        }

        void FixedUpdate()
        {
            if (!_active) return;

            _simulationFrame++;
            DetectStateChanges();

            MainThreadCommand completedStep = null;
            if (_pendingStepFrames > 0)
            {
                _pendingStepFrames--;
                if (_pendingStepFrames == 0)
                {
                    _paused = true;
                    Time.timeScale = 0f;
                    completedStep = _pendingStepCommand;
                    _pendingStepCommand = null;
                }
            }

            PublishSnapshot(true);
            completedStep?.Complete(200, _latestStateJson);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_active || _instance != this) return;

            BindActors();
            _simulationFrame = 0;
            _pendingStepFrames = 0;
            _pendingStepCommand = null;

            AutomationResetRequest reset = _pendingReset;
            if (reset != null)
            {
                if (brain != null)
                {
                    brain.deterministicSelection = true;
                    brain.selectionSeed = reset.seed;
                }
                if (hunterController != null)
                    hunterController.SetAutomationInput(HunterAutomationInput.Released);
                if (reset.setPositions) ApplyActorPositions(reset);
                _paused = reset.paused;
                Time.timeScale = _paused ? 0f : 1f;
            }

            _frameEvents.Clear();
            InitializePreviousState();
            AddEvent("encounter_reset", "system", string.Empty, string.Empty,
                reset != null ? $"seed={reset.seed}" : scene.name, 0f);
            PublishSnapshot(true);

            MainThreadCommand command = _pendingResetCommand;
            _pendingResetCommand = null;
            _pendingReset = null;
            command?.Complete(200, _latestStateJson);
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            Shutdown();
            _instance = null;
        }

        void OnApplicationQuit()
        {
            if (_instance == this) Shutdown();
        }

        void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _server?.Dispose();
            _server = null;

            while (_commands.TryDequeue(out MainThreadCommand command))
                command.Complete(503, ErrorJson("Game is shutting down"));
            _pendingStepCommand?.Complete(503, ErrorJson("Game is shutting down"));
            _pendingResetCommand?.Complete(503, ErrorJson("Game is shutting down"));
            _pendingStepCommand = null;
            _pendingResetCommand = null;

            try
            {
                _telemetry?.Flush();
                _telemetry?.Dispose();
            }
            catch (IOException) { }
            _telemetry = null;
        }

        void BindActors()
        {
            hunterController = FindAnyObjectByType<HunterController>();
            hunterHealth = hunterController != null
                ? hunterController.GetComponent<HunterHealth>()
                : FindAnyObjectByType<HunterHealth>();
            brain = FindAnyObjectByType<VelkhanaBrain>();
        }

        AutomationHttpServer.Response HandleHttpRequest(AutomationHttpServer.Request request)
        {
            if (request.method == "OPTIONS")
                return AutomationHttpServer.Response.Json(200, "{\"ok\":true}");

            if (request.method == "GET")
            {
                switch (request.path)
                {
                    case "/health":
                        return AutomationHttpServer.Response.Json(200,
                            $"{{\"ok\":true,\"schemaVersion\":1,\"port\":{port},\"ready\":" +
                            (_latestStateJson != null ? "true" : "false") + "}");
                    case "/state":
                        return _latestStateJson != null
                            ? AutomationHttpServer.Response.Json(200, _latestStateJson)
                            : AutomationHttpServer.Error(503, "State is not ready");
                    case "/events":
                        long after = 0;
                        if (request.query.TryGetValue("after", out string afterText))
                            long.TryParse(afterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out after);
                        return AutomationHttpServer.Response.Json(200, BuildEventsJson(after));
                    case "/schema":
                        return AutomationHttpServer.Response.Json(200, ApiSchemaJson());
                    default:
                        return AutomationHttpServer.Error(404, "Unknown endpoint");
                }
            }

            if (request.method != "POST")
                return AutomationHttpServer.Error(404, "Unknown endpoint");

            switch (request.path)
            {
                case "/input":
                case "/step":
                case "/pause":
                case "/reset":
                case "/actors":
                case "/ai":
                case "/capture":
                case "/quit":
                    return QueueMainThreadCommand(request.path, request.body);
                default:
                    return AutomationHttpServer.Error(404, "Unknown endpoint");
            }
        }

        AutomationHttpServer.Response QueueMainThreadCommand(string path, string body)
        {
            if (_shuttingDown) return AutomationHttpServer.Error(503, "Game is shutting down");

            var command = new MainThreadCommand { path = path, body = body ?? string.Empty };
            _commands.Enqueue(command);
            if (!command.completed.Wait(MainThreadCommandTimeoutMilliseconds))
                return AutomationHttpServer.Error(504, "Main-thread command timed out");

            return AutomationHttpServer.Response.Json(
                command.statusCode,
                command.response ?? ErrorJson("Command completed without a response"));
        }

        void ExecuteCommand(MainThreadCommand command)
        {
            try
            {
                switch (command.path)
                {
                    case "/input": ExecuteInput(command); break;
                    case "/step": ExecuteStep(command); break;
                    case "/pause": ExecutePause(command); break;
                    case "/reset": ExecuteReset(command); break;
                    case "/actors": ExecuteActors(command); break;
                    case "/ai": ExecuteAi(command); break;
                    case "/capture": ExecuteCapture(command); break;
                    case "/quit": ExecuteQuit(command); break;
                    default: command.Complete(404, ErrorJson("Unknown command")); break;
                }
            }
            catch (Exception exception)
            {
                command.Complete(400, ErrorJson(exception.Message));
            }
        }

        void ExecuteInput(MainThreadCommand command)
        {
            EnsureBindings();
            if (hunterController == null)
                throw new InvalidOperationException("HunterController is not available");

            HunterAutomationInput input = ParseJson(command.body, HunterAutomationInput.Released);
            ValidateInput(input);
            hunterController.SetAutomationInput(input);
            AddEvent("input", "hunter", string.Empty, string.Empty, command.body, 0f);
            PublishSnapshot(false);
            command.Complete(200, _latestStateJson);
        }

        void ExecuteStep(MainThreadCommand command)
        {
            if (!_paused || !Mathf.Approximately(Time.timeScale, 0f))
                throw new InvalidOperationException("Pause the simulation before requesting exact steps");
            if (_pendingStepCommand != null)
                throw new InvalidOperationException("A step command is already running");

            AutomationStepRequest request = ParseJson(command.body, new AutomationStepRequest());
            if (request.frames < 1 || request.frames > 36000)
                throw new ArgumentOutOfRangeException(nameof(request.frames),
                    "frames must be between 1 and 36000");

            _pendingStepFrames = request.frames;
            _pendingStepCommand = command;
            _paused = false;
            Time.timeScale = 1f;
            AddEvent("step_begin", "system", string.Empty, string.Empty,
                $"frames={request.frames}", request.frames);
            // Completion happens after the requested final FixedUpdate.
        }

        void ExecutePause(MainThreadCommand command)
        {
            if (_pendingStepCommand != null)
                throw new InvalidOperationException("Cannot change pause state during an exact step");

            AutomationPauseRequest request = ParseJson(command.body, new AutomationPauseRequest());
            _paused = request.paused;
            Time.timeScale = _paused ? 0f : 1f;
            AddEvent(_paused ? "paused" : "resumed", "system",
                string.Empty, string.Empty, string.Empty, 0f);
            PublishSnapshot(false);
            command.Complete(200, _latestStateJson);
        }

        void ExecuteReset(MainThreadCommand command)
        {
            if (_pendingResetCommand != null)
                throw new InvalidOperationException("An encounter reset is already running");
            if (_pendingStepCommand != null)
            {
                _pendingStepCommand.Complete(409, ErrorJson("Exact step cancelled by reset"));
                _pendingStepCommand = null;
                _pendingStepFrames = 0;
            }

            AutomationResetRequest request = ParseJson(command.body, new AutomationResetRequest());
            _paused = true;
            Time.timeScale = 0f;
            _pendingReset = request;
            _pendingResetCommand = command;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.buildIndex >= 0)
                SceneManager.LoadScene(activeScene.buildIndex, LoadSceneMode.Single);
            else if (!string.IsNullOrEmpty(activeScene.name))
                SceneManager.LoadScene(activeScene.name, LoadSceneMode.Single);
            else
                throw new InvalidOperationException("The active scene cannot be reloaded");
            // Completion happens from OnSceneLoaded after fresh actors are bound.
        }

        void ExecuteActors(MainThreadCommand command)
        {
            EnsureBindings();
            AutomationActorCommand request = ParseJson(command.body, new AutomationActorCommand());
            if (!request.setHunter && !request.setMonster)
                throw new InvalidOperationException("Set setHunter and/or setMonster to true");

            if (request.setHunter)
            {
                if (hunterController == null) throw new InvalidOperationException("Hunter is unavailable");
                SetHunterTransform(
                    new Vector3(request.hunterX, request.hunterY, request.hunterZ),
                    request.hunterYaw);
            }
            if (request.setMonster)
            {
                if (brain == null) throw new InvalidOperationException("Monster is unavailable");
                brain.transform.SetPositionAndRotation(
                    new Vector3(request.monsterX, request.monsterY, request.monsterZ),
                    Quaternion.Euler(0f, request.monsterYaw, 0f));
            }

            Physics.SyncTransforms();
            AddEvent("teleport", "system", string.Empty, string.Empty, command.body, 0f);
            InitializePreviousPositions();
            PublishSnapshot(false);
            command.Complete(200, _latestStateJson);
        }

        void ExecuteAi(MainThreadCommand command)
        {
            EnsureBindings();
            if (brain == null) throw new InvalidOperationException("VelkhanaBrain is unavailable");

            AutomationAiCommand request = ParseJson(command.body, new AutomationAiCommand());
            brain.deterministicSelection = request.deterministic;
            brain.selectionSeed = request.seed;
            brain.enabled = request.enabled;
            AddEvent("ai_configuration", "monster", string.Empty,
                request.enabled ? "enabled" : "disabled",
                $"deterministic={request.deterministic};seed={request.seed}", 0f);
            PublishSnapshot(false);
            command.Complete(200, _latestStateJson);
        }

        void ExecuteCapture(MainThreadCommand command)
        {
            AutomationCaptureRequest request = ParseJson(command.body, new AutomationCaptureRequest());
            string path = AutomationCapture.CaptureMainCamera(request.path);
            AddEvent("capture", "system", string.Empty, string.Empty, path, 0f);
            PublishSnapshot(false);
            command.Complete(200, JsonUtility.ToJson(new AutomationOkResponse
            {
                message = "capture written",
                path = path,
                simulationFrame = _simulationFrame,
            }));
        }

        void ExecuteQuit(MainThreadCommand command)
        {
            command.Complete(200, JsonUtility.ToJson(new AutomationOkResponse
            {
                message = "quitting",
                simulationFrame = _simulationFrame,
            }));
            StartCoroutine(QuitAfterResponse());
        }

        static IEnumerator QuitAfterResponse()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            Application.Quit();
        }

        void EnsureBindings()
        {
            if (hunterController == null || hunterHealth == null || brain == null) BindActors();
        }

        void ApplyActorPositions(AutomationResetRequest request)
        {
            if (hunterController != null)
                SetHunterTransform(
                    new Vector3(request.hunterX, request.hunterY, request.hunterZ),
                    request.hunterYaw);
            if (brain != null)
                brain.transform.SetPositionAndRotation(
                    new Vector3(request.monsterX, request.monsterY, request.monsterZ),
                    Quaternion.Euler(0f, request.monsterYaw, 0f));
            Physics.SyncTransforms();
        }

        void SetHunterTransform(Vector3 position, float yaw)
        {
            CharacterController controller = hunterController.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            hunterController.transform.SetPositionAndRotation(
                position, Quaternion.Euler(0f, yaw, 0f));
            if (controller != null) controller.enabled = wasEnabled;
        }

        void ValidateInput(HunterAutomationInput input)
        {
            if (!IsFinite(input.moveX) || !IsFinite(input.moveY) ||
                !IsFinite(input.aimX) || !IsFinite(input.aimY))
                throw new InvalidDataException("Input axes must be finite numbers");
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static T ParseJson<T>(string json, T defaults)
        {
            if (string.IsNullOrWhiteSpace(json)) return defaults;
            if (typeof(T).IsValueType) return JsonUtility.FromJson<T>(json);
            JsonUtility.FromJsonOverwrite(json, defaults);
            return defaults;
        }

        void InitializePreviousState()
        {
            _previousParts.Clear();
            _previousStateReady = false;
            InitializePreviousPositions();
            CapturePreviousState();
            _previousStateReady = true;
        }

        void InitializePreviousPositions()
        {
            _previousHunterPosition = hunterController != null
                ? hunterController.transform.position
                : Vector3.zero;
            _previousMonsterPosition = brain != null ? brain.transform.position : Vector3.zero;
        }

        void CapturePreviousState()
        {
            if (hunterController != null)
            {
                _previousHunterState = hunterController.CurrentState.ToString();
                _previousHunterNode = hunterController.CurrentNode.ToString();
                _previousHunterAttack = AttackName(hunterController.CurrentAttack);
            }
            _previousHunterHealth = hunterHealth != null ? hunterHealth.Current : 0f;

            if (brain != null)
            {
                _previousMonsterState = brain.CurrentState.ToString();
                _previousMonsterAttack = AttackName(brain.CurrentAttack);
                _previousThkTrace = brain.CurrentThkTrace ?? string.Empty;
                _previousMonsterHealth = brain.CurrentHealth;
                _previousArmorStage = brain.stage;
                _previousEnraged = brain.enraged;
                _previousSelectionRollCount = brain.SelectionRollCount;

                BodyPartHurtbox[] parts = SortedParts();
                for (int i = 0; i < parts.Length; i++)
                {
                    BodyPartHurtbox part = parts[i];
                    _previousParts[part.name] = new PartObservation
                    {
                        damage = part.AccumulatedDamage,
                        armor = part.iceArmorHealth,
                        broken = part.IsBroken,
                    };
                }
            }
        }

        void DetectStateChanges()
        {
            if (!_previousStateReady)
            {
                InitializePreviousState();
                return;
            }

            if (hunterController != null)
            {
                string state = hunterController.CurrentState.ToString();
                if (state != _previousHunterState)
                    AddEvent("state", "hunter", _previousHunterState, state, string.Empty, 0f);

                string node = hunterController.CurrentNode.ToString();
                if (node != _previousHunterNode)
                    AddEvent("wp00_node", "hunter", _previousHunterNode, node,
                        $"ActionNo={HunterController.ActionNumberFor(hunterController.CurrentNode)}", 0f);

                string attack = AttackName(hunterController.CurrentAttack);
                if (attack != _previousHunterAttack)
                    AddEvent("action", "hunter", _previousHunterAttack, attack, node, 0f);
            }

            if (hunterHealth != null && !Mathf.Approximately(hunterHealth.Current, _previousHunterHealth))
                AddEvent("health", "hunter", FormatFloat(_previousHunterHealth),
                    FormatFloat(hunterHealth.Current), string.Empty,
                    hunterHealth.Current - _previousHunterHealth);

            if (brain != null)
            {
                string state = brain.CurrentState.ToString();
                if (state != _previousMonsterState)
                    AddEvent("state", "monster", _previousMonsterState, state,
                        brain.CurrentThkTrace, 0f);

                string attack = AttackName(brain.CurrentAttack);
                if (attack != _previousMonsterAttack)
                    AddEvent("action", "monster", _previousMonsterAttack, attack,
                        brain.CurrentThkNode, 0f);

                string trace = brain.CurrentThkTrace ?? string.Empty;
                if (trace != _previousThkTrace)
                    AddEvent("thk_trace", "monster", _previousThkTrace, trace,
                        brain.CurrentThkNode, 0f);

                if (!Mathf.Approximately(brain.CurrentHealth, _previousMonsterHealth))
                    AddEvent("health", "monster", FormatFloat(_previousMonsterHealth),
                        FormatFloat(brain.CurrentHealth), string.Empty,
                        brain.CurrentHealth - _previousMonsterHealth);
                if (brain.stage != _previousArmorStage)
                    AddEvent("armor_stage", "monster", _previousArmorStage.ToString(),
                        brain.stage.ToString(), string.Empty, 0f);
                if (brain.enraged != _previousEnraged)
                    AddEvent("enrage", "monster", _previousEnraged.ToString(),
                        brain.enraged.ToString(), string.Empty, 0f);
                if (brain.SelectionRollCount != _previousSelectionRollCount)
                    AddEvent("selection_roll", "monster",
                        _previousSelectionRollCount.ToString(CultureInfo.InvariantCulture),
                        brain.SelectionRollCount.ToString(CultureInfo.InvariantCulture),
                        brain.CurrentThkTrace,
                        brain.SelectionRollCount - _previousSelectionRollCount);

                DetectPartChanges();
            }

            CapturePreviousState();
        }

        void DetectPartChanges()
        {
            BodyPartHurtbox[] parts = SortedParts();
            for (int i = 0; i < parts.Length; i++)
            {
                BodyPartHurtbox part = parts[i];
                if (!_previousParts.TryGetValue(part.name, out PartObservation previous))
                {
                    _previousParts[part.name] = new PartObservation
                    {
                        damage = part.AccumulatedDamage,
                        armor = part.iceArmorHealth,
                        broken = part.IsBroken,
                    };
                    continue;
                }

                float damageDelta = part.AccumulatedDamage - previous.damage;
                float armorDelta = part.iceArmorHealth - previous.armor;
                if (damageDelta > 0.0001f)
                    AddEvent("part_damage", "monster", FormatFloat(previous.damage),
                        FormatFloat(part.AccumulatedDamage), part.name, damageDelta);
                if (armorDelta < -0.0001f)
                    AddEvent("ice_armor_damage", "monster", FormatFloat(previous.armor),
                        FormatFloat(part.iceArmorHealth), part.name, -armorDelta);
                if (part.IsBroken && !previous.broken)
                    AddEvent("part_break", "monster", "false", "true", part.name, 0f);
            }
        }

        void PublishSnapshot(bool writeTelemetry)
        {
            AutomationStateSnapshot snapshot = BuildSnapshot();
            string json = JsonUtility.ToJson(snapshot);
            _latestStateJson = json;
            _frameEvents.Clear();

            if (!writeTelemetry || _telemetry == null) return;
            try
            {
                _telemetry.WriteLine(json);
                if (_simulationFrame % 60 == 0) _telemetry.Flush();
            }
            catch (IOException exception)
            {
                Debug.LogError($"Automation telemetry write failed: {exception.Message}");
                _telemetry.Dispose();
                _telemetry = null;
            }
        }

        AutomationStateSnapshot BuildSnapshot()
        {
            EnsureBindings();
            var snapshot = new AutomationStateSnapshot
            {
                simulationFrame = _simulationFrame,
                paused = _paused,
                pendingStepFrames = _pendingStepFrames,
                fixedDeltaTime = Time.fixedDeltaTime,
                events = _frameEvents.ToArray(),
            };

            if (hunterController != null)
            {
                Vector3 position = hunterController.transform.position;
                CharacterController characterController =
                    hunterController.GetComponent<CharacterController>();
                Vector3 velocity = characterController != null
                    ? characterController.velocity
                    : (position - _previousHunterPosition) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
                snapshot.hunter = new AutomationHunterState
                {
                    actor = ActorState(hunterController.transform, velocity),
                    health = hunterHealth != null ? hunterHealth.Current : 0f,
                    maxHealth = hunterHealth != null ? hunterHealth.maxHealth : 0f,
                    dead = hunterHealth != null && hunterHealth.IsDead,
                    state = hunterController.CurrentState.ToString(),
                    stateFrame = hunterController.StateFrame,
                    wp00Node = hunterController.CurrentNode.ToString(),
                    actionNumber = HunterController.ActionNumberFor(hunterController.CurrentNode),
                    bufferedNode = hunterController.BufferedNode.ToString(),
                    weaponDrawn = hunterController.WeaponDrawn,
                    weaponTransitioning = hunterController.IsWeaponTransitioning,
                    chargeStage = hunterController.CurrentChargeStage.ToString(),
                    chargeLevel = hunterController.ChargeLevel,
                    chargeFrames = hunterController.ChargeFrames,
                    running = hunterController.IsRunning,
                    guarding = hunterController.IsGuarding,
                    invulnerable = hunterController.IsInvulnerable,
                    hyperArmor = hunterController.HasHyperArmor,
                    launched = hunterController.IsLaunched,
                    knockedDown = hunterController.IsKnockedDown,
                    automationInputEnabled = hunterController.IsAutomationInputEnabled,
                    input = hunterController.AutomationInput,
                    attack = AttackState(
                        hunterController.CurrentAttack,
                        hunterController.AttackFrame,
                        hunterController.LastSimulatedAttackFrame),
                };
                _previousHunterPosition = position;
            }

            if (brain != null)
            {
                Vector3 position = brain.transform.position;
                Vector3 velocity = (position - _previousMonsterPosition) /
                                   Mathf.Max(0.0001f, Time.fixedDeltaTime);
                snapshot.monster = new AutomationMonsterState
                {
                    actor = ActorState(brain.transform, velocity),
                    health = brain.CurrentHealth,
                    maxHealth = brain.maxHealth,
                    healthFraction = brain.HealthFraction,
                    state = brain.CurrentState.ToString(),
                    stateFrame = brain.StateFrame,
                    context = brain.CurrentContext.ToString(),
                    combatMode = brain.CombatMode.ToString(),
                    armorStage = brain.stage.ToString(),
                    enraged = brain.enraged,
                    rageBuild = brain.RageBuild,
                    airborne = brain.IsAirborne,
                    toppled = brain.IsToppled,
                    toppleCause = brain.CurrentToppleCause.ToString(),
                    toppleFramesRemaining = brain.ToppleFramesRemaining,
                    desiredBand = brain.DesiredBand.ToString(),
                    desiredDistance = brain.DesiredDistance,
                    pacingReposition = brain.IsPacingReposition,
                    sequenceStep = brain.SequenceStep,
                    sequenceLength = brain.SequenceLength,
                    selectionRollCount = brain.SelectionRollCount,
                    thkNode = brain.CurrentThkNode,
                    thkTrace = brain.CurrentThkTrace,
                    aiEnabled = brain.enabled,
                    selectionSeed = brain.selectionSeed,
                    attack = AttackState(brain.CurrentAttack, brain.AttackFrame,
                        brain.LastSimulatedAttackFrame),
                    parts = PartStates(),
                };
                _previousMonsterPosition = position;
            }

            if (hunterController != null && brain != null)
            {
                Vector3 delta = hunterController.transform.position - brain.transform.position;
                Vector3 flat = delta;
                flat.y = 0f;
                snapshot.relative = new AutomationRelativeState
                {
                    horizontalDistance = flat.magnitude,
                    verticalDistance = Mathf.Abs(delta.y),
                    distance3d = delta.magnitude,
                    monsterFacingAngle = VelkhanaBrain.AbsoluteFacingAngle(
                        brain.transform.forward, flat),
                };
            }

            return snapshot;
        }

        AutomationBodyPartState[] PartStates()
        {
            BodyPartHurtbox[] parts = SortedParts();
            var result = new AutomationBodyPartState[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                BodyPartHurtbox part = parts[i];
                result[i] = new AutomationBodyPartState
                {
                    name = part.name,
                    part = part.part.ToString(),
                    accumulatedDamage = part.AccumulatedDamage,
                    accumulatedStagger = brain.GetAccumulatedStagger(part.part),
                    breakThreshold = part.breakThreshold,
                    broken = part.IsBroken,
                    iceArmorHealth = part.iceArmorHealth,
                    hasIceArmor = part.HasIceArmor,
                };
            }
            return result;
        }

        BodyPartHurtbox[] SortedParts()
        {
            if (brain == null) return Array.Empty<BodyPartHurtbox>();
            BodyPartHurtbox[] parts = brain.GetComponentsInChildren<BodyPartHurtbox>(true);
            Array.Sort(parts, (a, b) => string.CompareOrdinal(a.name, b.name));
            return parts;
        }

        static AutomationActorState ActorState(Transform actor, Vector3 velocity)
        {
            return new AutomationActorState
            {
                name = actor.name,
                position = Vector(actor.position),
                rotationEuler = Vector(actor.eulerAngles),
                forward = Vector(actor.forward),
                velocity = Vector(velocity),
            };
        }

        static AutomationAttackState AttackState(
            AttackDefinition attack,
            int currentFrame,
            int lastSimulatedFrame)
        {
            if (attack == null) return null;
            int displayFrame = lastSimulatedFrame >= 0 ? lastSimulatedFrame : currentFrame;
            return new AutomationAttackState
            {
                name = attack.name,
                phase = AttackPhase(attack, displayFrame),
                frame = currentFrame,
                lastSimulatedFrame = lastSimulatedFrame,
                totalFrames = attack.TotalFrames,
                startupFrames = attack.startupFrames,
                activeFrames = attack.activeFrames,
                recoveryFrames = attack.recoveryFrames,
                hitboxActive = attack.IsHitActive(displayFrame),
            };
        }

        static string AttackPhase(AttackDefinition attack, int frame)
        {
            if (frame < attack.startupFrames) return "Startup";
            if (frame < attack.startupFrames + attack.activeFrames) return "Active";
            if (frame < attack.TotalFrames) return "Recovery";
            return "Complete";
        }

        static AutomationVector3 Vector(Vector3 value)
        {
            return new AutomationVector3 { x = value.x, y = value.y, z = value.z };
        }

        void AddEvent(
            string type,
            string actor,
            string from,
            string to,
            string detail,
            float value)
        {
            var automationEvent = new AutomationEvent
            {
                sequence = ++_eventSequence,
                simulationFrame = _simulationFrame,
                type = type ?? string.Empty,
                actor = actor ?? string.Empty,
                from = from ?? string.Empty,
                to = to ?? string.Empty,
                detail = detail ?? string.Empty,
                value = value,
            };
            string json = JsonUtility.ToJson(automationEvent);
            _frameEvents.Add(automationEvent);

            lock (_eventLock)
            {
                _eventRing.Add(new EventJson { sequence = automationEvent.sequence, json = json });
                if (_eventRing.Count > EventCapacity)
                    _eventRing.RemoveRange(0, _eventRing.Count - EventCapacity);
            }
        }

        string BuildEventsJson(long after)
        {
            var builder = new StringBuilder(256);
            lock (_eventLock)
            {
                long oldest = _eventRing.Count > 0 ? _eventRing[0].sequence : _eventSequence + 1;
                builder.Append("{\"latestSequence\":")
                    .Append(_eventSequence)
                    .Append(",\"oldestSequence\":")
                    .Append(oldest)
                    .Append(",\"events\":[");
                bool first = true;
                for (int i = 0; i < _eventRing.Count; i++)
                {
                    if (_eventRing[i].sequence <= after) continue;
                    if (!first) builder.Append(',');
                    builder.Append(_eventRing[i].json);
                    first = false;
                }
                builder.Append("]}");
            }
            return builder.ToString();
        }

        void OpenTelemetry()
        {
            if (string.IsNullOrEmpty(_telemetryPath)) return;
            try
            {
                string path = Path.GetFullPath(_telemetryPath);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                _telemetry = new StreamWriter(path, false, new UTF8Encoding(false));
                _telemetryPath = path;
                Debug.Log($"Automation telemetry writing to {path}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not open automation telemetry: {exception.Message}");
                _telemetry = null;
            }
        }

        static string ApiSchemaJson()
        {
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"endpoints\":{" +
                   "\"GET /health\":\"readiness\"," +
                   "\"GET /state\":\"complete current combat snapshot\"," +
                   "\"GET /events?after=N\":\"transition and damage event ring\"," +
                   "\"GET /schema\":\"this document\"," +
                   "\"POST /input\":\"HunterAutomationInput held state\"," +
                   "\"POST /pause\":\"{paused:bool}\"," +
                   "\"POST /step\":\"{frames:int}; requires paused simulation\"," +
                   "\"POST /reset\":\"reload encounter with seed and optional positions\"," +
                   "\"POST /actors\":\"teleport hunter and/or monster\"," +
                   "\"POST /ai\":\"configure enabled, deterministic and seed\"," +
                   "\"POST /capture\":\"{path:string}\"," +
                   "\"POST /quit\":\"gracefully stop a standalone player\"" +
                   "}}";
        }

        static string ErrorJson(string message)
        {
            return JsonUtility.ToJson(new AutomationErrorResponse
            {
                ok = false,
                error = message ?? "Unknown error",
            });
        }

        static string AttackName(AttackDefinition attack) => attack != null ? attack.name : string.Empty;
        static string FormatFloat(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        static bool HasCommandLineFlag(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string CommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
