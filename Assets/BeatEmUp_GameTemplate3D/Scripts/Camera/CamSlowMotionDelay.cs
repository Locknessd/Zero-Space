using System.Collections;
using UnityEngine;

/// <summary>
/// TEMPLATE SLOW-MOTION EFFECT (BeatEmUp GameTemplate3D).
///
/// WHY IT IS DISABLED BY DEFAULT HERE
/// This effect writes Time.timeScale = 0.2 for a short window on every hit. That is fine for a
/// self-contained action game, but in THIS project the whole combat sequence is choreographed by a
/// queue (CombatPositioningController) that paces its step-ins, clip waits and spacing pauses in real
/// time. Dropping the time scale stretched every one of those waits fivefold -- a 0.35s step took 1.75s,
/// a 0.4s breath took 2s -- and the queue appeared to stall for many seconds per turn.
///
/// The combat pipeline has since been made independent of Time.timeScale (it paces with
/// Time.unscaledDeltaTime), so this effect can be re-enabled if the slow-motion look is wanted. It is
/// left OFF by default because it also slows the fighters' animation playback, which makes the whole
/// exchange read as sluggish rather than punchy.
///
/// SET enableSlowMotion = false TO GUARANTEE NO TIME-SCALE CHANGE, or tick it back on to restore the
/// template behaviour.
/// </summary>
public class CamSlowMotionDelay : MonoBehaviour {

	[Tooltip("Master switch for the slow-motion effect. OFF = Time.timeScale is never modified by this component.")]
	public bool enableSlowMotion = false;

	[Tooltip("Time scale applied while the effect runs. 0.2 = one fifth speed.")]
	public float slowMotionTimeScale = .2f;

	public void StartSlowMotionDelay(float duration){
		// Disabled: never touch the time scale, so the combat queue's pacing stays predictable.
		if (!enableSlowMotion) return;

		StopAllCoroutines();
		StartCoroutine(SlowMotionRoutine(duration));
	}

	//slow motion delay
	IEnumerator SlowMotionRoutine(float duration) {

		//set timescale
		Time.timeScale = slowMotionTimeScale;

		//wait a moment...
		float startTime = Time.realtimeSinceStartup;
		while (Time.realtimeSinceStartup < (startTime + duration)) {
			yield return null;
		}
		
		//reset timescale
		GameSettings settings = Resources.Load("GameSettings", typeof(GameSettings)) as GameSettings;
		if (settings != null) { 
			Time.timeScale = settings.timeScale;
		} else {
			Time.timeScale = 1;
		}
	}
}
