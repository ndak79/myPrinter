using FluentAssertions;
using MyPrinter.Desktop.Activation;

namespace desktop.Tests;

public class MachineLicenseStoreTests
{
    [Fact]
    public void Machine_license_store_uses_versioned_local_machine_dpapi_storage()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sourcePath = Path.Combine(repoRoot, "desktop", "Activation", "MachineLicenseStore.cs");

        File.Exists(sourcePath).Should().BeTrue("machine-scoped licenses need a dedicated store");

        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("DataProtectionScope.LocalMachine");
        source.Should().Contain("Environment.SpecialFolder.CommonApplicationData");
        source.Should().Contain("EnvelopeVersion");
        source.Should().Contain("Path.GetRandomFileName()");
    }

    [Fact]
    public void Load_rejects_envelope_protected_for_another_machine()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"myprinter-license-{Guid.NewGuid():N}");
        try
        {
            var token = new LicenseToken { Fingerprint = "fp", ProductId = "prod", Expiry = long.MaxValue, Version = 3, Token = "signed" };
            new MachineLicenseStore(directory, new TestProtector("machine-a")).TrySave(token).Should().BeTrue();

            new MachineLicenseStore(directory, new TestProtector("machine-b")).Load().Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private sealed class TestProtector(string machine) : IMachineDataProtector
    {
        public byte[] Protect(byte[] plaintext, byte[] entropy)
            => System.Text.Encoding.UTF8.GetBytes(machine + ":" + Convert.ToBase64String(plaintext));

        public byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        {
            var text = System.Text.Encoding.UTF8.GetString(ciphertext);
            var prefix = machine + ":";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                throw new System.Security.Cryptography.CryptographicException();
            return Convert.FromBase64String(text[prefix.Length..]);
        }
    }
}
