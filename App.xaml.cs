using System.Windows;
using Hale_Marketing_International.Backup;
using Hale_Marketing_International.Security;

namespace Hale_Marketing_International
{
    public partial class App : Application
    {
        // Single shared BackupManager — used by all windows
        public static readonly BackupManager BackupManager = new BackupManager();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DatabaseInitializer.EnsureAllTables();
            
            // ── No local DB found → show login with restore option ─────────────
            if (!AppSettings.Instance.IsSetupComplete)
            {
                var result = MessageBox.Show(
                    "No local data found on this device.\n\n" +
                    "• Brand new installation → click NO\n" +
                    "  Then press Ctrl+Shift+A on the login screen to create an account.\n\n" +
                    "• Switching from another device → click YES\n" +
                    "  Sign in with Google to restore your data.",
                    "Welcome to Hale Marketing POS",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var restore = new Hale_Marketing_International.Views.RestoreWindow();
                    restore.ShowDialog();
                    // RestoreWindow restarts the app on success, so we return here
                    return;
                }
            }

            // ── Try to silently reconnect Google (cached token) ────────────────
            if (AppSettings.Instance.GoogleDriveConfigured)
                await BackupManager.TrySilentConnectAsync();

            // ── Start 24-hour auto-backup timer ───────────────────────────────
            BackupManager.StartDailyTimer();

            // ── Show login window ─────────────────────────────────────────────
           // var login = new MainWindow();
            //login.Show();
            //MainWindow = login;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Only backup if Google is actually connected — skip if not
                if (AppSettings.Instance.GoogleDriveConfigured &&
                    !string.IsNullOrEmpty(BackupManager.ConnectedEmail))
                {
                    // Run with a 10-second timeout so it never hangs shutdown
                    var task = BackupManager.BackupOnCloseAsync();
                    task.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch { /* never block shutdown on backup failure */ }
            finally
            {
                BackupManager.StopDailyTimer();
                base.OnExit(e);
            }
        }
    }
}