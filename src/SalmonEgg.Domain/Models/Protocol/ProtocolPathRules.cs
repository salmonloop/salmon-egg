using System;

namespace SalmonEgg.Domain.Models.Protocol
{
    public static class ProtocolPathRules
    {
        public static bool IsAbsolutePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal)
                || trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            return trimmed.Length >= 3
                && char.IsLetter(trimmed[0])
                && trimmed[1] == ':'
                && (trimmed[2] == '\\' || trimmed[2] == '/');
        }
    }
}
