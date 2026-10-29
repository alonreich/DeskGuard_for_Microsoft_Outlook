namespace DeskGuard.Core.Security;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static partial class AuthenticodeVerifier
{
    private const string WINTRUST_ACTION_GENERIC_VERIFY_V2 = "{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}";
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x00000100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

    public static bool VerifyManifestSignature(string manifestPath, string signaturePath)
    {
        try
        {
            if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            {
                return false;
            }

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] signatureBytes = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());

            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(SigningConstants.ManifestPublicKeyBytes, out _);
            return rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthenticodeVerifier] Manifest signature verification failed: {ex.Message}");
            return false;
        }
    }

    public static bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            // First check using X509Certificate2
            try
            {
                using X509Certificate2 cert = X509CertificateLoader.LoadCertificateFromFile(filePath);
                if (cert != null)
                {
                    return true;
                }
            }
            catch
            {
                // Fallback to WinVerifyTrust API
            }

            Guid actionGuid = new(WINTRUST_ACTION_GENERIC_VERIFY_V2);

            WINTRUST_FILE_INFO fileInfo = new()
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = Marshal.StringToHGlobalUni(filePath),
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            WINTRUST_DATA trustData = new()
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFileInfo,
                dwStateAction = WTD_STATEACTION_IGNORE,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_SAFER_FLAG,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };

            IntPtr pTrustData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(trustData, pTrustData, false);

            try
            {
                int result = WinVerifyTrust(IntPtr.Zero, ref actionGuid, pTrustData);
                // 0 means verified trusted signature; CERT_E_UNTRUSTEDROOT (0x800B0109 / -2146762487) means signed but self-signed root
                return result == 0 || result == unchecked((int)0x800B0109);
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfo.pcwszFilePath);
                Marshal.FreeHGlobal(pFileInfo);
                Marshal.FreeHGlobal(pTrustData);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthenticodeVerifier] WinVerifyTrust check error: {ex.Message}");
            return false;
        }
    }
}
