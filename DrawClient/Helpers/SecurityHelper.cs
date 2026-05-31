using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DrawClient.Helpers
{
    public static class SecurityHelper
    {
        public static string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }

        public static int ParseUserIdFromToken(string token)
        {
            var payload = token.Split('.')[1];
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using (var doc = JsonDocument.Parse(json))
            {
                var el = doc.RootElement.GetProperty("user_id");
                return el.ValueKind == JsonValueKind.String
                    ? int.Parse(el.GetString())
                    : el.GetInt32();
            }
        }
    }
}
