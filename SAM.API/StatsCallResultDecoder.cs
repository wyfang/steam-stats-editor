// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;

namespace SAM.API
{
    public static class StatsCallResultDecoder
    {
        public static Types.UserStatsReceived Decode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length != 20 && payload.Length != 24)
                throw new ArgumentException("UserStatsReceived requires the reported 20- or 24-byte native payload.", nameof(payload));
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Steam Stats Editor targets little-endian Windows.");
            // CSteamID starts at offset 12, including when native tail padding makes
            // cubParam 24 bytes. Marshal's default 8-byte field alignment is not used.
            return new Types.UserStatsReceived
            {
                GameId = BitConverter.ToUInt64(payload, 0),
                Result = BitConverter.ToInt32(payload, 8),
                SteamIdUser = BitConverter.ToUInt64(payload, 12),
            };
        }
    }
}
