// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
namespace SAM.API.Callbacks
{
    public class UserStatsStored : Callback<Types.UserStatsStored>
    {
        public override int Id => 1102;
        public override bool IsServer => false;
    }
}
