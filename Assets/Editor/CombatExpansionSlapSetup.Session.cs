using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        sealed class PreviewSession : IDisposable
        {
            readonly Scene active = SceneManager.GetActiveScene();
            readonly PropertyInfo[] singletons = new[]
            {
                typeof(GameManager), typeof(MemeBattleUI), typeof(WebSocketManager),
                typeof(MortalKombatCamera), typeof(CombatPositioningController)
            }.Select(t => t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)).ToArray();
            readonly object[] values;
            Scene scene;
            public CharacterCombat[] Fighters { get; private set; }

            public PreviewSession()
            {
                values = singletons.Select(p => p.GetValue(null)).ToArray();
                try
                {
                    scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                    typeof(CombatPositioningController).GetProperty("Instance").SetValue(null, null);
                    var all = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                    Fighters = new[] { "Mankey", "Pepe" }.Select(name => all.Single(f => f.name == name)).ToArray();
                    foreach (var fighter in Fighters)
                    {
                        fighter.battleSfx = null;
                        fighter.battleVfx = null;
                        fighter.hitEffect = null;
                        if (!fighter.Initialize() || !fighter.Animator || !fighter.Animator.avatar ||
                            !fighter.Animator.avatar.isValid || !fighter.Animator.isHuman)
                            throw new InvalidOperationException("Cannot initialize SlapFace fighter " + fighter.name);
                    }
                    if (Fighters[0].Animator.avatar == Fighters[1].Animator.avatar)
                        throw new InvalidOperationException("SlapFace calibration requires distinct avatars.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                try
                {
                    try
                    {
                        if (Fighters != null)
                            foreach (var fighter in Fighters)
                                if (fighter)
                                    fighter.SourcePlayback?.Cancel();
                    }
                    finally
                    {
                        if (scene.IsValid())
                            EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
                finally
                {
                    for (int i = 0; i < singletons.Length; i++)
                        singletons[i].SetValue(null, values[i]);
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                }
            }
        }
    }
}
