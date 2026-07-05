using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hale_Marketing_International.Security;

namespace Hale_Marketing_International.Backup
{
    public class BackupManager
    {
        private readonly GoogleDriveService _drive = new GoogleDriveService();
        private DispatcherTimer? _dailyTimer;

        public string ConnectedEmail => _drive.SignedInEmail;

        // ── Connect Google account (called from Backup & Sync tab) ────────────

        public async Task<string> ConnectGoogleAccountAsync()
        {
            string email = await _drive.AuthenticateAsync();
            return email;
        }

        // ── Silent reconnect on startup (uses cached token) ───────────────────

        public async Task<string> TrySilentConnectAsync()
        {
            return await _drive.TryAuthenticateSilentlyAsync();
        }

        // ── Auto backup on close ──────────────────────────────────────────────

        public async Task BackupOnCloseAsync()
        {
            if (!AppSettings.Instance.GoogleDriveConfigured) return;
            if (!AppSettings.Instance.IsSetupComplete) return;
            try
            {
                if (string.IsNullOrEmpty(_drive.SignedInEmail))
                    await _drive.TryAuthenticateSilentlyAsync();

                if (!string.IsNullOrEmpty(_drive.SignedInEmail))
                    await _drive.UploadBackupAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Backup] Close backup failed: " + ex.Message);
            }
        }

        // ── Daily timer ───────────────────────────────────────────────────────

        public void StartDailyTimer()
        {
            _dailyTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            _dailyTimer.Tick += async (_, _) => await BackupOnCloseAsync();
            _dailyTimer.Start();
        }

        public void StopDailyTimer() => _dailyTimer?.Stop();

        // ── Manual sync ───────────────────────────────────────────────────────

        public async Task<string> SyncNowAsync(IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(_drive.SignedInEmail))
                await _drive.AuthenticateAsync();
            return await _drive.UploadBackupAsync(progress);
        }

        // ── Restore ───────────────────────────────────────────────────────────

        public async Task<List<BackupInfo>> GetBackupsAsync()
        {
            return await _drive.ListBackupsAsync();
        }

        public async Task RestoreAsync(string fileId, IProgress<int>? progress = null)
        {
            await _drive.RestoreBackupAsync(fileId, progress);
        }
    }
}