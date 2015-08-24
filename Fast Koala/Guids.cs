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

        private const string guidFastKoalaProjItemMenuCmdSetString = "fb9920bc-46a6-4d85-9926-a08a10239137";
        private const string guidFastKoalaProjMenuCmdSetString = "fb9920bc-46a6-4d85-9926-a08a10239136";
        private const string guidFastKoalaProjAddCmdSetString = "6B120CE5-ED5F-4B86-AEEC-FD54D1C0C9FF";

        public static readonly Guid guidFastKoalaProjItemMenuCmdSet = new Guid(guidFastKoalaProjItemMenuCmdSetString);
        public static readonly Guid guidFastKoalaProjMenuCmdSet = new Guid(guidFastKoalaProjMenuCmdSetString);
        public static readonly Guid guidFastKoalaProjAddCmdSet = new Guid(guidFastKoalaProjAddCmdSetString);
    };
}