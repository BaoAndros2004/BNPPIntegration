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
            var client = new SftpClient(conn);
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

                    e.CanTrust = string.Equals(expectedClean, actualClean, StringComparison.OrdinalIgnoreCase);
                    if (!e.CanTrust)
                    {
                        _logger.LogCritical("SFTP Host Key Mismatch! Expected: {Expected}, Actual from server: {Actual}", expectedFingerprint, actualFingerprint);
                    }
                }
                else
                {
                    // Nếu chưa cấu hình fingerprint, tin cậy tạm thời và ghi log cảnh báo để người dùng copy vào appsettings
                    _logger.LogWarning("SFTP Host Key Verification is not configured! Server SHA-256 Fingerprint is: {Actual}. Please set 'Sftp:ServerFingerprintSha256' in appsettings.json for security.", actualFingerprint);
                    e.CanTrust = true;
                }
            };

            return client;
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                _logger.LogWarning("SFTP service is disabled in configuration.");
                return false;
            }

            try
            {
                using var client = CreateSftpClient();
                await Task.Run(() => client.Connect(), cancellationToken);
                var connected = client.IsConnected;
                client.Disconnect();
                _logger.LogInformation("SFTP Connection Test Succeeded! Host: {Host}:{Port}", client.ConnectionInfo.Host, client.ConnectionInfo.Port);
                return connected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SFTP Connection Test Failed!");
                return false;
            }
        }

        public async Task UploadPaymentFileAsync(string localPgpFilePath, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                _logger.LogInformation("SFTP is disabled; skipping upload for {FilePath}", localPgpFilePath);
                return;
            }

            var remoteDir = _configuration["Sftp:RemoteIncomingPath"] ?? "/incoming";
            var fileName = Path.GetFileName(localPgpFilePath);
            var remoteFilePath = $"{remoteDir.TrimEnd('/')}/{fileName}";

            using var client = CreateSftpClient();

            _logger.LogInformation("Connecting to SFTP {Host}:{Port} to upload {FileName}...", client.ConnectionInfo.Host, client.ConnectionInfo.Port, fileName);
            await Task.Run(() => client.Connect(), cancellationToken);

            await using var stream = File.OpenRead(localPgpFilePath);
            await Task.Run(() => client.UploadFile(stream, remoteFilePath, true), cancellationToken);

            client.Disconnect();
            _logger.LogInformation("Successfully uploaded {FileName} to BNP SFTP at {RemotePath}", fileName, remoteFilePath);
        }

        public async Task<IReadOnlyList<string>> DownloadReportsAsync(string localDirectory, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                return Array.Empty<string>();
            }

            var remoteDir = _configuration["Sftp:RemoteOutgoingPath"] ?? "/outgoing";
            Directory.CreateDirectory(localDirectory);
            var downloadedFiles = new List<string>();

            using var client = CreateSftpClient();

            await Task.Run(() => client.Connect(), cancellationToken);

            if (!client.Exists(remoteDir))
            {
                _logger.LogWarning("Remote directory {RemoteDir} does not exist on SFTP server.", remoteDir);
                client.Disconnect();
                return downloadedFiles;
            }

            var files = await Task.Run(() => client.ListDirectory(remoteDir), cancellationToken);
            foreach (var file in files)
            {
                if (file.IsDirectory || file.Name.StartsWith('.'))
                    continue;

                var localFilePath = Path.Combine(localDirectory, file.Name);
                await using (var stream = File.Create(localFilePath))
                {
                    await Task.Run(() => client.DownloadFile(file.FullName, stream), cancellationToken);
                }

                // Xóa file trên SFTP server sau khi đã tải thành công về local
                try
                {
                    await Task.Run(() => client.DeleteFile(file.FullName), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Downloaded {FileName} but could not delete it from remote server.", file.Name);
                }

                downloadedFiles.Add(localFilePath);
                _logger.LogInformation("Successfully downloaded bank report: {FileName}", file.Name);
            }

            client.Disconnect();
            return downloadedFiles;
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

            return fullPath;
        }
    }
}

