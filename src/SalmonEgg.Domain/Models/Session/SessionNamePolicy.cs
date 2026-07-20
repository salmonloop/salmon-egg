namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// Session display name rules shared across UI and services.
    /// Keep this policy in Domain so it can be tested and reused.
    /// </summary>
    public static class SessionNamePolicy
    {
        public const int MaxLength = 80;

        public static string CreateDefault(string sessionId)
        {
            var id = (sessionId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                return "Session";
            }

            var shortId = id.Length <= 8 ? id : id.Substring(0, 8);
            return $"Session {shortId}";
        }

        public static string? Sanitize(string? input)
        {
            if (input == null)
            {
                return null;
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            return trimmed.Length <= MaxLength ? trimmed : trimmed.Substring(0, MaxLength);
        }

    }
}
