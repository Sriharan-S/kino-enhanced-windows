using System.Security.Cryptography;
using System.Text;

namespace Kino.Core.Identity;

/// <summary>
/// The device secret (32 random bytes, generated once, never sent). Per-screen tokens are derived from it
/// so different screens can't link the same viewer (wiki docs §5).
/// </summary>
public sealed class DeviceIdentity
{
    private readonly byte[] _secret;

    private DeviceIdentity(byte[] secret) => _secret = secret;

    /// <summary>Loads or creates the secret for a profile. Profiles let several instances run on one PC (testing).</summary>
    public static DeviceIdentity LoadOrCreate(string profile = "default")
    {
        string dir = KinoPaths.For(profile);
        string path = Path.Combine(dir, "device.key");

        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length == 32) return new DeviceIdentity(existing);
        }

        Directory.CreateDirectory(dir);
        var secret = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, secret);
        return new DeviceIdentity(secret);
    }

    public static DeviceIdentity FromSecret(byte[] secret) =>
        secret.Length == 32 ? new DeviceIdentity(secret) : throw new ArgumentException("secret must be 32 bytes");

    /// <summary>hex(HMAC-SHA256(deviceSecret, screenId))</summary>
    public string TokenFor(string screenId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(screenId)));
}
