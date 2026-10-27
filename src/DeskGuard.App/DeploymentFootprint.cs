namespace DeskGuard.App;

using System;
using System.IO;

internal static class DeploymentFootprint
{
    public const string DisplayName = "DeskGuard for Microsoft Outlook";
    public const string AppExeName = "DeskGuard.exe";
    public const string Publisher = "DeskGuard";
    public const string AppDirectoryName = "DeskGuard";
    public const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DeskGuard";
    public const string AppRegistryKeyPath = @"SOFTWARE\DeskGuard";

    public static readonly string ProgramFilesInstallFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        AppDirectoryName);

    public static readonly string PerUserInstallFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        AppDirectoryName);

    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDirectoryName);

    public static readonly string LogPath = Path.Combine(DataFolder, "install.log");

    public static string GetDefaultInstallFolder() => ProgramFilesInstallFolder;

    public static string GetInstallExePath(string installFolder) => Path.Combine(installFolder, AppExeName);
}
