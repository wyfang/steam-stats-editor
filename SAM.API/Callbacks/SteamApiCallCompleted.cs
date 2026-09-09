// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
namespace SAM.API.Callbacks
{
    public sealed class SteamApiCallCompleted : Callback<Types.SteamApiCallCompleted>
    {
        public override int Id => 703;
        public override bool IsServer => false;
    }
}
