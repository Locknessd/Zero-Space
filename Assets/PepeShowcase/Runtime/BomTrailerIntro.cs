using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZeroSpace.Showcase
{
    public sealed partial class BomTrailerIntro : MonoBehaviour
    {
        public Font font;
        public const float TitleEnd = 4;
        public const float FreezeStart = 11.4f;
        public const float FadeStart = 15.8f;
        PepeAttackShowcase showcase;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        Vector3 anchor;
        Quaternion facing;
        bool animatorEnabled;
        bool rootMotion;
        AnimatorCullingMode cullingMode;
        Canvas showcaseCanvas;
        bool canvasEnabled;
        public bool IsActive => graph.IsValid();
        public bool Frozen { get; private set; }
        public float SampleTime { get; private set; }

        public void Begin(PepeAttackShowcase owner)
        {
            showcase = owner;
            animatorEnabled = owner.character.enabled;
            rootMotion = owner.character.applyRootMotion;
            cullingMode = owner.character.cullingMode;
            owner.character.enabled = true;
            owner.character.applyRootMotion = false;
            owner.character.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anchor = owner.character.transform.position;
            facing = owner.character.transform.rotation;
            graph = PlayableGraph.Create("BOM Pepe startup and freeze frame");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, owner.library.startupAnimation);
            playable.SetApplyFootIK(true);
            playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Pepe startup", owner.character);
            output.SetSourcePlayable(playable);
            graph.Play();
            BuildLayout(owner.director.output);
            layout.SetActive(true);
            showcaseCanvas = owner.presentation.heading.GetComponentInParent<Canvas>(true);
            canvasEnabled = showcaseCanvas.enabled;
            showcaseCanvas.enabled = false;
        }

        public void Evaluate(float seconds)
        {
            float motion = Mathf.Clamp01((seconds - TitleEnd) / (FreezeStart - TitleEnd));
            SampleTime = motion * showcase.library.startupPoseSeconds;
            Frozen = seconds >= FreezeStart && seconds < FadeStart;
            playable.SetTime(SampleTime);
            graph.Evaluate(0);
            showcase.character.transform.SetPositionAndRotation(anchor, facing);
            var hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips);
            var head = showcase.character.GetBoneTransform(HumanBodyBones.Head);
            var feet = showcase.character.GetBoneTransform(HumanBodyBones.LeftFoot);
            var other = showcase.character.GetBoneTransform(HumanBodyBones.RightFoot);
            hips.position += Vector3.up * Mathf.Max(0, .025f - Mathf.Min(feet.position.y, other.position.y));
            // Hold the complete pose and camera once the name card lands.
            float cameraProgress = Mathf.SmoothStep(0, 1, motion);
            var focus = Vector3.Lerp(hips.position, head.position, Mathf.Lerp(.15f, .7f, cameraProgress));
            float yaw = Mathf.Lerp(-28, -8, cameraProgress);
            var rotation = Quaternion.Euler(2, 180 + yaw, 0);
            float bodyHeight = Vector3.Distance(head.position, (feet.position + other.position) * .5f);
            float distance = Mathf.Lerp(bodyHeight * 3.7f, bodyHeight * 2.1f, cameraProgress);
            focus += rotation * Vector3.right * Mathf.Lerp(0, bodyHeight * .38f, cameraProgress);
            var camera = showcase.director.output;
            camera.fieldOfView = 35;
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
            DisplayLayout(seconds);
        }

        public void End()
        {
            if (graph.IsValid()) graph.Destroy();
            if (showcase && showcase.character)
            {
                showcase.character.enabled = animatorEnabled;
                showcase.character.applyRootMotion = rootMotion;
                showcase.character.cullingMode = cullingMode;
            }
            if (showcaseCanvas) showcaseCanvas.enabled = canvasEnabled;
            showcaseCanvas = null;
            if (layout) layout.SetActive(false);
            Frozen = false;
        }

        void OnDestroy() => End();
    }
}
