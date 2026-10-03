using System;
using System.Security.Cryptography;
using System.Text;

namespace AirsoftPlanner.App.Services;

/// <summary>
/// Mots de passe enregistrés dans les préférences : chiffrés pour l'utilisateur Windows courant (DPAPI),
/// illisibles depuis un autre compte ou un autre poste.
/// </summary>
public static class Secret
{
    public static string Protect(string value)
    {
        if (value.Length == 0 || !OperatingSystem.IsWindows())
            return value.Length == 0 ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string stored)
    {
        if (stored.Length == 0)
            return "";
        try
        {
            var bytes = Convert.FromBase64String(stored);
            return Encoding.UTF8.GetString(OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)
                : bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return "";
        }
    }
}
