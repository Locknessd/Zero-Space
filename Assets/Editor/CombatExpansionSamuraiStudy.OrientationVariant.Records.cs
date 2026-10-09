using System;
using System.Collections.Generic;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        [Serializable]
        sealed class OrientationVariantReport
        {
            public string scope = OrientationVariantScope;
            public NativeReferenceReport capture = new NativeReferenceReport
            {
                scope = OrientationVariantScope,
                status = "PROVISIONAL RUNNING; partial variant evidence only"
            };
            public List<OrientationVariantAsset> variants = new List<OrientationVariantAsset>();
        }

        [Serializable]
        sealed class OrientationVariantAsset
        {
            public string role, originalPath, variantPath, originalIdentity, variantIdentity;
            public string originalSha256, variantSha256, originalMetaSha256, variantMetaSha256;
            public long originalBytes, variantBytes;
            public bool originalKeepOriginalOrientation, variantKeepOriginalOrientation;
            public bool bytesMatch, metadataMatchesOnlyIntendedChanges, allClipSettingsChecked;
            public string originalImporterSettings, variantImporterSettings;
            public string originalClipSettings, variantClipSettings;
            public string status = "PROVISIONAL preparation incomplete";
        }
    }
}
