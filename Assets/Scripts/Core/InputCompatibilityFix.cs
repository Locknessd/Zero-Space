using UnityEngine;
using UnityEngine.EventSystems;

// Ensures the default StandaloneInputModule doesn't spam exceptions when the old Input axes
// are not configured (common in builds that use the new Input System only).
// Place this in the scene (it will auto-run) to disable the StandaloneInputModule early.
[DefaultExecutionOrder(-1000)]
public class InputCompatibilityFix : MonoBehaviour
{
    void Awake()
    {
        bool missing = false;
        try
        {
            // Try reading the classic axes; if they are not defined, Unity will throw ArgumentException
            _ = Input.GetAxisRaw("Horizontal");
            _ = Input.GetAxisRaw("Vertical");
        }
        catch (System.ArgumentException)
        {
            missing = true;
        }

        if (missing)
        {
            Debug.LogWarning("InputCompatibilityFix: Classic Input axes 'Horizontal'/'Vertical' not found. Disabling StandaloneInputModule to avoid exception loop.");
            var es = FindObjectOfType<EventSystem>();
            if (es != null)
            {
                var sim = es.GetComponent<StandaloneInputModule>();
                if (sim != null && sim.enabled)
                {
                    sim.enabled = false;
                    Debug.Log("InputCompatibilityFix: StandaloneInputModule disabled.");
                }
            }
            else
            {
                Debug.LogWarning("InputCompatibilityFix: No EventSystem found in scene.");
            }
        }
    }
}
