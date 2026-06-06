using System;
using System.Security.Cryptography;
using System.Text;

namespace DrawServer
{
    public static class EncryptionHelper
    {
        // Key 256-bit dùng PBKDF2 — phải khớp với DrawClient
        private static readonly byte[] _key = DeriveKey();

        private static byte[] DeriveKey()
        {
            using (var kdf = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes("CTVCQM_NT106_SecretKey"),
                Encoding.UTF8.GetBytes("CTVCQM_Salt_2024"),
                10000, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(32);
            }
        }

        // Mã hóa JSON → Base64(IV[16] + CipherText)
        public static string Encrypt(string plaintext)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = _key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                    byte[] cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

                    byte[] result = new byte[16 + cipherBytes.Length];
                    Buffer.BlockCopy(aes.IV, 0, result, 0, 16);
                    Buffer.BlockCopy(cipherBytes, 0, result, 16, cipherBytes.Length);

                    return Convert.ToBase64String(result);
                }
            }
        }

        // Giải mã Base64(IV[16] + CipherText) → JSON
        public static string Decrypt(string cipherBase64)
        {
            byte[] data = Convert.FromBase64String(cipherBase64);

            byte[] iv = new byte[16];
            byte[] cipherBytes = new byte[data.Length - 16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            Buffer.BlockCopy(data, 16, cipherBytes, 0, cipherBytes.Length);

            using (var aes = Aes.Create())
            {
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] plaintextBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plaintextBytes);
                }
            }
        }
    }
}
