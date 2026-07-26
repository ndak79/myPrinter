using System.Security.Cryptography;
using System.Text.Json;

namespace MyPrinter.Desktop.Activation;

internal interface IMachineDataProtector
{
    byte[] Protect(byte[] plaintext, byte[] entropy);
    byte[] Unprotect(byte[] ciphertext, byte[] entropy);
}

internal sealed class DpapiMachineDataProtector : IMachineDataProtector
{
    public byte[] Protect(byte[] plaintext, byte[] entropy)
        => ProtectedData.Protect(plaintext, entropy, DataProtectionScope.LocalMachine);

    public byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        => ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.LocalMachine);
}

internal sealed class MachineLicenseStore
{
    private const int EnvelopeVersion = 1;
    private const string MutexName = @"Global\myPrinter.MachineLicenseStore";
    private static readonly byte[] Entropy = SHA256.HashData("myPrinter.machine-license.v1"u8);

    private readonly string _path;
    private readonly IMachineDataProtector _protector;

    internal MachineLicenseStore(string directory, IMachineDataProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(protector);

        _path = Path.Combine(directory, "license.dat");
        _protector = protector;
    }

    internal static MachineLicenseStore CreateDefault()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return new MachineLicenseStore(Path.Combine(commonData, "myPrinter"), new DpapiMachineDataProtector());
    }

    internal bool Exists()
    {
        try
        {
            return WithLock(() => File.Exists(_path));
        }
        catch
        {
            return true;
        }
    }

    internal LicenseToken? Load()
    {
        try
        {
            return WithLock(() =>
            {
                if (!File.Exists(_path))
                    return null;

                var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(_path));
                if (envelope?.Version != EnvelopeVersion || string.IsNullOrWhiteSpace(envelope.Payload))
                    return null;

                var protectedPayload = Convert.FromBase64String(envelope.Payload);
                var plaintext = _protector.Unprotect(protectedPayload, Entropy);
                return JsonSerializer.Deserialize<LicenseToken>(plaintext);
            });
        }
        catch
        {
            return null;
        }
    }

    internal bool TrySave(LicenseToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        try
        {
            return WithLock(() =>
            {
                var plaintext = JsonSerializer.SerializeToUtf8Bytes(token);
                var envelope = new Envelope
                {
                    Version = EnvelopeVersion,
                    Payload = Convert.ToBase64String(_protector.Protect(plaintext, Entropy))
                };

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tempPath = _path + "." + Path.GetRandomFileName() + ".tmp";
                try
                {
                    File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(envelope));
                    File.Move(tempPath, _path, overwrite: true);
                    return true;
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            });
        }
        catch
        {
            return false;
        }
    }

    internal void Delete()
    {
        try { WithLock(() => { File.Delete(_path); return true; }); } catch { }
    }

    private static T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, MutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            if (!acquired)
                throw new TimeoutException("Timed out waiting for the machine license store.");
            return action();
        }
        catch (AbandonedMutexException)
        {
            return action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    private sealed class Envelope
    {
        public int Version { get; init; }
        public string Payload { get; init; } = "";
    }
}
