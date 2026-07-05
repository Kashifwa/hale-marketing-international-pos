using System;
using System.Windows;
using Hale_Marketing_International.Security;

namespace Hale_Marketing_International.Views
{
    public partial class BackupSyncWindow : Window
    {
        public BackupSyncWindow()
        {
            InitializeComponent();
            LoadCurrentStatus();
        }

        private void LoadCurrentStatus()
        {
            var settings = AppSettings.Instance;

            // Show connected email
            TxtEmail.Text = string.IsNullOrEmpty(settings.ConnectedGoogleEmail)
                ? "Not connected"
                : settings.ConnectedGoogleEmail;

            // Show last backup time
            TxtLastBackup.Text = settings.LastBackupTime.HasValue
                ? settings.LastBackupTime.Value.ToString("dd MMM yyyy  hh:mm tt")
                : "Never";

            // Disable sync if not connected
            BtnSync.IsEnabled = settings.GoogleDriveConfigured;
        }

        // ── Sync Now ──────────────────────────────────────────────────────────

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            BtnSync.IsEnabled = false;
            BtnConnect.IsEnabled = false;
            PbSync.Visibility = Visibility.Visible;
            TxtStatus.Text = "Connecting to Google Drive...";

            var progress = new Progress<int>(v =>
            {
                PbSync.Value = v;
                TxtStatus.Text =
                    v < 40 ? "Encrypting data..." :
                    v < 80 ? "Uploading to Google Drive..." :
                              "Done!";
            });

            try
            {
                await App.BackupManager.SyncNowAsync(progress);

                TxtLastBackup.Text = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
                TxtStatus.Text = "✅ Backup successful!";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "❌ Sync failed: " + ex.Message;
            }
            finally
            {
                BtnSync.IsEnabled = true;
                BtnConnect.IsEnabled = true;
            }
        }

        // ── Connect / Change Google Account ───────────────────────────────────

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            BtnConnect.IsEnabled = false;
            TxtStatus.Text = "Opening Google sign-in...";

            try
            {
                // Delete cached token so user can switch accounts
                string tokenFolder = AppSettings.GoogleTokenFolder;
                if (System.IO.Directory.Exists(tokenFolder))
                    System.IO.Directory.Delete(tokenFolder, recursive: true);

                string email = await App.BackupManager.ConnectGoogleAccountAsync();

                TxtEmail.Text = email;
                BtnSync.IsEnabled = true;
                TxtStatus.Text = "✅ Connected as " + email;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "❌ Connection failed: " + ex.Message;
            }
            finally
            {
                BtnConnect.IsEnabled = true;
            }
        }
    }
}