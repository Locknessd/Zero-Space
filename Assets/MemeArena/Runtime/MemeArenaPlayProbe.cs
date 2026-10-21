using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace MemeArena
{
    // Optional probe used only by the editor's isolated integration check.
    public sealed class MemeArenaPlayProbe : MonoBehaviour
    {
        public Action AfterFrame;
        public readonly List<float> frameMilliseconds = new List<float>();
        public long peakBatches, peakTriangles, peakGcBytes;
        public bool countersAvailable;
        ProfilerRecorder batches, triangles, gc;
        int frames;

        void Start()
        {
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            countersAvailable = batches.Valid && triangles.Valid && gc.Valid;
            StartCoroutine(Rendered());
        }

        void Update()
        {
            frames++;
            if (frames < 120) return;
            frameMilliseconds.Add(Time.unscaledDeltaTime * 1000);
            if (batches.Valid) peakBatches = Math.Max(peakBatches, batches.LastValue);
            if (triangles.Valid) peakTriangles = Math.Max(peakTriangles, triangles.LastValue);
            if (gc.Valid) peakGcBytes = Math.Max(peakGcBytes, gc.LastValue);
        }

        IEnumerator Rendered()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                AfterFrame?.Invoke();
            }
        }

        void OnDestroy()
        {
            batches.Dispose();
            triangles.Dispose();
            gc.Dispose();
        }
    }
}
