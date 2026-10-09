using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        sealed class RetreatRender : IDisposable
        {
            readonly RenderTexture texture = RenderTexture.GetTemporary(480, 320, 24);
            readonly Texture2D stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            readonly Texture2D sheet = new Texture2D(1920, 320, TextureFormat.RGB24, false);
            readonly RenderTexture previous = RenderTexture.active;

            public void Draw(CharacterCombat source, CharacterCombat target, FrankTestActor actor,
                Camera camera, FrankCinematicCamera framing, int column)
            {
                var targetTexture = camera.targetTexture;
                float aspect = camera.aspect;
                try
                {
                    camera.targetTexture = texture;
                    camera.aspect = 1.5f;
                    using var skin = new CombatExpansionPreviewSkin(
                        source.gameObject, target.gameObject, actor.gameObject);
                    framing.Apply(0, true);
                    FitRetreatWeapon(camera, actor);
                    skin.Sample();
                    camera.Render();
                    RenderTexture.active = texture;
                    stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0);
                    stamp.Apply();
                    sheet.SetPixels(column * 480, 0, 480, 320, stamp.GetPixels());
                }
                finally
                {
                    camera.targetTexture = targetTexture;
                    camera.aspect = aspect;
                    RenderTexture.active = previous;
                }
            }

            public void Save(string path)
            {
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
            }

            static void FitRetreatWeapon(Camera camera, FrankTestActor actor)
            {
                // Battle framing caches the pair actors, so include the independent candidate sword explicitly.
                float tanY = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) * .9f;
                float tanX = tanY * camera.aspect;
                float dolly = 0;
                foreach (var renderer in actor.Pose.weaponRenderers)
                {
                    if (!renderer || !renderer.enabled)
                        continue;
                    var bounds = renderer.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var sign = new Vector3((i & 1) == 0 ? -1 : 1,
                            (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                        var point = camera.transform.InverseTransformPoint(
                            bounds.center + Vector3.Scale(bounds.extents, sign));
                        dolly = Mathf.Max(dolly, Mathf.Abs(point.x) / tanX - point.z);
                        dolly = Mathf.Max(dolly, Mathf.Abs(point.y) / tanY - point.z);
                        dolly = Mathf.Max(dolly, camera.nearClipPlane - point.z);
                    }
                }
                camera.transform.position -= camera.transform.forward * dolly;
            }

            public void Dispose()
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
