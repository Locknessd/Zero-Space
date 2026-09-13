using System.Collections;
using UnityEngine;

/// <summary>
/// Lightweight coroutine runner that can start coroutines from code even if the original
/// caller GameObject is inactive or destroyed. The runner GameObject is created
/// temporarily and destroyed when the coroutine finishes.
/// </summary>
public class CoroutineRunner : MonoBehaviour
{
    public static void Run(IEnumerator routine)
    {
        if (routine == null) return;
        var go = new GameObject("__CoroutineRunner");
        DontDestroyOnLoad(go);
        var runner = go.AddComponent<CoroutineRunner>();
        runner.StartCoroutine(runner.RunAndDestroy(routine));
    }

    IEnumerator RunAndDestroy(IEnumerator routine)
    {
        yield return StartCoroutine(routine);
        Destroy(gameObject);
    }
}
