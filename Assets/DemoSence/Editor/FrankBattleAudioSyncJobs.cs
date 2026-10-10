namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectBattleAudioSync() => BattleAudioSyncReview.Inspect();
        public static void AlignBattleAudioTransients() => BattleAudioTimingSetup.Apply();
        public static void ValidateBattleAudioSync() => BattleAudioSyncReview.Validate();
        public static void CheckBattleAudioSyncInPlayMode() => BattleAudioSyncPlayCheck.Start();
    }
}
