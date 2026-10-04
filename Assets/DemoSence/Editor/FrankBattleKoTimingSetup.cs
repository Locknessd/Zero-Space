using System;
using System.IO;
using UnityEditor;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        internal const string KoTimingReview = "GeneratedAssets/BattleKoTimingReview";

        [MenuItem("Tools/Battle/Apply KO after finisher and slow motion")]
        public static void InstallDeferredBattleKo()
        {
            Directory.CreateDirectory(KoTimingReview);
            if (!File.Exists(KoTimingReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, KoTimingReview + "/BattleSceneBefore.unity.txt");
            ComicScene((game, camera) =>
            {
                if (!game.uiManager.knockout || !game.GetComponent<BattleImpactFeedback>())
                    throw new Exception("Battle KO/slow-motion bindings are missing.");
                EditorUtility.SetDirty(game.uiManager.knockout);
            });
        }

        [MenuItem("Tools/Battle/Check deferred KO timing in Play Mode")]
        public static void BattleKoTimingPlayCheck() => FrankBattleAnimationTestPlayCheck.Start(timingOnly: true);
    }
}
