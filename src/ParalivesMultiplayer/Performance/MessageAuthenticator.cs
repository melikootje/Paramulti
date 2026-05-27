using System;
using System.Security.Cryptography;
using System.Text;

namespace ParalivesMultiplayer.Performance
{
    public static class MessageAuthenticator
    {
        static byte[] _sharedSecret;
        static bool _enabled;

        const int MaxPlayerNameLength = 32;
        const int MaxChatMessageLength = 512;

        public static void Initialize(bool enabled, string sharedSecret)
        {
            _enabled = enabled;
            if (!string.IsNullOrEmpty(sharedSecret))
            {
                _sharedSecret = Encoding.UTF8.GetBytes(sharedSecret);
            }
            Plugin.Log.LogInfo($"[Auth] Initialized: enabled={enabled}, secret={(sharedSecret != null ? "set" : "not set")}");
        }

        public static bool IsEnabled => _enabled;

        public static byte[] ComputeHmac(string data)
        {
            if (!_enabled || _sharedSecret == null) return Array.Empty<byte>();

            using (var hmac = new HMACSHA256(_sharedSecret))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        public static byte[] ComputeMessageAuth(string messageCode, int playerId, uint tick)
        {
            if (!_enabled || _sharedSecret == null) return Array.Empty<byte>();

            var payload = $"{messageCode}:{playerId}:{tick}:{DateTime.UtcNow.Ticks}";
            return ComputeHmac(payload);
        }

        public static bool VerifyMessageAuth(string messageCode, int playerId, uint tick, byte[] providedHash)
        {
            if (!_enabled || _sharedSecret == null) return true;
            if (providedHash == null || providedHash.Length == 0) return false;

            var expected = ComputeMessageAuth(messageCode, playerId, tick);
            return ConstantTimeCompare(expected, providedHash);
        }

        static bool ConstantTimeCompare(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= a[i] ^ b[i];

            return result == 0;
        }

        public static string SanitizePlayerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";

            if (name.Length > MaxPlayerNameLength)
                name = name.Substring(0, MaxPlayerNameLength);

            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }

        public static string SanitizeChatMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            if (message.Length > MaxChatMessageLength)
                message = message.Substring(0, MaxChatMessageLength);

            var sb = new StringBuilder();
            foreach (char c in message)
            {
                if (c < 32 && c != '\n' && c != '\r')
                    continue;
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static bool IsControlCharacter(char c)
        {
            return c < 32 || c == 127;
        }

        public static bool ContainsInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            var injectionPatterns = new[] { ";;", "||", "&&", "$(", "`", "<script>", "</script>", "javascript:" };
            var lower = input.ToLower();

            foreach (var pattern in injectionPatterns)
            {
                if (lower.Contains(pattern))
                    return true;
            }

            return false;
        }
    }
}
