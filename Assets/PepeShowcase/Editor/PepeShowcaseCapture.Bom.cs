using UnityEditor;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static bool recordingBom;

        [MenuItem("Tools/Pepe Showcase/Record BOM Pepe trailer")]
        public static void BeginBomTrailer()
        {
            FindEncoder();
            PepeShowcasePlayCheck.BeginRecording(PepeShowcaseSetup.BomScenePath);
            Status("STARTING isolated BOM Pepe trailer recording.");
        }

        [MenuItem("Tools/Pepe Showcase/Validate BOM Pepe trailer")]
        public static void ValidateBomTrailer() =>
            ValidateCompilation(PepeShowcaseSetup.BomScenePath, "/BomValidation.txt");
    }
}
