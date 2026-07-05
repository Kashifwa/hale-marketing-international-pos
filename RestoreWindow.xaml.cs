using System;
using System.Windows;
using Hale_Marketing_International.Backup;

namespace Hale_Marketing_International.Views
{
    public partial class RestoreWindow : Window
    {
        private readonly BackupManager _manager = new BackupManager();

        public RestoreWindow()
        {
            InitializeComponent();
        }

        // ── Step 1: Sign in ───────────────────────────────────────────────────

        private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            BtnSignIn.IsEnabled = false;
            TxtStatus.Text = "Opening Google sign-in...";

            try
            {
                // Authenticate — email is captured automatically inside the service
                string email = await _manager.ConnectGoogleAccountAsync();

                TxtSignInStatus.Text = "✓ " + email;
                TxtStatus.Text = "Loading your backups...";
                TxtNoBackups.Visibility = Visibility.Collapsed;

                // Load backup list — no master key needed
                var backups = await _manager.GetBackupsAsync();
                LstBackups.ItemsSource = backups;
                LstBackups.IsEnabled = backups.Count > 0;

                if (backups.Count == 0)
                {
                    TxtNoBackups.Visibility = Visibility.Visible;
                    TxtNoBackups.Text = "No backups found for this Google account.";
                    TxtStatus.Text = "";
                }
                else
                {
                    LstBackups.SelectedIndex = 0; // select latest
                    BtnRestore.IsEnabled = true;
                    TxtStatus.Text = backups.Count + " backup(s) found. Select one and click Restore.";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Sign-in failed: " + ex.Message;
                BtnSignIn.IsEnabled = true;
            }
        }

        // ── Step 2: Restore ───────────────────────────────────────────────────

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!(LstBackups.SelectedItem is BackupInfo selected))
            {
                MessageBox.Show("Please select a backup from the list.",
                    "Select Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Restore backup from:\n" + selected.DisplayName + "\n\n" +
                "This will replace your current local data. Continue?",
                "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            BtnRestore.IsEnabled = false;
            BtnSignIn.IsEnabled = false;
            PbRestore.Visibility = Visibility.Visible;

            var progress = new Progress<int>(v =>
            {
                PbRestore.Value = v;
                TxtStatus.Text =
                    v < 40 ? "Downloading from Google Drive..." :
                    v < 75 ? "Decrypting your data..." :
                    v < 100 ? "Writing database..." :
                              "Done!";
            });

            try
            {
                // No master key parameter — email-derived key used automatically
                await _manager.RestoreAsync(selected.FileId, progress);

                MessageBox.Show(
                    "✅ Restore complete!\n\nAll your data has been recovered.\nThe app will now restart.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Restart app to load the restored DB
                string? exePath = Environment.ProcessPath;
                if (exePath != null)
                    System.Diagnostics.Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Restore failed:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                BtnRestore.IsEnabled = true;
                BtnSignIn.IsEnabled = true;
                PbRestore.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "Restore failed. Check your internet and try again.";
            }
        }
    }
}