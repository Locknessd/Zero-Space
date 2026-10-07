using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>
    /// Plays individually directed camera takes after the demo or battle evaluates both characters.
    /// The library owns shot design; this component only interpolates, blends and protects framing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(13500)]
    public sealed partial class FrankCinematicCamera : MonoBehaviour
    {
        [Header("Demo")]
        public FrankCombinationTester tester;
        [Header("Battle")]
        public GameManager battle;
        [Min(1f)] public float battleIdleDistance = 5f;
        [Range(.65f, 1.2f)] public float battleDistanceScale = 1f;
        public bool keepBattleCameraInFront = true;
        [Header("Battle readability")]
        [Tooltip("Keep battle takes near the fighting plane; demo takes retain authored angles.")]
        public bool readableBattleFraming = true;
        [Range(4f, 25f)] public float battleMaximumPitch = 12f;
        [Range(0f, 25f)] public float battleMaximumYaw = 12f;
        [Tooltip("Maximum authored radius in world units before scene scaling. Visibility fitting can expand for launches and separation.")]
        [Range(4f, 10f)] public float battleMaximumAuthoredDistance = 6.8f;
        [Range(0f, 1f)] public float battleActionCenterBlend = .75f;
        [Tooltip("Shot entry and return blend in presentation seconds.")]
        [Range(.15f, .65f)] public float battleTransitionSeconds = .36f;
        [Header("Cinematography")]
        public FrankCameraLibrary library;
        public bool cinematic = true;
        [Range(.05f, .15f)] public float viewportMargin = .07f;
        [Min(.1f)] public float transitionDuration = .65f;
        [Min(.03f)] public float minimumHeight = .3f;
        public float Zoom { get; set; } = 1f;
        public FrankCameraLibrary.Shot ActiveShot { get; private set; }
        public float SafetyDolly { get; private set; }
        public IReadOnlyList<Vector3> LastFramingPoints => framingPoints;

        readonly List<Vector3> framingPoints = new List<Vector3>(128);
        readonly List<FramingRenderer> framingRenderers = new List<FramingRenderer>();
        readonly HashSet<Renderer> uniqueRenderers = new HashSet<Renderer>();
        FrankTestDriver cachedMankeyDriver, cachedPepeDriver;
        FrankTestActor cachedMankey, cachedPepe;
        FrankCameraLibrary cachedLibrary;
        string currentKey;
        int cachedMode = -1, cachedMotion = -1;
        bool cachedPepeAttacks;
        bool initialized, cacheReady;
        float previousTime, transitionTime, activeTransitionDuration;
        bool transitioning;
        FrankCameraLibrary.Frame displayed, transitionFrom;
        Camera battleCamera;
        FrankBattlePairPlayback battlePlayback;
        int battlePlaybackId = -1;
        Vector3 battleOrigin;
        Quaternion battleRotation;
        CharacterCombat cachedBattleLeft, cachedBattleRight;
        FrankTestActor cachedBattleAttack, cachedBattleReaction;

        sealed class FramingRenderer
        {
            public Renderer renderer;
            public Mesh baked;
        }

        public static string CurrentKey(FrankCombinationTester owner)
        {
            if (!owner) return null;
            string role = owner.pepeAttacks ? "/pepe" : "/mankey";
            if (owner.greatSword) return "execution/" + owner.greatSwordMotion + role;
            if (owner.gunSword) return "combo/" + owner.comboMotion + role;
            if (owner.unarmed) return "vol10/" + owner.unarmedMotion + role;
            return "frank/" + owner.motion + role;
        }

        /// <summary>Returns false when the caller should use its manual fallback view.</summary>
        public bool Apply(float deltaTime = 0, bool immediate = false)
        {
            if (battle) return ApplyBattle(deltaTime, immediate);
            if (!cinematic || !tester || !tester.demoCamera || !library) return false;
            int mode = tester.greatSword ? 3 : tester.gunSword ? 2 : tester.unarmed ? 1 : 0;
            int motion = mode == 3 ? tester.greatSwordMotion : mode == 2 ? tester.comboMotion : mode == 1 ? tester.unarmedMotion : tester.motion;
            bool changed = mode != cachedMode || motion != cachedMotion || tester.pepeAttacks != cachedPepeAttacks || library != cachedLibrary;
            string key = changed || ActiveShot == null ? CurrentKey(tester) : currentKey;
            if (changed || ActiveShot == null)
            {
                ActiveShot = library.Find(key);
                cachedLibrary = library;
            }
            if (ActiveShot == null || ActiveShot.frames == null || ActiveShot.frames.Length == 0)
                return false;

            cachedMode = mode;
            cachedMotion = motion;
            cachedPepeAttacks = tester.pepeAttacks;
            return ApplyFrame(tester.demoCamera, Sample(ActiveShot, tester.time), key, tester.time,
                changed, deltaTime, immediate, true);
        }

        public static string BattleKey(CombatTripletData move)
        {
            if (move == null || move.sourcePair == null) return null;
            string role = move.sourcePair.pepeAttacks ? "/pepe" : "/mankey";
            if (move.sourcePair.unarmedIndex >= 0) return "vol10/" + move.sourcePair.unarmedIndex + role;
            int index;
            switch (move.weapon)
            {
                case TrumpWeaponManager.WeaponType.TwoHandedAxe: index = 0; break;
                case TrumpWeaponManager.WeaponType.Assassin: index = 1; break;
                case TrumpWeaponManager.WeaponType.DualDaggers: index = 2; break;
                case TrumpWeaponManager.WeaponType.GreatSword: index = 3; break;
                case TrumpWeaponManager.WeaponType.Katana: index = 4; break;
                case TrumpWeaponManager.WeaponType.Spear: index = 5; break;
                case TrumpWeaponManager.WeaponType.WarriorShield: index = 6; break;
                default: return null;
            }
            return "frank/" + index + role;
        }

        bool ApplyBattle(float deltaTime, bool immediate)
        {
            if (!cinematic || !library || !battle.leftCombat || !battle.rightCombat) return false;
            if (!battleCamera) battleCamera = GetComponent<Camera>();
            if (!battleCamera) return false;
            var playback = battle.leftCombat.SourcePlayback;
            if (!playback || !playback.Playing) playback = battle.rightCombat.SourcePlayback;
            bool playing = playback && playback.Playing && playback.AttackerActor;
            string key = playing ? BattleKey(playback.Move) : null;
            bool newSequence = playing && (playback != battlePlayback ||
                playback.PlaybackId != battlePlaybackId);
            if (newSequence)
            {
                battlePlayback = playback;
                battlePlaybackId = playback.PlaybackId;
                // Library takes are authored around an attacker at the origin facing +Z.
                // Capture the battle pair's placement once, before root-motion recovery moves it.
                battleOrigin = playback.AttackerActor.transform.position;
                battleRotation = playback.AttackerActor.transform.rotation;
            }
            if (!playing) { battlePlayback = null; battlePlaybackId = -1; }
            CollectFramingPoints(framingPoints);
            Vector3 actionCenter = (battle.leftCombat.transform.position + battle.rightCombat.transform.position) * .5f + Vector3.up;
            if (framingPoints.Count > 0)
            {
                var bounds = new Bounds(framingPoints[0], Vector3.zero);
                foreach (var point in framingPoints) bounds.Encapsulate(point);
                actionCenter = bounds.center;
            }
            ActiveShot = key == null ? null : library.Find(key);
            FrankCameraLibrary.Frame frame;
            float time = 0;
            if (ActiveShot != null && ActiveShot.frames != null && ActiveShot.frames.Length > 0)
            {
                time = playback.SampleTime;
                frame = Sample(ActiveShot, time);
                Vector3 offset = battleRotation * (frame.position - frame.focus);
                frame.focus = battleOrigin + battleRotation * frame.focus;
                // Battle's backdrop is behind the X lane. Reflect the viewing side,
                // keeping the authored elevation, distance and lens for either attacker.
                if (keepBattleCameraInFront) offset.z = Mathf.Abs(offset.z);
                if (readableBattleFraming)
                {
                    float radius = Mathf.Min(offset.magnitude, battleMaximumAuthoredDistance);
                    float yaw = Mathf.Clamp(Mathf.Atan2(offset.x, Mathf.Abs(offset.z)) * Mathf.Rad2Deg,
                        -battleMaximumYaw, battleMaximumYaw) * Mathf.Deg2Rad;
                    float pitch = Mathf.Clamp(Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg,
                        4f, battleMaximumPitch) * Mathf.Deg2Rad;
                    float side = offset.z < 0 ? -1 : 1;
                    offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                        side * Mathf.Cos(yaw) * Mathf.Cos(pitch)) * radius;
                    frame.focus = Vector3.Lerp(frame.focus, actionCenter, battleActionCenterBlend);
                }
                frame.position = frame.focus + offset;
            }
            else
            {
                key = "battle/idle";
                Vector3 focus = actionCenter;
                frame = new FrankCameraLibrary.Frame { focus = focus, position = focus + new Vector3(0, .65f, battleIdleDistance), fov = 42 };
            }
            // Dolly along the existing viewing ray: preserve each take's angle and lens.
            // ApplyFrame still fits the actual bodies and weapons when a take needs more room.
            frame.position = frame.focus + (frame.position - frame.focus) * battleDistanceScale;
            return ApplyFrame(battleCamera, frame, key, time, newSequence || key != currentKey,
                deltaTime, immediate, false, true);
        }

        void LateUpdate()
        {
            if (battle) Apply(Time.timeScale <= 0 ? 0 : Time.unscaledDeltaTime);
        }

        bool ApplyFrame(Camera camera, FrankCameraLibrary.Frame frame, string key, float time,
            bool changed, float deltaTime, bool immediate, bool relativeTransition, bool framingReady = false)
        {
            camera.aspect = Mathf.Max(.05f, (float)camera.pixelWidth / Mathf.Max(1, camera.pixelHeight));
            // Preserve the baked motion envelope on narrow windows instead of chasing
            // each changing silhouette with a reactive portrait dolly.
            float aspectScale = Mathf.Max(1, 1.30f / camera.aspect);
            frame.position = frame.focus + (frame.position - frame.focus) * Mathf.Clamp(Zoom, .65f, 3f) * aspectScale;
            bool wrapped = initialized && !changed && time + .001f < previousTime;
            if (initialized && (changed || wrapped) && !immediate)
            {
                transitionFrom = displayed;
                // Actor selections and loop restarts reset the source motion. Blend
                // viewing angle and lens relative to the incoming action center.
                if (changed && relativeTransition)
                {
                    transitionFrom.position += frame.focus - transitionFrom.focus;
                    transitionFrom.focus = frame.focus;
                }
                float angle = Quaternion.Angle(LookRotation(transitionFrom.position, transitionFrom.focus), LookRotation(frame.position, frame.focus));
                float radiusRatio = Vector3.Distance(transitionFrom.position, transitionFrom.focus) / Mathf.Max(.1f, Vector3.Distance(frame.position, frame.focus));
                activeTransitionDuration = Mathf.Clamp(transitionDuration + angle * .009f + Mathf.Abs(Mathf.Log(radiusRatio)) * .22f + Vector3.Distance(transitionFrom.focus, frame.focus) * .045f, .65f, 1.45f);
                if (battle && readableBattleFraming)
                    activeTransitionDuration = Mathf.Clamp(battleTransitionSeconds + angle * .002f, .15f, .65f);
                transitionTime = 0;
                transitioning = true;
                SafetyDolly = 0;
            }
            if (immediate || !initialized) transitioning = false;
            currentKey = key;
            previousTime = time;

            if (transitioning)
            {
                transitionTime += Mathf.Max(0, deltaTime);
                float u = Mathf.Clamp01(transitionTime / activeTransitionDuration);
                float blend = u * u * u * (u * (u * 6 - 15) + 10);
                Quaternion orbit = Quaternion.Slerp(LookRotation(transitionFrom.position, transitionFrom.focus),
                    LookRotation(frame.position, frame.focus), blend);
                float targetRadius = Vector3.Distance(frame.position, frame.focus);
                float radius = Mathf.Lerp(Vector3.Distance(transitionFrom.position, transitionFrom.focus), targetRadius, blend);
                if (!relativeTransition) frame.focus = Vector3.Lerp(transitionFrom.focus, frame.focus, blend);
                float blendedFov = Mathf.LerpUnclamped(transitionFrom.fov, frame.fov, blend);
                // Incoming motion already contains an anticipated framing envelope. Do not
                // squeeze it inside the previous shot or lag behind its action center.
                // Keep coverage consistent while blending between different lens widths.
                float lensFit = Mathf.Max(1, Mathf.Tan(frame.fov * Mathf.Deg2Rad * .5f) / Mathf.Tan(blendedFov * Mathf.Deg2Rad * .5f));
                radius = Mathf.Max(radius, targetRadius * lensFit);
                frame.position = frame.focus - orbit * Vector3.forward * radius;
                frame.fov = blendedFov;
                if (u >= 1) transitioning = false;
            }

            camera.orthographic = false;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = Mathf.Max(200, camera.farClipPlane);
            frame.fov = Mathf.Clamp(frame.fov, 20, 75);
            frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
            if (!framingReady) CollectFramingPoints(framingPoints);
            var rotation = LookRotation(frame.position, frame.focus);
            float wanted = RequiredDolly(frame, rotation, camera.aspect, Mathf.Clamp(viewportMargin, .05f, .15f));
            float hardMinimum = RequiredDolly(frame, rotation, camera.aspect, .05f);
            if (immediate || !initialized) SafetyDolly = wanted;
            else if (deltaTime > 0)
                SafetyDolly = Mathf.Lerp(SafetyDolly, wanted, 1 - Mathf.Exp(-deltaTime / .25f));
            SafetyDolly = Mathf.Max(hardMinimum, SafetyDolly);
            frame.position -= rotation * Vector3.forward * SafetyDolly;

            // Low-angle shots must remain above the floor even after aspect/weapon fitting.
            // Refit after a height correction because it changes the view direction.
            for (int i = 0; i < 8; i++)
            {
                frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
                rotation = LookRotation(frame.position, frame.focus);
                float correction = RequiredDolly(frame, rotation, camera.aspect, .0501f);
                if (correction <= .00001f) break;
                frame.position -= rotation * Vector3.forward * (correction + .0001f);
            }
            frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
            camera.transform.SetPositionAndRotation(frame.position, LookRotation(frame.position, frame.focus));
            camera.fieldOfView = frame.fov;
            displayed = frame;
            initialized = true;
            return true;
        }

    }
}
