using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        sealed class SourceSession : IDisposable
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

            public SourceSession()
            {
                values = singletons.Select(p => p.GetValue(null)).ToArray();
                try
                {
                    scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                    var fighters = scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                    Fighters = new[] { "Mankey", "Pepe" }.Select(name =>
                        fighters.SingleOrDefault(f => f.name == name)).ToArray();
                    for (int i = 0; i < Fighters.Length; i++)
                    {
                        var fighter = Fighters[i];
                        if (!fighter || !fighter.Animator || !fighter.Animator.avatar ||
                            !fighter.Animator.avatar.isValid || !fighter.Animator.avatar.isHuman)
                            throw new InvalidOperationException("Missing retargetable BattleScene fighter " +
                                new[] { "Mankey", "Pepe" }[i]);
                    }
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
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
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
