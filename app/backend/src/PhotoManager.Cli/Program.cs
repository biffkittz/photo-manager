using System.CommandLine;
using System.Text;
using System.Text.Json;
using PhotoManager.Core.Crypto;

// photo-crypt: offline recovery tool. With only the archive password (and, for web-key data, the web key) it
// can read every file the system has ever written, so the archive never depends on the app being deployed.
//   photo-crypt inspect  <file>
//   photo-crypt decrypt  <input> <output>
//   photo-crypt metadata <file>
//   photo-crypt encrypt  <input> <output> [--legacy]
// Secrets come from PHOTO_CRYPT_PASSWORD / PHOTO_CRYPT_WEB_KEY or an interactive prompt; never from arguments
// (arguments end up in shell history and process listings).
var input = new Argument<FileInfo>("input") { Description = "Input file" };
var output = new Argument<FileInfo>("output") { Description = "Output file (must not exist)" };
var legacy = new Option<bool>("--legacy") { Description = "Write the protect.ps1 format instead of v2" };

var inspect = new Command("inspect", "Show the format and header of an encrypted file") { input };
inspect.SetAction(async (parse, ct) => await Commands.InspectAsync(parse.GetValue(input)!, ct));

var decrypt = new Command("decrypt", "Decrypt a legacy (protect.ps1) or v2 file") { input, output };
decrypt.SetAction(async (parse, ct) => await Commands.DecryptAsync(parse.GetValue(input)!, parse.GetValue(output)!, ct));

var metadata = new Command("metadata", "Print the metadata sealed in a v2 archive file") { input };
metadata.SetAction(async (parse, ct) => await Commands.MetadataAsync(parse.GetValue(input)!, ct));

var encrypt = new Command("encrypt", "Encrypt a file with the archive password") { input, output, legacy };
encrypt.SetAction(async (parse, ct) => await Commands.EncryptAsync(parse.GetValue(input)!, parse.GetValue(output)!, parse.GetValue(legacy), ct));

var root = new RootCommand("photo-crypt: inspect, decrypt and encrypt Photo Manager archive files") { inspect, decrypt, metadata, encrypt };
return await root.Parse(args).InvokeAsync();

/// <summary>Command implementations.</summary>
internal static class Commands
{
    private const int DefaultIterations = 600_000;
    private static readonly byte[] DefaultSalt = Convert.FromBase64String("O1zP6nn3EyFZaMO35BZ02g==");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task InspectAsync(FileInfo file, CancellationToken ct)
    {
        var prefix = new byte[PhotoCipherV2.HeaderSize];
        await using var stream = file.OpenRead();
        var read = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, ct);
        if (!PhotoCipherV2.HasV2Magic(prefix.AsSpan(0, read)))
        {
            Console.WriteLine($"Format: legacy protect.ps1 (AES-256-CBC, PBKDF2-SHA1 x{LegacyPhotoCipher.Pbkdf2Iterations}); size {file.Length} bytes");
            return;
        }

        if (read < PhotoCipherV2.HeaderSize)
        {
            Console.WriteLine($"Format: v2, but the file is truncated: only {read} of the {PhotoCipherV2.HeaderSize} header bytes are present.");
            return;
        }

        var header = V2HeaderView.Parse(prefix);
        Console.WriteLine($"Format: v2; key type {header.KeyType}; key id {header.KeyId}; chunk {header.ChunkSize} B; KDF iterations {header.Iterations}; metadata {(header.MetadataLength > 0 ? "present" : "none")}");
    }

    public static async Task DecryptAsync(FileInfo source, FileInfo destination, CancellationToken ct)
    {
        EnsureNew(destination);
        var isV2 = await IsV2Async(source, ct);
        await WriteOrDeleteAsync(destination, async output =>
        {
            await using var input = source.OpenRead();
            if (isV2)
            {
                using var ring = new PhotoKeyRing(OptionalPassword(), WebKeys(), DefaultSalt, DefaultIterations);
                await PhotoCipherV2.DecryptAsync(input, output, ring, ct);
            }
            else
            {
                await using var plaintext = await LegacyPhotoCipher.OpenDecryptStreamAsync(input, RequirePassword(), ct);
                await plaintext.CopyToAsync(output, ct);
            }
        });

        Console.WriteLine($"Decrypted to {destination.FullName}");
    }

    public static async Task MetadataAsync(FileInfo source, CancellationToken ct)
    {
        await using var input = source.OpenRead();
        using var ring = new PhotoKeyRing(OptionalPassword(), WebKeys(), DefaultSalt, DefaultIterations);
        var metadata = await PhotoCipherV2.ReadMetadataAsync(input, ring, cancellationToken: ct);
        Console.WriteLine(metadata is null ? "(no metadata)" : JsonSerializer.Serialize(metadata, Indented));
    }

    public static async Task EncryptAsync(FileInfo source, FileInfo destination, bool legacy, CancellationToken ct)
    {
        EnsureNew(destination);
        var password = RequirePassword();
        await WriteOrDeleteAsync(destination, async output =>
        {
            await using var input = source.OpenRead();
            if (legacy)
            {
                await LegacyPhotoCipher.EncryptAsync(input, output, password, ct);
            }
            else
            {
                using var ring = new PhotoKeyRing(password, [], DefaultSalt, DefaultIterations);
                await PhotoCipherV2.EncryptAsync(input, output, ring.ActiveArchiveKey, cancellationToken: ct);
            }
        });

        Console.WriteLine($"Encrypted to {destination.FullName}");
    }

    // A failed decryption (wrong password, corrupted or truncated file) must not leave partial, unauthenticated
    // output behind that could be mistaken for a recovered photo.
    private static async Task WriteOrDeleteAsync(FileInfo destination, Func<Stream, Task> write)
    {
        // Created outside the try: if creation fails (the file appeared meanwhile) there is nothing of ours to delete.
        var output = new FileStream(destination.FullName, FileMode.CreateNew, FileAccess.Write);
        try
        {
            await using (output)
            {
                await write(output);
            }
        }
        catch
        {
            File.Delete(destination.FullName);
            throw;
        }
    }

    private static async Task<bool> IsV2Async(FileInfo file, CancellationToken ct)
    {
        var prefix = new byte[8];
        await using var stream = file.OpenRead();
        var read = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, ct);
        return PhotoCipherV2.HasV2Magic(prefix.AsSpan(0, read));
    }

    private static void EnsureNew(FileInfo destination)
    {
        if (destination.Exists)
        {
            throw new IOException($"{destination.FullName} already exists; refusing to overwrite.");
        }
    }

    private static string? OptionalPassword() =>
        Environment.GetEnvironmentVariable("PHOTO_CRYPT_PASSWORD") is { Length: > 0 } fromEnv ? fromEnv
            : Environment.GetEnvironmentVariable("PHOTO_CRYPT_WEB_KEY") is { Length: > 0 } ? null
            : Prompt("Archive password: ");

    private static string RequirePassword() => OptionalPassword() ?? Prompt("Archive password: ");

    private static byte[][] WebKeys() =>
        Environment.GetEnvironmentVariable("PHOTO_CRYPT_WEB_KEY") is { Length: > 0 } key ? [Convert.FromBase64String(key)] : [];

    private static string Prompt(string label)
    {
        Console.Error.Write(label);
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && builder.Length > 0)
            {
                builder.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }
}

/// <summary>Read-only view of a v2 header for display (the parser itself is internal to Core).</summary>
internal sealed record V2HeaderView(string KeyType, string KeyId, int ChunkSize, int Iterations, int MetadataLength)
{
    public static V2HeaderView Parse(ReadOnlySpan<byte> header) => new(
        ((V2KeyType)header[9]).ToString(),
        Convert.ToHexStringLower(header.Slice(20, 8)),
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[12..]),
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]),
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[76..]));
}
