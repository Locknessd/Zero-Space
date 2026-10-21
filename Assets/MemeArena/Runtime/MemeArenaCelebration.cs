using UnityEngine;

namespace MemeArena
{
    public sealed class MemeArenaCelebration : MonoBehaviour
    {
        public bool vfxEnabled = true;
        public ParticleSystem[] confetti = System.Array.Empty<ParticleSystem>();
        public Camera view;
        public int PlayCount { get; private set; }

        public void Play()
        {
            if (!vfxEnabled) return;
            PlayCount++;
            if (!view) view = Camera.main;
            for (int i = 0; i < confetti.Length; i++)
            {
                var system = confetti[i];
                if (system)
                {
                    if (view)
                    {
                        float side = i % 2 == 0 ? -1 : 1;
                        system.transform.position = view.ViewportToWorldPoint(new Vector3(side < 0 ? .07f : .93f,
                            .18f, Mathf.Max(6, view.nearClipPlane + 5)));
                        system.transform.rotation = Quaternion.LookRotation(view.transform.up +
                            view.transform.right * side * .2f);
                    }
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    system.Play(true);
                }
            }
        }
    }
}
