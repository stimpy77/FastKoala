// Guids.cs
// MUST match guids.h
using System;
using System.Diagnostics.CodeAnalysis;

namespace Wijits.FastKoala
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class GuidList
    {
        public const string guidFastKoalaPkgString = "5a540277-c820-4fb4-9cb5-f878fc9af04e";
        public const string guidFastKoalaProjItemMenuCmdSetString = "fb9920bc-46a6-4d85-9926-a08a10239137";
        public const string guidFastKoalaProjMenuCmdSetString = "fb9920bc-46a6-4d85-9926-a08a10239136";

        public static readonly Guid guidFastKoalaProjItemMenuCmdSet = new Guid(guidFastKoalaProjItemMenuCmdSetString);
        public static readonly Guid guidFastKoalaProjMenuCmdSet = new Guid(guidFastKoalaProjMenuCmdSetString);
    };
}