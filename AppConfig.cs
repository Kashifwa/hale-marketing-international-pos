using System;
using System.IO;

namespace Hale_Marketing_International
{
    public static class AppConfig
    {
        public static readonly string ConnectionString;

        static AppConfig()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HaleMarketingInternational");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            string dbPath = Path.Combine(folder, "posdata.db");

            // One-time migration: agar naya db abhi tak nahi bana,
            // aur purani (debug folder) wali db maujood hai, to usay copy kar lo
            if (!File.Exists(dbPath))
            {
                string oldDevPath = Path.Combine(AppContext.BaseDirectory, "posdata.db");
                if (File.Exists(oldDevPath))
                {
                    File.Copy(oldDevPath, dbPath);
                }
            }

            ConnectionString = $"Data Source={dbPath};Version=3;";
        }
    }
}