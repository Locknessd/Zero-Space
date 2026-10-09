using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string E10Output = "GeneratedAssets/CombatExpansion/SamuraiStudy/Execution10SupineRecovery";
        const string E10GetUp = "f83ba830485ff2b46ba1d67ec650f1e0:1827226128182048838";
        const string E10RecoveryPath =
            "Assets/CombatExpansion/Actions/Frank_GreatSword_SupineRecovery_Grounding.asset";
        const string E10Scope = "Actual surviving victim controller recovery after full original Execution10. " +
            "Saved BattleScene avatars, original source A/B clips and calibrated drivers, unarmed victim, " +
            "native 1.7m spacing and yaw, original Execution10 grounding. Exact Damage_Getup02_L_iP, " +
            "existing supine grounding, 0.12s entry blend. Independent recovery grid at minimum 361Hz. " +
            "Every mapped victim bone checked at entry; attacker has no authored recovery in this study. " +
            "Displacement is diagnostic, not a motion-quality verdict. Sheets require visual review. " +
            "No approved damaging contact, registration, audio, presentation, PlayMode or gameplay completion claim.";

        [Serializable]
        sealed class E10Report
        {
            public string status = "RUNNING; partial evidence", error, scope = E10Scope;
            public string utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion;
            public int sampleRate = 361;
            public float entryLimit = .01f, clearanceLimit = -.025f, driftLimit = .00002f;
            public bool fileGuardsPassed, sceneGuardsPassed;
            public NativeReferenceFile[] guardedFiles;
            public List<E10Case> cases = new List<E10Case>();
        }

        [Serializable]
        sealed class E10Case
        {
            public string source, target, status = "PENDING", error, sheet;
            public int direction, samples, stoppedSamples, mappedVictimBones;
            public string attack, reaction, getUp, grounding, recoveryGrounding;
            public string attackerDriver, receiverDriver, sourceAvatar, targetAvatar;
            public float sourceDuration, endpointSeconds, recoveryDuration, lastRecoverySeconds;
            public float entryJump, minimumClearance, maximumFrameDisplacement, repeatDrift, stoppedDrift;
            public bool recoveryStarted, cancellationPassed;
            public float[] sheetRecoverySeconds;
            [NonSerialized] public Bounds bounds;
        }
    }
}
