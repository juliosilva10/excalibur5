using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Excalibur5.Config;

/// <summary>
/// Persists the API token encrypted with DPAPI (user-scope) in %APPDATA%\Excalibur5.
/// </summary>
public static class TokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Excalibur5", "token.dat");

    public static void Save(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            Delete();
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var plain     = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static string Load()
    {
        if (!File.Exists(FilePath)) return string.Empty;
        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var plain     = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
