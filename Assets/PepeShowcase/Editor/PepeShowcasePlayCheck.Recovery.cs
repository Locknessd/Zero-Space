using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FrankRetarget.Editor
{
    internal static partial class PepeShowcasePlayCheck
    {
        // Failed compilation can prevent EnteredPlayMode/EnteredEditMode callbacks altogether.
        internal static void CancelPendingRecording()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before cancelling a pending recording.");
            if (SessionState.GetInt(Key, 0) != 1) return;
            string copy = SessionState.GetString(Key + ".copy", "");
            if (!copy.StartsWith(PepeShowcaseSetup.Root + "/Editor/PlayCheck-", StringComparison.Ordinal))
                throw new InvalidOperationException("Pending recording does not point to a generated scene copy.");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + ".previous", ""));
            AssetDatabase.DeleteAsset(copy);
            SessionState.SetInt(Key, 0);
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void CancelPepePendingRecording() => PepeShowcasePlayCheck.CancelPendingRecording();
    }
}
