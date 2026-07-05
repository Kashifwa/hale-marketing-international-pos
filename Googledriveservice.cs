using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Hale_Marketing_International.Security;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;
using IOFileStream = System.IO.FileStream;
using IOFileMode = System.IO.FileMode;
using IOFileAccess = System.IO.FileAccess;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Hale_Marketing_International.Backup
{
    public class GoogleDriveService
    {
        private DriveService? _drive;
        private string _userEmail = string.Empty;
        private const string FolderName = "HaleMarketingPOS_Backups";

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>
        /// Signs in with Google. Token is cached per-device — silent on next launch.
        /// After auth, _userEmail is populated from the credential.
        /// </summary>
        public async Task<string> AuthenticateAsync()
        {
            string credPath = IOPath.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "credentials.json");

            if (!IOFile.Exists(credPath))
                throw new System.IO.FileNotFoundException(
                    "credentials.json not found next to your .exe.", credPath);

            string[] scopes = { DriveService.Scope.DriveFile,
                                 "https://www.googleapis.com/auth/userinfo.email",
                                 "openid" };

            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromFile(credPath).Secrets,
                scopes,
                "pos_user",
                CancellationToken.None,
                new FileDataStore(AppSettings.GoogleTokenFolder, true)
            );

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "HaleMarketingPOS"
            });

            // Get the signed-in email from Google
            _userEmail = await GetSignedInEmailAsync();

            // Persist to settings so we can show it in the UI
            AppSettings.Instance.ConnectedGoogleEmail = _userEmail;
            AppSettings.Instance.GoogleDriveConfigured = true;
            AppSettings.Instance.Save();

            return _userEmail;
        }

        /// <summary>
        /// Silent re-auth using cached token. Returns email or empty if not cached.
        /// </summary>
        public async Task<string> TryAuthenticateSilentlyAsync()
        {
            try
            {
                if (!HasCachedToken()) return string.Empty;
                return await AuthenticateAsync();
            }
            catch { return string.Empty; }
        }

        public bool HasCachedToken()
        {
            string folder = AppSettings.GoogleTokenFolder;
            return IODirectory.Exists(folder) &&
                   IODirectory.GetFiles(folder, "*.TokenResponse-user").Length > 0;
        }

        private async Task<string> GetSignedInEmailAsync()
        {
            try
            {
                var oauth2Service = new Google.Apis.Oauth2.v2.Oauth2Service(
                    new BaseClientService.Initializer
                    {
                        HttpClientInitializer = _drive!.HttpClientInitializer,
                        ApplicationName = "HaleMarketingPOS"
                    });
                var userInfo = await oauth2Service.Userinfo.Get().ExecuteAsync();
                return userInfo.Email ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// Encrypts DB using the signed-in Google email as key, uploads to Drive.
        /// </summary>
        public async Task<string> UploadBackupAsync(IProgress<int>? progress = null)
        {
            EnsureAuthenticated();

            string dbPath = AppSettings.DatabasePath;
            string tempFile = IOPath.GetTempFileName();

            try
            {
                // 1. Encrypt using email-derived key (no manual master key)
                BackupEncryption.EncryptFile(dbPath, tempFile, _userEmail);
                progress?.Report(40);

                // 2. Get or create Drive folder
                string folderId = await GetOrCreateFolderAsync();
                progress?.Report(60);

                // 3. Upload
                string fileName = "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".posdb.enc";
                var metadata = new DriveFile
                {
                    Name = fileName,
                    Parents = new[] { folderId }
                };

                using (var stream = new IOFileStream(tempFile, IOFileMode.Open, IOFileAccess.Read))
                {
                    var request = _drive!.Files.Create(metadata, stream, "application/octet-stream");
                    request.Fields = "id, name, createdTime";
                    await request.UploadAsync();

                    string fileId = request.ResponseBody?.Id
                        ?? throw new Exception("Upload succeeded but no file ID returned.");

                    progress?.Report(100);

                    AppSettings.Instance.LastBackupTime = DateTime.Now;
                    AppSettings.Instance.Save();

                    return fileId;
                }
            }
            finally
            {
                if (IOFile.Exists(tempFile)) IOFile.Delete(tempFile);
            }
        }

        // ── List backups ──────────────────────────────────────────────────────

        public async Task<List<BackupInfo>> ListBackupsAsync()
        {
            EnsureAuthenticated();
            string folderId = await GetOrCreateFolderAsync();

            var list = _drive!.Files.List();
            list.Q = "'" + folderId + "' in parents and trashed = false";
            list.Fields = "files(id, name, createdTime, size)";
            list.OrderBy = "createdTime desc";

            var result = await list.ExecuteAsync();
            return result.Files.Select(f => new BackupInfo
            {
                FileId = f.Id,
                FileName = f.Name,
                CreatedTime = f.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.MinValue,
                SizeBytes = f.Size ?? 0
            }).ToList();
        }

        // ── Restore ───────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads and decrypts a backup using the signed-in Google email as key.
        /// On a new device: authenticate first, then call this.
        /// </summary>
        public async Task RestoreBackupAsync(string fileId, IProgress<int>? progress = null)
        {
            EnsureAuthenticated();

            string tempEncrypted = IOPath.GetTempFileName();
            string tempDecrypted = IOPath.GetTempFileName();

            try
            {
                // 1. Download
                var request = _drive!.Files.Get(fileId);
                using (var fs = new IOFileStream(tempEncrypted, IOFileMode.Create, IOFileAccess.Write))
                    await request.DownloadAsync(fs);
                progress?.Report(40);

                // 2. Decrypt using email-derived key
                BackupEncryption.DecryptFile(tempEncrypted, tempDecrypted, _userEmail);
                progress?.Report(75);

                // 3. Replace local DB
                string dbPath = AppSettings.DatabasePath;
                IODirectory.CreateDirectory(IOPath.GetDirectoryName(dbPath)!);
                IOFile.Copy(tempDecrypted, dbPath, overwrite: true);
                progress?.Report(100);

                AppSettings.Instance.LastBackupTime = DateTime.Now;
                AppSettings.Instance.Save();
            }
            finally
            {
                if (IOFile.Exists(tempEncrypted)) IOFile.Delete(tempEncrypted);
                if (IOFile.Exists(tempDecrypted)) IOFile.Delete(tempDecrypted);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<string> GetOrCreateFolderAsync()
        {
            var q = _drive!.Files.List();
            q.Q = "mimeType='application/vnd.google-apps.folder' and name='" + FolderName + "' and trashed=false";
            q.Fields = "files(id)";
            var res = await q.ExecuteAsync();

            if (res.Files.Count > 0) return res.Files[0].Id;

            var folder = new DriveFile
            {
                Name = FolderName,
                MimeType = "application/vnd.google-apps.folder"
            };
            var created = await _drive.Files.Create(folder).ExecuteAsync();
            return created.Id;
        }

        private void EnsureAuthenticated()
        {
            if (_drive == null || string.IsNullOrEmpty(_userEmail))
                throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync() first.");
        }

        public string SignedInEmail => _userEmail;
    }

    public class BackupInfo
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; }
        public long SizeBytes { get; set; }

        public string DisplayName =>
            CreatedTime.ToString("dd MMM yyyy  HH:mm") +
            "  —  " + (SizeBytes / 1024.0).ToString("F1") + " KB";
    }
}