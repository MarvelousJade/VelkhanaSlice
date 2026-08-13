using System;
using VelkhanaSlice.Hunter;

namespace VelkhanaSlice.Automation
{
    [Serializable]
    public sealed class AutomationVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class AutomationActorState
    {
        public string name;
        public AutomationVector3 position;
        public AutomationVector3 rotationEuler;
        public AutomationVector3 forward;
        public AutomationVector3 velocity;
    }

    [Serializable]
    public sealed class AutomationAttackState
    {
        public string name;
        public string phase;
        public int frame;
        public int lastSimulatedFrame;
        public int totalFrames;
        public int startupFrames;
        public int activeFrames;
        public int recoveryFrames;
        public bool hitboxActive;
    }

    [Serializable]
    public sealed class AutomationHunterState
    {
        public AutomationActorState actor;
        public float health;
        public float maxHealth;
        public bool dead;
        public string state;
        public int stateFrame;
        public string wp00Node;
        public int actionNumber;
        public string bufferedNode;
        public bool weaponDrawn;
        public bool weaponTransitioning;
        public string chargeStage;
        public int chargeLevel;
        public int chargeFrames;
        public bool running;
        public bool guarding;
        public bool invulnerable;
        public bool hyperArmor;
        public bool launched;
        public bool knockedDown;
        public bool automationInputEnabled;
        public HunterAutomationInput input;
        public AutomationAttackState attack;
    }

    [Serializable]
    public sealed class AutomationBodyPartState
    {
        public string name;
        public string part;
        public float accumulatedDamage;
        public float accumulatedStagger;
        public float breakThreshold;
        public bool broken;
        public float iceArmorHealth;
        public bool hasIceArmor;
    }

    [Serializable]
    public sealed class AutomationMonsterState
    {
        public AutomationActorState actor;
        public float health;
        public float maxHealth;
        public float healthFraction;
        public string state;
        public int stateFrame;
        public string context;
        public string combatMode;
        public string armorStage;
        public bool enraged;
        public float rageBuild;
        public bool airborne;
        public bool toppled;
        public string toppleCause;
        public int toppleFramesRemaining;
        public string desiredBand;
        public float desiredDistance;
        public bool pacingReposition;
        public int sequenceStep;
        public int sequenceLength;
        public int selectionRollCount;
        public string thkNode;
        public string thkTrace;
        public bool aiEnabled;
        public int selectionSeed;
        public AutomationAttackState attack;
        public AutomationBodyPartState[] parts;
    }

    [Serializable]
    public sealed class AutomationRelativeState
    {
        public float horizontalDistance;
        public float verticalDistance;
        public float distance3d;
        public float monsterFacingAngle;
    }

    [Serializable]
    public sealed class AutomationEvent
    {
        public long sequence;
        public long simulationFrame;
        public string type;
        public string actor;
        public string from;
        public string to;
        public string detail;
        public float value;
    }

    [Serializable]
    public sealed class AutomationStateSnapshot
    {
        public int schemaVersion = 1;
        public long simulationFrame;
        public bool paused;
        public int pendingStepFrames;
        public float fixedDeltaTime;
        public AutomationHunterState hunter;
        public AutomationMonsterState monster;
        public AutomationRelativeState relative;
        public AutomationEvent[] events;
    }

    [Serializable]
    public sealed class AutomationResetRequest
    {
        public int seed = 124;
        public bool paused = true;
        public bool setPositions;
        public float hunterX;
        public float hunterY = 1f;
        public float hunterZ = -5f;
        public float hunterYaw;
        public float monsterX;
        public float monsterY;
        public float monsterZ = 6f;
        public float monsterYaw = 180f;
    }

    [Serializable]
    public sealed class AutomationStepRequest
    {
        public int frames = 1;
    }

    [Serializable]
    public sealed class AutomationPauseRequest
    {
        public bool paused = true;
    }

    [Serializable]
    public sealed class AutomationActorCommand
    {
        public bool setHunter;
        public float hunterX;
        public float hunterY = 1f;
        public float hunterZ;
        public float hunterYaw;
        public bool setMonster;
        public float monsterX;
        public float monsterY;
        public float monsterZ;
        public float monsterYaw;
    }

    [Serializable]
    public sealed class AutomationAiCommand
    {
        public bool enabled = true;
        public bool deterministic = true;
        public int seed = 124;
    }

    [Serializable]
    public sealed class AutomationCaptureRequest
    {
        public string path;
    }

    [Serializable]
    internal sealed class AutomationOkResponse
    {
        public bool ok = true;
        public string message;
        public string path;
        public long simulationFrame;
    }

    [Serializable]
    internal sealed class AutomationErrorResponse
    {
        public bool ok;
        public string error;
    }
}
