using System;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace Hale_Marketing_International.Services
{
    public static class UserService
    {
        private static string _dbPath = "Data Source=posdata.db;Version=3;";

        // Call this on app startup to ensure table exists
        public static void EnsureUserTable()
        {
            using (var con = new SQLiteConnection(_dbPath))
            {
                con.Open();
                using (var cmd = new SQLiteCommand(con))
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Users (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Username TEXT NOT NULL UNIQUE,
                            PasswordHash TEXT NOT NULL,
                            FullName TEXT,
                            Role TEXT DEFAULT 'User',
                            IsActive INTEGER DEFAULT 1,
                            CreatedAt TEXT
                        );";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ── Hash password with SHA256 ─────────────────────────────────────────
        public static string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ── Validate login ────────────────────────────────────────────────────
        public static bool ValidateCredentials(string username, string password)
        {
            try
            {
                string hash = HashPassword(password);
                using (var con = new SQLiteConnection(_dbPath))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(
                        @"SELECT COUNT(*) FROM Users 
                          WHERE Username = @u AND PasswordHash = @p AND IsActive = 1", con))
                    {
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.Parameters.AddWithValue("@p", hash);
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch { return false; }
        }

        // ── Add user (you call this from your admin tool on YOUR pc) ──────────
        public static bool AddUser(string username, string password,
                                   string fullName = "", string role = "User")
        {
            try
            {
                string hash = HashPassword(password);
                using (var con = new SQLiteConnection(_dbPath))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(
                        @"INSERT INTO Users (Username, PasswordHash, FullName, Role, IsActive, CreatedAt)
                          VALUES (@u, @p, @fn, @role, 1, @date)", con))
                    {
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.Parameters.AddWithValue("@p", hash);
                        cmd.Parameters.AddWithValue("@fn", fullName);
                        cmd.Parameters.AddWithValue("@role", role);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch { return false; }
        }
        // Add this method to UserService.cs
        public static bool AdminExists()
        {
            try
            {
                using (var con = new SQLiteConnection(_dbPath))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT COUNT(*) FROM Users WHERE IsActive = 1", con))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch { return false; }
        }
        // ── Change password ───────────────────────────────────────────────────
        public static bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            if (!ValidateCredentials(username, oldPassword)) return false;
            try
            {
                string newHash = HashPassword(newPassword);
                using (var con = new SQLiteConnection(_dbPath))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(
                        "UPDATE Users SET PasswordHash = @p WHERE Username = @u", con))
                    {
                        cmd.Parameters.AddWithValue("@p", newHash);
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch { return false; }
        }

        // ── Get user info ─────────────────────────────────────────────────────
        public static (string FullName, string Role) GetUserInfo(string username)
        {
            try
            {
                using (var con = new SQLiteConnection(_dbPath))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT FullName, Role FROM Users WHERE Username = @u", con))
                    {
                        cmd.Parameters.AddWithValue("@u", username);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                                return (r["FullName"].ToString(), r["Role"].ToString());
                        }
                    }
                }
            }
            catch { }
            return ("", "User");
        }
    }
}