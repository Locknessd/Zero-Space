using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleVfxPlayer
{
    readonly HashSet<ParticleSystem> pausedParticles = new HashSet<ParticleSystem>();

    // Local contact holds leave global time running, so particles retain their
    // unscaled presentation. A global/menu pause must also freeze native simulation.
    void Update() => SynchronizeParticlePause();

    bool SynchronizeParticlePause()
    {
        bool paused = Application.isPlaying && Time.timeScale <= 0;
        if (!paused)
        {
            foreach (var particles in pausedParticles)
                if (particles && particles.gameObject.activeInHierarchy && particles.isPaused)
                    particles.Play(false);
            pausedParticles.Clear();
            return false;
        }

        foreach (var instance in instances)
        {
            if (!instance.root || !instance.root.activeInHierarchy)
                continue;
            foreach (var particles in instance.particles)
            {
                // Resume only systems that this owner paused; retain external pauses.
                if (!particles || !particles.isPlaying)
                    continue;
                particles.Pause(false);
                pausedParticles.Add(particles);
            }
        }
        return true;
    }
}
