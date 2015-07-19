// PkgCmdID.cs
// MUST match PkgCmdID.h
using System.Diagnostics.CodeAnalysis;

namespace Wijits.FastKoala
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class PkgCmdIDList
    {
        public const uint cmdidEnableBuildTimeTransformationsProjItem = 0x100;
        public const uint cmdidEnableBuildTimeTransformationsProj = 0x101;
        public const uint cmdidAddMissingTransformsProjItem = 0x102;
    };
}