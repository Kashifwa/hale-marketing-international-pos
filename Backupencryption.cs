using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hale_Marketing_International.Security
{
    /// <summary>
    /// AES-256 encryption for SQLite backup files.
    /// Master key is derived automatically from the user's Google email.
    /// The user never sees or types the key — it is always the same for their email.
    /// </summary>
    public static class BackupEncryption
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("POSBACKUP1");

        /// <summary>
        /// Derives a 256-bit encryption key from a Google email address.
        /// Same email always produces the same key — deterministic and automatic.
        /// </summary>
        public static byte[] DeriveKeyFromEmail(string googleEmail, byte[] salt)
        {
            // Normalise email (lowercase, trimmed) so casing never matters
            string normalisedEmail = googleEmail.Trim().ToLowerInvariant();

            // Use PBKDF2 with the email as password + random salt per backup
            using var pbkdf2 = new Rfc2898DeriveBytes(
                normalisedEmail, salt, 100_000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32); // 256-bit key
        }

        /// <summary>
        /// Encrypts the SQLite DB to an output file using the Google email as key source.
        /// Format: [magic(10)] [salt(16)] [iv(16)] [ciphertext]
        /// </summary>
        public static void EncryptFile(string dbPath, string outputPath, string googleEmail)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] key = DeriveKeyFromEmail(googleEmail, salt);
            byte[] iv = RandomNumberGenerator.GetBytes(16);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            fsOut.Write(Magic, 0, Magic.Length);
            fsOut.Write(salt, 0, salt.Length);
            fsOut.Write(iv, 0, iv.Length);

            using var cs = new CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var fsIn = new FileStream(dbPath, FileMode.Open, FileAccess.Read);
            fsIn.CopyTo(cs);
        }

        /// <summary>
        /// Decrypts a backup file using the Google email as key source.
        /// Throws InvalidOperationException if the email is wrong or file is corrupt.
        /// </summary>
        public static void DecryptFile(string encryptedPath, string outputPath, string googleEmail)
        {
            using var fsIn = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read);

            // Verify magic header
            byte[] magic = new byte[Magic.Length];
            fsIn.Read(magic, 0, magic.Length);
            if (Encoding.ASCII.GetString(magic) != Encoding.ASCII.GetString(Magic))
                throw new InvalidOperationException("Invalid backup file.");

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            fsIn.Read(salt, 0, 16);
            fsIn.Read(iv, 0, 16);

            byte[] key = DeriveKeyFromEmail(googleEmail, salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            try
            {
                using var cs = new CryptoStream(fsIn, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                cs.CopyTo(fsOut);
            }
            catch (CryptographicException)
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
                throw new InvalidOperationException("Decryption failed — wrong Google account.");
            }
        }
    }
}