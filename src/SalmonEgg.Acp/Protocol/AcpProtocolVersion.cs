namespace SalmonEgg.Acp.Protocol
{
    public static class AcpProtocolVersion
    {
        public const int V1 = 1;
        public const int V2 = 2;
        public const int Latest = V2;

        public static bool IsSupported(int version)
            => version is V1 or V2;
    }
}
