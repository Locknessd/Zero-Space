using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    // The battle Animator owns state and time; the demo driver supplies authored prop tracks
    // and the calibrated visible pose after Animator evaluation, just as in FrankTestActor.
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public sealed class FrankCombatWeaponRig : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            public TrumpWeaponManager.WeaponType weapon;
            public FrankTestDriver driver;
        }

        public Binding[] bindings = Array.Empty<Binding>();
        public FrankTestDriver ActiveDriver { get; private set; }
        public float SampleTime { get; private set; }
        float standingHipHeight;
        public Vector3 CameraGroundPosition => ActiveDriver && ActiveDriver.pose.targetHips
            ? ActiveDriver.pose.targetHips.position - Vector3.up * standingHipHeight
            : transform.position;
        Animator character;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        AnimationClip sourceClip;
        Transform[] restoredBones;
        Vector3[] restoredPositions;
        Quaternion[] restoredRotations;
        static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");

        public bool Begin(Animator target, TrumpWeaponManager.WeaponType weapon)
        {
            Release();
            if (weapon == TrumpWeaponManager.WeaponType.None) return true;
            var binding = Array.Find(bindings, b => b != null && b.weapon == weapon);
            if (binding == null || !binding.driver)
            {
                Debug.LogError($"{name}: no source weapon driver for {weapon}.", this);
                return false;
            }
            character = target;
            var hips = target.GetBoneTransform(HumanBodyBones.Hips);
            standingHipHeight = hips ? hips.position.y - target.transform.position.y : 0f;
            // Save only the visible model before adding the source skeleton. IK may adjust limb
            // lengths; restore those transforms on release so they do not leak into idle/get-up.
            restoredBones = target.GetComponentsInChildren<Transform>(true);
            restoredPositions = new Vector3[restoredBones.Length];
            restoredRotations = new Quaternion[restoredBones.Length];
            for (int i = 0; i < restoredBones.Length; i++)
            {
                restoredPositions[i] = restoredBones[i].localPosition;
                restoredRotations[i] = restoredBones[i].localRotation;
            }
            var scale = target.transform.localScale;
            bool wasEnabled = target.enabled;
            try
            {
                ActiveDriver = Instantiate(binding.driver, transform, false);
                ActiveDriver.name = "Combat source rig - " + weapon;
                var calibration = ActiveDriver.characterScale;
                // In the demo the unit-scale driver is a sibling of the scaled character. Battle
                // keeps the fighter root for positioning/camera refs, so compensate its scale here.
                ActiveDriver.transform.localScale = new Vector3(1f / calibration.x, 1f / calibration.y, 1f / calibration.z);
                ActiveDriver.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                ActiveDriver.Bind(target);
                var pose = ActiveDriver.pose;
                pose.isAttacker = true;
                pose.transferFingers = true;
                foreach (var limb in pose.limbs) limb.alignGrip = limb.sourceKnuckle && limb.sourceFingerJoint;
                foreach (var renderer in pose.originalBody) if (renderer) renderer.enabled = false;
                foreach (var renderer in pose.weaponRenderers)
                {
                    if (!renderer) continue;
                    renderer.enabled = true;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                sourceClip = pose.sourceClips[0];
                graph = PlayableGraph.Create(name + " combat weapon");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, sourceClip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0);
                var output = AnimationPlayableOutput.Create(graph, "Authored weapon tracks", pose.driver);
                output.SetSourcePlayable(playable);
                graph.Play();
                return true;
            }
            catch
            {
                Release();
                throw;
            }
            finally
            {
                target.transform.localScale = scale;
                // Bind disables the visible Animator in the demo. In battle it must keep ticking
                // so AnimationEndAction and the hit/get-up queue retain ownership of completion.
                target.enabled = wasEnabled;
            }
        }

        void LateUpdate() { Evaluate(); }

        public void Evaluate()
        {
            if (!ActiveDriver || !character || !graph.IsValid()) return;
            var state = character.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != AttackState) return;
            SampleTime = Mathf.Clamp01(state.normalizedTime) * sourceClip.length;
            playable.SetTime(SampleTime);
            graph.Evaluate(0);
            ActiveDriver.pose.ApplyPose();
        }

        public void Release()
        {
            if (graph.IsValid()) graph.Destroy();
            if (ActiveDriver)
            {
                ActiveDriver.gameObject.SetActive(false);
                var retired = ActiveDriver.gameObject;
                if (Application.isPlaying) Destroy(retired);
#if UNITY_EDITOR
                // Preview validation also receives animation callbacks. Immediate destruction
                // from inside those callbacks is prohibited; remove it on the next editor tick.
                else UnityEditor.EditorApplication.delayCall += () => { if (retired) DestroyImmediate(retired); };
#endif
            }
            ActiveDriver = null;
            if (restoredBones != null)
                for (int i = 0; i < restoredBones.Length; i++)
                    if (restoredBones[i] && restoredBones[i] != transform)
                        restoredBones[i].SetLocalPositionAndRotation(restoredPositions[i], restoredRotations[i]);
            restoredBones = null;
            restoredPositions = null;
            restoredRotations = null;
            sourceClip = null;
            SampleTime = 0;
        }

        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
