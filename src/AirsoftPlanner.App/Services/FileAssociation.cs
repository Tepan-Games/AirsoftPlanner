using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>
/// Associe l'extension .aop au logiciel pour l'utilisateur Windows courant (sans droits administrateur) :
/// un double-clic sur un fichier d'OP l'ouvre dans Airsoft Planner. Effectué uniquement à la demande de l'utilisateur.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileAssociation
{
    private const string ProgId = "AirsoftPlanner.Operation";

    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("emplacement_du_programme_introuvable"));
        using (var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.aop"))
            extension.SetValue("", ProgId);
        using (var type = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            type.SetValue("", L.T("fichier_d_op_airsoft_planner"));
            using (var icon = type.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");
            using (var command = type.CreateSubKey(@"shell\open\command"))
                command.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // Prévient l'Explorateur pour que les icônes et l'action d'ouverture soient prises en compte tout de suite.
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
