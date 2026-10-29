namespace DeskGuard.Build;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal static class Signing
{
    private static readonly string CertPfxPath = Path.Combine(Environment.CurrentDirectory, "ssl-certificate", "DeskGuard_CodeSigning.pfx");
    private const string CertPasswordDefault = "DeskGuard_LocalDev_SecuredKey_2026!";

    public static string? FindSignTool()
    {
        string? onPath = Cli.FindOnPath("signtool.exe");
        if (!string.IsNullOrEmpty(onPath)) return onPath;

        string winKits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (Directory.Exists(winKits))
        {
            // Pick highest version
            var subdirs = Directory.GetDirectories(winKits);
            Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(subdirs);
            foreach (var dir in subdirs)
            {
                string x64Candidate = Path.Combine(dir, "x64", "signtool.exe");
                if (File.Exists(x64Candidate)) return x64Candidate;
            }
        }

        return null;
    }

    public static bool SignBinary(string exePath, BuildLog log)
    {
        if (!File.Exists(exePath))
        {
            log.Error($"[Signer] Target binary not found: {exePath}");
            return false;
        }

        if (!File.Exists(CertPfxPath))
        {
            log.Warn($"[Signer] Code signing certificate not found at {CertPfxPath}. Attempting auto-generation...");
            int genExit = Cli.RunStreaming("powershell", ["-ExecutionPolicy", "Bypass", "-File", "ssl-certificate\\Generate-Certificate.ps1"], log);
            if (genExit != 0 || !File.Exists(CertPfxPath))
            {
                log.Warn("[Signer] Could not generate certificate. Skipping binary Authenticode signing.");
                return false;
            }
        }

        string? signtool = FindSignTool();
        if (string.IsNullOrEmpty(signtool))
        {
            log.Warn("[Signer] signtool.exe was not found in Windows Kits. Skipping binary Authenticode signing.");
            return false;
        }

        string password = Environment.GetEnvironmentVariable("DESKGUARD_CERT_PASS") ?? CertPasswordDefault;
        log.Info($"[Signer] Signing '{Path.GetFileName(exePath)}' with Authenticode certificate via SignTool...");

        // Try signing with timestamp first, fall back to timestamp-free if offline
        int exit = Cli.RunStreaming(signtool, [
            "sign",
            "/f", CertPfxPath,
            "/p", password,
            "/fd", "SHA256",
            "/tr", "http://timestamp.digicert.com",
            "/td", "SHA256",
            exePath
        ], log);

        if (exit != 0)
        {
            log.Warn("[Signer] Timestamp server unavailable; signing without timestamp...");
            exit = Cli.RunStreaming(signtool, [
                "sign",
                "/f", CertPfxPath,
                "/p", password,
                "/fd", "SHA256",
                exePath
            ], log);
        }

        if (exit == 0)
        {
            log.Success($"[Signer] Successfully signed '{Path.GetFileName(exePath)}'.");
            return true;
        }

        log.Warn($"[Signer] SignTool returned non-zero exit code: {exit}");
        return false;
    }

    public static bool SignManifest(string manifestPath, string signaturePath, BuildLog log)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                log.Error($"[Signer] Manifest file missing: {manifestPath}");
                return false;
            }

            if (!File.Exists(CertPfxPath))
            {
                log.Warn($"[Signer] Code signing certificate not found at {CertPfxPath}.");
                return false;
            }

            string password = Environment.GetEnvironmentVariable("DESKGUARD_CERT_PASS") ?? CertPasswordDefault;
            using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(CertPfxPath, password, X509KeyStorageFlags.Exportable);
            using RSA? rsa = cert.GetRSAPrivateKey();
            if (rsa == null)
            {
                log.Error("[Signer] Certificate does not contain an RSA private key.");
                return false;
            }

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] signatureBytes = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            File.WriteAllText(signaturePath, Convert.ToBase64String(signatureBytes));
            log.Success($"[Signer] Manifest signed with RSA-SHA256: '{Path.GetFileName(signaturePath)}'.");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"[Signer] Exception while signing manifest: {ex.Message}");
            return false;
        }
    }
}
