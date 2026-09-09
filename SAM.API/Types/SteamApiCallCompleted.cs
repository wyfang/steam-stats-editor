// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System.Runtime.InteropServices;

namespace SAM.API.Types
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SteamApiCallCompleted
    {
        public ulong AsyncCall;
        public int CallbackId;
        public uint ParameterSize;
    }
}
