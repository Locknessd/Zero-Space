namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleLoadingEvents()
        {
            CombatFlowValidation.Run();
            System.IO.Directory.CreateDirectory("GeneratedAssets/BattleLoadingReview");
            string report = System.IO.File.ReadAllText("Temp/CombatFlowValidation.txt");
            System.IO.File.WriteAllText("GeneratedAssets/BattleLoadingReview/Validation.txt", report);
            if (report.Contains("FAIL")) throw new System.Exception("Battle loading validation failed; see Validation.txt.");
        }
        public static void BeginBattleLoadingPlayValidation() => CombatFeelPlayValidation.BeginBattleReplay();
    }
}
