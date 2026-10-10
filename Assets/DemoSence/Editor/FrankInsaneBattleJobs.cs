namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectInsaneBattleCombos() => BattleComboAuthoring.Inspect();
        public static void InstallInsaneBattleCombos() => BattleComboAuthoring.Install();
        public static void StudyInsaneBattleCombos() => BattleComboAuthoring.Study();
        public static void ValidateAllAttackPresentation() => BattleAttackReview.Run(false);
        public static void RepairAllAttackPresentation() => BattleAttackReview.Run(true);
        public static void FinishInsaneBattlePresentation() => BattleComboAuthoring.FinalizePresentation();
        public static void RepairAttackReviewFindings() => BattleAttackReview.RepairFindings();
        public static void ValidateInsaneComboFrameSkips() => BattleAttackReview.ValidateComboFrameSkips();
        public static void RepairCombo2OpeningGunfire() => BattleComboAuthoring.RepairCombo2OpeningGunfire();
        public static void ValidateCombo2Gunfire() => BattleAttackReview.ValidateCombo2Gunfire();
        public static void ApplyBattleWeaponSoundPacks() => BattleWeaponAudioSetup.Apply();
        public static void ValidateBattleWeaponSoundPacks() => BattleWeaponAudioReview.Validate();
        public static void StudyLight3Contact() => BattleAttackReview.StudyLight3();
        public static void RepairLight3Contact()
        {
            BattleAttackReview.RepairLight3();
            BattleAttackReview.ValidateLight3();
        }
        public static void ValidateLight3Contact() => BattleAttackReview.ValidateLight3();
    }
}
