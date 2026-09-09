using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;

namespace BNPPIntegration.BNPP.Security;

public sealed class PgpEncryptionService
{
    private const int BufferSize = 64 * 1024;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PgpEncryptionService> _logger;

    public PgpEncryptionService(
        IConfiguration configuration,
        ILogger<PgpEncryptionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> EncryptAsync(string xmlPath, CancellationToken cancellationToken)
    {
        var fullXmlPath = Path.GetFullPath(xmlPath);
        if (!File.Exists(fullXmlPath))
            throw new FileNotFoundException("The XML file to encrypt was not found.", fullXmlPath);

        var publicKeyPath = ResolvePublicKeyPath();
        if (!File.Exists(publicKeyPath))
            throw new FileNotFoundException("BNP public key was not found.", publicKeyPath);

        var keyInfo = ReadPublicKey(publicKeyPath);

        var pgpPath = fullXmlPath + ".pgp";
        var temporaryPath = pgpPath + ".tmp";

        if (File.Exists(pgpPath))
            throw new IOException($"The encrypted output file already exists: {pgpPath}");

        DeleteIfExists(temporaryPath);

        try
        {
            await EncryptBinaryAsync(fullXmlPath, temporaryPath, keyInfo.EncryptionKey, cancellationToken);

            if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                throw new InvalidOperationException("OpenPGP encryption did not create a valid output file.");

            File.Move(temporaryPath, pgpPath);

            _logger.LogInformation(
                "[PGP] Encrypted binary file: {PgpFileName} (Key: {Fingerprint})",
                Path.GetFileName(pgpPath),
                keyInfo.PrimaryFingerprint);

            return pgpPath;
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            throw;
        }
    }

    private static async Task EncryptBinaryAsync(
        string inputPath,
        string outputPath,
        PgpPublicKey encryptionKey,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
        await using var output = new FileStream(
            outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, true);

        var encryptedData = new PgpEncryptedDataGenerator(
            SymmetricKeyAlgorithmTag.Aes256,
            true,
            new SecureRandom());
        encryptedData.AddMethod(encryptionKey);

        await using var encryptedOutput = encryptedData.Open(output, new byte[BufferSize]);
        var compressedData = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
        await using var compressedOutput = compressedData.Open(encryptedOutput);
        var literalData = new PgpLiteralDataGenerator();
        await using var literalOutput = literalData.Open(
            compressedOutput,
            PgpLiteralData.Binary,
            Path.GetFileName(inputPath),
            input.Length,
            File.GetLastWriteTimeUtc(inputPath));

        await input.CopyToAsync(literalOutput, BufferSize, cancellationToken);
    }

    private static PublicKeyInfo ReadPublicKey(string publicKeyPath)
    {
        try
        {
            using var keyFile = File.OpenRead(publicKeyPath);
            using var decodedKey = PgpUtilities.GetDecoderStream(keyFile);
            var keyRingBundle = new PgpPublicKeyRingBundle(decodedKey);
            var rings = keyRingBundle.GetKeyRings().Cast<PgpPublicKeyRing>().ToArray();

            if (rings.Length != 1)
                throw new InvalidOperationException("The BNP public key file must contain exactly one public-key ring.");

            var primaryKey = rings[0].GetPublicKey();
            var encryptionKey = rings[0]
                .GetPublicKeys()
                .Cast<PgpPublicKey>()
                .FirstOrDefault(key => key.IsEncryptionKey && !key.HasRevocation());

            if (encryptionKey is null)
                throw new InvalidOperationException("The BNP public key has no usable encryption key.");

            var validSeconds = encryptionKey.GetValidSeconds(primaryKey);
            if (validSeconds > 0 &&
                encryptionKey.CreationTime.ToUniversalTime().AddSeconds(validSeconds) <= DateTime.UtcNow)
                throw new InvalidOperationException("The BNP OpenPGP encryption key has expired.");

            return new PublicKeyInfo(
                encryptionKey,
                Hex.ToHexString(primaryKey.GetFingerprint()).ToUpperInvariant());
        }
        catch (PgpException exception)
        {
            throw new InvalidOperationException(
                "The configured BNP public key is not a valid OpenPGP public key.",
                exception);
        }
    }



    private string ResolvePublicKeyPath()
    {
        var configuredDirectory = GetRequiredSetting("Pgp:KeyDirectory");
        var keyType = GetRequiredSetting("Pgp:Type");
        var keyDirectory = Path.IsPathRooted(configuredDirectory)
            ? Path.GetFullPath(configuredDirectory)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredDirectory));

        if (!Directory.Exists(keyDirectory))
        {
            var currentCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredDirectory));
            if (Directory.Exists(currentCandidate))
            {
                keyDirectory = currentCandidate;
            }
            else
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (var i = 0; i < 4 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir.FullName, configuredDirectory);
                    if (Directory.Exists(candidate))
                    {
                        keyDirectory = Path.GetFullPath(candidate);
                        break;
                    }
                    dir = dir.Parent;
                }
            }
        }

        if (!Directory.Exists(keyDirectory))
            throw new DirectoryNotFoundException($"PGP key directory was not found: {keyDirectory}");

        var typeToken = $"-{keyType.Trim()}-";
        var matches = Directory
            .EnumerateFiles(keyDirectory, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Contains(typeToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException(
                $"No PGP public key containing '{typeToken}' was found in {keyDirectory}."),
            _ => throw new InvalidOperationException(
                $"Multiple PGP public keys containing '{typeToken}' were found in {keyDirectory}.")
        };
    }

    private string GetRequiredSetting(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is required.");

        return value;
    }



    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record PublicKeyInfo(PgpPublicKey EncryptionKey, string PrimaryFingerprint);
}
