using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static bool cleanupObserved;
        static double cleanupBegan, cleanupDspBegan;
        static float cleanupUnscaledBegan, particleBudget, audioBudget;
        static string cleanupState;
        static readonly List<object> cleanupEffects = new List<object>();

        static T CleanupField<T>(object owner, string name) =>
            (T)ObservationField(owner, name).GetValue(owner);

        static void SamuraiCleanupCleared()
        {
            if (samuraiSuite && cleanupObserved && activeCase)
                failure = "Presentation was forcibly cleared while observing natural expiry.";
        }

        static void ResetSamuraiCleanup()
        {
            cleanupObserved = false;
            cleanupEffects.Clear();
            particleBudget = audioBudget = 0;
        }

        // Read-only observation: never stop, clear, simulate, disable, or reset presentation here.
        static bool AwaitSamuraiCleanup()
        {
            if (!samuraiSuite)
                return true;
            if (!cleanupObserved)
            {
                cleanupObserved = true;
                cleanupBegan = Now;
                cleanupDspBegan = AudioSettings.dspTime;
                cleanupUnscaledBegan = Time.unscaledTime;
                cleanupState = SamuraiCleanupState();
                report.AppendLine($"SETTLE case={step + 1} quietWallSeconds={Now - settledAt:F3} " +
                    $"activeEffects={game.battleVfx.ActiveEffectCount} " +
                    $"activeVoices={game.battleSfx.ActiveVoiceCount} scale={Time.timeScale:F3} " +
                    $"unscaledTime={Time.unscaledTime:F3} dspTime={AudioSettings.dspTime:F3}");
                ReportSamuraiParticles();
                ReportSamuraiVoices();
                // A pool releases in LateUpdate; particles finish in the engine's particle phase.
                // Allow two maximum simulation steps for that ordering, not a longer content timeout.
                particleBudget += 2 * Time.maximumDeltaTime;
                // AudioSource.isPlaying crosses an audio buffer boundary after the final sample.
                AudioSettings.GetDSPBufferSize(out int bufferLength, out int buffers);
                audioBudget += (float)(bufferLength * buffers) / AudioSettings.outputSampleRate;
                report.AppendLine($"SETTLE bounds particleUnscaledSeconds={particleBudget:F3} " +
                    $"voiceDspSeconds={audioBudget:F3}; existing case30s/suite600s watchdog unchanged.");
                Write();
            }
            ObserveSamuraiCleanup();
            bool empty = game.battleVfx.ActiveEffectCount == 0 && game.battleSfx.ActiveVoiceCount == 0;
            if (!empty)
                return false;
            foreach (var item in cleanupEffects)
                Require(CleanupField<float>(item, "age") <= 4f,
                    "Effect reached BattleVfxPlayer's four-second forced-release ceiling; not natural expiry.");
            report.AppendLine($"SETTLE CLEAN case={step + 1} extraWallSeconds={Now - cleanupBegan:F3} " +
                $"extraUnscaledSeconds={Time.unscaledTime - cleanupUnscaledBegan:F3} " +
                $"extraDspSeconds={AudioSettings.dspTime - cleanupDspBegan:F3} effects=0 voices=0");
            // Natural expiry is proven. The caller may now perform its explicit lifecycle reset.
            cleanupObserved = false;
            return true;
        }

        static void ObserveSamuraiCleanup()
        {
            if (!samuraiSuite || !cleanupObserved)
                return;
            Require(cleanupState == SamuraiCleanupState(),
                "Settling changed cues, contacts, callbacks, health, participant or equipment ownership.");
            Require(game.battleVfx.isActiveAndEnabled && game.battleSfx.isActiveAndEnabled &&
                !CleanupField<FrankBattlePairPlayback>(game.battleVfx, "owner") &&
                !CleanupField<FrankBattlePairPlayback>(game.battleSfx, "owner") &&
                !CleanupField<bool>(game.battleSfx, "menuPaused") &&
                CleanupField<HashSet<AudioSource>>(game.battleSfx, "suspendedVoices").Count == 0,
                "Presentation reacquired a sequence or a pause while settling.");
            CheckCompletion();
            bool particlesOverdue = game.battleVfx.ActiveEffectCount > 0 &&
                Time.unscaledTime - cleanupUnscaledBegan > particleBudget;
            bool audioOverdue = game.battleSfx.ActiveVoiceCount > 0 &&
                AudioSettings.dspTime - cleanupDspBegan > audioBudget;
            if (!particlesOverdue && !audioOverdue)
                return;
            ReportSamuraiParticles();
            ReportSamuraiVoices();
            Write();
            Require(false, "Presentation exceeded its declared finite lifetime; see SETTLE particle/voice diagnostics.");
        }

        static string SamuraiCleanupState()
        {
            string result = $"{HealthSnapshot()}|{contacts}|{starts}|{sourceEnded}|{targetEnded}|" +
                $"{game.battleSfx.PlayedCueCount}|{game.battleVfx.PlayedEffectCount}|" +
                $"{game.battleVfx.ContactCount}|{feedback.HitStopCount}|{lighting.PlayedFlashCount}|" +
                $"{shake.ShakeCount}|{game.IsEventQueueBusy}|";
            foreach (var fighter in fighters)
                result += $"{fighter.PlaybackId}/{fighter.SourcePlayback?.GetEntityId()}/" +
                    $"{fighter.IsBusy}/{fighter.IsDead}/{fighter.enabled}/{fighter.Animator.enabled}|";
            foreach (var manager in equipment)
                result += $"{RequestedWeapon(manager)}/{manager.ActiveWeapon}/{manager.IsUnarmedPresentation}|";
            return result;
        }

        static void ReportSamuraiParticles()
        {
            var instances = CleanupField<IEnumerable>(game.battleVfx, "instances");
            int roots = 0;
            foreach (var item in instances)
            {
                var root = CleanupField<GameObject>(item, "root");
                if (!root || !root.activeSelf)
                    continue;
                roots++;
                if (!cleanupEffects.Contains(item))
                    cleanupEffects.Add(item);
                var prefab = CleanupField<GameObject>(item, "prefab");
                float age = CleanupField<float>(item, "age");
                report.AppendLine($"SETTLE effect={root.name} id={root.GetEntityId()} prefab={prefab.name} " +
                    $"poolAgeUnscaled={age:F3} activeHierarchy={root.activeInHierarchy} " +
                    $"flightOwner={CleanupField<FrankBattlePairPlayback>(item, "flightOwner")}");
                Require(!CleanupField<Transform>(item, "flightTarget"),
                    "A source-owned projectile retained flight ownership after completion.");
                foreach (var ps in CleanupField<ParticleSystem[]>(item, "particles"))
                {
                    if (!ps)
                        continue;
                    var main = ps.main;
                    var trails = ps.trails;
                    bool alive = ps.IsAlive(false);
                    report.AppendLine($"SETTLE particle={ps.name} id={ps.GetEntityId()} alive={alive} " +
                        $"count={ps.particleCount} time={ps.time:F3} playing={ps.isPlaying} " +
                        $"emitting={ps.isEmitting} paused={ps.isPaused} duration={main.duration:F3} " +
                        $"delay={main.startDelay.constantMax:F3}/{main.startDelay.mode} " +
                        $"lifetime={main.startLifetime.constantMax:F3}/{main.startLifetime.mode} " +
                        $"speed={main.simulationSpeed:F3} unscaled={main.useUnscaledTime} " +
                        $"loop={main.loop} culling={main.cullingMode} trails={trails.enabled}");
                    if (!alive)
                        continue;
                    Require(!main.loop && main.useUnscaledTime && main.simulationSpeed > 0 && !ps.isPaused &&
                        !ps.subEmitters.enabled && !ps.lifetimeByEmitterSpeed.enabled,
                        "Active particle needs unsupported lifetime/clock ownership; inspect diagnostics.");
                    float lifetime = ConstantMaximum(main.startLifetime);
                    float delay = ConstantMaximum(main.startDelay);
                    float trail = trails.enabled && !trails.dieWithParticles
                        ? ConstantMaximum(trails.lifetime) : 0;
                    float declared = (delay + main.duration + lifetime + trail) / main.simulationSpeed;
                    var particles = new ParticleSystem.Particle[ps.particleCount];
                    int count = ps.GetParticles(particles);
                    float remaining = 0;
                    for (int i = 0; i < count; i++)
                        remaining = Mathf.Max(remaining, particles[i].remainingLifetime);
                    remaining = (remaining + trail) / main.simulationSpeed;
                    if (ps.isEmitting)
                        remaining = Mathf.Max(remaining,
                            (Mathf.Max(0, main.duration - ps.time) + delay + lifetime + trail) /
                            main.simulationSpeed);
                    report.AppendLine($"SETTLE particleBound={ps.name} declaredSeconds={declared:F3} " +
                        $"remainingSeconds={remaining:F3} poolCeilingRemaining={4f - age:F3}");
                    Require(float.IsFinite(remaining) && remaining >= 0 &&
                        age + remaining < 4f,
                        "Particle tail cannot naturally finish before the pool's forced-release ceiling.");
                    particleBudget = Mathf.Max(particleBudget, remaining);
                }
            }
            Require(roots == game.battleVfx.ActiveEffectCount &&
                game.battleVfx.weaponTrails.ActiveTrailCount == 0,
                "Active presentation includes an unaccounted pool instance or retained weapon trail.");
        }

        static float ConstantMaximum(ParticleSystem.MinMaxCurve curve)
        {
            Require(curve.mode == ParticleSystemCurveMode.Constant ||
                curve.mode == ParticleSystemCurveMode.TwoConstants,
                "Particle lifetime needs a proven curve bound; a sampled guess is not a cleanup deadline.");
            float maximum = curve.mode == ParticleSystemCurveMode.Constant
                ? curve.constant : Mathf.Max(curve.constantMin, curve.constantMax);
            Require(float.IsFinite(maximum) && maximum >= 0, "Invalid declared particle lifetime.");
            return maximum;
        }

        static void ReportSamuraiVoices()
        {
            var voices = CleanupField<List<AudioSource>>(game.battleSfx, "voices").ToList();
            var announcer = CleanupField<AudioSource>(game.battleSfx, "announcer");
            if (announcer)
                voices.Add(announcer);
            var groups = CleanupField<Dictionary<AudioSource, string>>(game.battleSfx, "voiceGroups");
            var startsAt = CleanupField<Dictionary<AudioSource, double>>(game.battleSfx, "voiceStarts");
            var suspended = CleanupField<HashSet<AudioSource>>(game.battleSfx, "suspendedVoices");
            Require(suspended.Count == 0, "Suspended audio remains after the case settled.");
            int active = 0;
            foreach (var voice in voices.Where(voice => voice && voice.isPlaying))
            {
                active++;
                var clip = voice.clip;
                groups.TryGetValue(voice, out string group);
                startsAt.TryGetValue(voice, out double born);
                report.AppendLine($"SETTLE voice={voice.name} id={voice.GetEntityId()} group={group} " +
                    $"clip={clip?.name} length={clip?.length:F3} samples={voice.timeSamples}/{clip?.samples} " +
                    $"pitch={voice.pitch:F3} loop={voice.loop} dspAge={AudioSettings.dspTime - born:F3}");
                Require(clip && !voice.loop && float.IsFinite(voice.pitch) && voice.pitch > 0,
                    "Active audio has no finite forward-playing clip lifetime.");
                float remaining = Mathf.Max(0, clip.samples - voice.timeSamples) /
                    ((float)clip.frequency * voice.pitch);
                audioBudget = Mathf.Max(audioBudget, remaining);
                report.AppendLine($"SETTLE voiceBound={voice.name} remainingDspSeconds={remaining:F3}");
            }
            Require(active == game.battleSfx.ActiveVoiceCount,
                "Active audio includes an unaccounted pooled voice.");
        }
    }
}
