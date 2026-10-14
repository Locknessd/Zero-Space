namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectSlapEndPositions() => BattleSlapPositionReview.Inspect();
        public static void ValidateSlapEndPositions() => BattleSlapPositionReview.Validate();
    }
}
