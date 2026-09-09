using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace BNPPIntegration.BNPP.Security
{
    public sealed class SftpService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SftpService> _logger;

        public SftpService(IConfiguration configuration, ILogger<SftpService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private readonly SemaphoreSlim _sftpLock = new(1, 1);

        public bool IsEnabled => _configuration.GetValue<bool>("Sftp:Enabled", false);

        private ConnectionInfo CreateConnectionInfo()
        {
            var host = GetRequiredSetting("Sftp:Host");
            var port = _configuration.GetValue<int>("Sftp:Port", 22);
            var username = GetRequiredSetting("Sftp:UserName");
            var privateKeyPath = GetRequiredSetting("Sftp:PrivateKeyPath");
            var passphrase = _configuration["Sftp:PrivateKeyPassphrase"];

            var resolvedKeyPath = ResolveFilePath(privateKeyPath);
            if (!File.Exists(resolvedKeyPath))
            {
                throw new FileNotFoundException($"SSH Private Key file was not found at: {resolvedKeyPath}");
            }

            PrivateKeyFile keyFile;
            try
            {
                keyFile = string.IsNullOrWhiteSpace(passphrase)
                    ? new PrivateKeyFile(resolvedKeyPath)
                    : new PrivateKeyFile(resolvedKeyPath, passphrase);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load SSH private key from {resolvedKeyPath}. Check if the file format is a valid PEM key and passphrase is correct.", ex);
            }

            var authMethod = new PrivateKeyAuthenticationMethod(username, keyFile);
            return new ConnectionInfo(host, port, username, authMethod)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private SftpClient CreateSftpClient()
        {
            var conn = CreateConnectionInfo();
            var client = new SftpClient(conn)
            {
                OperationTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            };
            var expectedFingerprint = _configuration["Sftp:ServerFingerprintSha256"];

            client.HostKeyReceived += (_, e) =>
            {
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(e.HostKey);
                var actualFingerprint = "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');

                if (!string.IsNullOrWhiteSpace(expectedFingerprint))
                {
                    var expectedClean = expectedFingerprint.Trim().TrimEnd('=');
                    var actualClean = actualFingerprint.TrimEnd('=');

                    var matched = string.Equals(expectedClean, actualClean, StringComparison.OrdinalIgnoreCase);
                    if (!matched)
                    {
                        _logger.LogWarning("SFTP Host Key Mismatch! Expected: {Expected}, Actual from server: {Actual}", expectedFingerprint, actualFingerprint);
                    }
                }
                e.CanTrust = true;
            };

            return client;
        }

        private async Task<SftpClient> CreateAndConnectClientAsync(CancellationToken cancellationToken, int maxAttempts = 3)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var client = CreateSftpClient();
                try
                {
                    await Task.Run(() => client.Connect(), cancellationToken);
                    _logger.LogInformation("[SFTP] Successfully connected to BNP Paribas SFTP server.");
                    return client;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (client.IsConnected)
                            client.Disconnect();
                        client.Dispose();
                    }
                    catch { }

                    if (attempt >= maxAttempts || cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("[SFTP] Connect failed after {Attempt} attempt(s): {Message}", attempt, ex.Message);
                        throw;
                    }

                    var backoffSeconds = attempt * 5;
                    _logger.LogWarning("[SFTP] Connect attempt {Attempt}/{MaxAttempts} failed: {Message}. Waiting {Delay}s for BNP session release...", attempt, maxAttempts, ex.Message, backoffSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                }
            }

            throw new InvalidOperationException("Failed to establish SFTP connection.");
        }

        public async Task<IReadOnlyList<string>> UploadPaymentFilesBatchAsync(IEnumerable<string> localPgpFilePaths, CancellationToken cancellationToken = default)
        {
            var uploaded = new List<string>();
            var fileList = localPgpFilePaths.Where(File.Exists).ToList();
            if (fileList.Count == 0 || !IsEnabled)
            {
                return uploaded;
            }

            var remoteDir = _configuration["Sftp:RemoteIncomingPath"] ?? "/in";

            await _sftpLock.WaitAsync(cancellationToken);
            try
            {
                using var client = await CreateAndConnectClientAsync(cancellationToken);

                for (var i = 0; i < fileList.Count; i++)
                {
                    var localPgpFilePath = fileList[i];
                    var fileName = Path.GetFileName(localPgpFilePath);
                    var remoteFilePath = $"{remoteDir.TrimEnd('/')}/{fileName}";

                    await using (var stream = File.OpenRead(localPgpFilePath))
                    {
                        await Task.Run(() => client.UploadFile(stream, remoteFilePath, true), cancellationToken);
                    }

                    uploaded.Add(localPgpFilePath);
                    _logger.LogInformation("[SFTP] [{Index}/{Total}] Uploaded {FileName} to {RemotePath}", i + 1, fileList.Count, fileName, remoteFilePath);
                }

                client.Disconnect();
                _logger.LogInformation("[SFTP] Batch upload completed ({Count} file(s)). Disconnected session.", uploaded.Count);
                return uploaded;
            }
            finally
            {
                // Cool-down delay to ensure BNP server completely clears session table
                try { await Task.Delay(3000, CancellationToken.None); } catch { }
                _sftpLock.Release();
            }
        }

        public async Task<IReadOnlyList<string>> DownloadReportsAsync(
            string localDirectory,
            string? archiveDirectory = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                return Array.Empty<string>();
            }

            var remoteDir = _configuration["Sftp:RemoteOutgoingPath"] ?? "/out";
            Directory.CreateDirectory(localDirectory);
            var downloadedFiles = new List<string>();

            // Non-blocking acquire: If PaymentWorker is currently uploading payments, yield to payments
            var acquired = await _sftpLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!acquired)
            {
                _logger.LogInformation("[BANK-REPORT] SFTP is currently busy (payment upload in progress). Skipping report check this cycle.");
                return downloadedFiles;
            }

            try
            {
                using var client = await CreateAndConnectClientAsync(cancellationToken);

                if (!client.Exists(remoteDir))
                {
                    _logger.LogWarning("[SFTP] Remote directory {RemoteDir} does not exist on SFTP server.", remoteDir);
                    client.Disconnect();
                    return downloadedFiles;
                }

                var deleteRemote = _configuration.GetValue<bool>("Sftp:DeleteRemoteAfterDownload", false);
                var archiveDir = !string.IsNullOrWhiteSpace(archiveDirectory)
                    ? archiveDirectory
                    : Path.Combine(localDirectory, "archive");
                Directory.CreateDirectory(archiveDir);

                var files = await Task.Run(() => client.ListDirectory(remoteDir), cancellationToken);
                var remoteFiles = files.Where(f => !f.IsDirectory && !f.Name.StartsWith('.')).ToList();
                if (remoteFiles.Count == 0)
                {
                    _logger.LogInformation("[SFTP] No bank report files found in remote {RemoteDir}. Session closed.", remoteDir);
                    client.Disconnect();
                    return downloadedFiles;
                }

                _logger.LogInformation("[SFTP] Found {Total} file(s) in remote {RemoteDir}. Downloading all to local in batch...", remoteFiles.Count, remoteDir);

                for (var i = 0; i < remoteFiles.Count; i++)
                {
                    var file = remoteFiles[i];
                    var localFilePath = Path.Combine(localDirectory, file.Name);
                    var archivedFilePath = Path.Combine(archiveDir, file.Name);

                    // Skip if file is already downloaded or already processed into archive
                    if (File.Exists(localFilePath) || File.Exists(archivedFilePath))
                    {
                        continue;
                    }

                    await using (var stream = File.Create(localFilePath))
                    {
                        await Task.Run(() => client.DownloadFile(file.FullName, stream), cancellationToken);
                    }

                    if (deleteRemote)
                    {
                        try
                        {
                            await Task.Run(() => client.DeleteFile(file.FullName), cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[SFTP] Downloaded {FileName} but could not delete it from remote server.", file.Name);
                        }
                    }

                    downloadedFiles.Add(localFilePath);
                    _logger.LogInformation("[SFTP] [{Index}/{Total}] Downloaded bank report: {FileName}", i + 1, remoteFiles.Count, file.Name);
                }

                client.Disconnect();
                _logger.LogInformation("[SFTP] Batch download completed. Downloaded {Count} new file(s) from BNP SFTP.", downloadedFiles.Count);
                return downloadedFiles;
            }
            finally
            {
                _sftpLock.Release();
            }
        }

        private string GetRequiredSetting(string key)
        {
            var value = _configuration[key];
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Configuration key '{key}' is required.")
                : value.Trim();
        }

        private static string ResolveFilePath(string path)
        {
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            var baseDir = AppContext.BaseDirectory;
            var fullPath = Path.GetFullPath(Path.Combine(baseDir, path));
            if (File.Exists(fullPath)) return fullPath;

            var currentDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
            if (File.Exists(currentDir)) return currentDir;

            var dir = new DirectoryInfo(baseDir);
            for (var i = 0; i < 4 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, path);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                dir = dir.Parent;
            }

            return fullPath;
        }
    }
}

