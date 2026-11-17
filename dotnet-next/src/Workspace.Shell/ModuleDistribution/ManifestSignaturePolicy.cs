using System;
using System.Security.Cryptography;

namespace Workspace.Shell.ModuleDistribution;

/// <summary>
/// Fixed product trust anchor supplied by compiled host code, never from a manifest or network feed.
/// Verifies raw detached RSA-3072/PSS/SHA-256 signatures over exact original manifest bytes.
/// This authenticates possession of the product signing key, not code harmlessness or publisher identity.
/// </summary>
public sealed class ManifestSignaturePolicy
{
    public const int SignatureBytes = 384;
    private readonly byte[] publicKey;
    public ManifestSignaturePolicy(string publicSpkiPem)
    {
        if (string.IsNullOrWhiteSpace(publicSpkiPem) || !publicSpkiPem.TrimStart().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) || publicSpkiPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new DistributionException("A product-embedded RSA-3072 public SPKI key is required.");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicSpkiPem);
            if (rsa.KeySize != 3072) throw new DistributionException("Manifest signing key must be RSA-3072.");
            publicKey = rsa.ExportSubjectPublicKeyInfo();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new DistributionException("Invalid manifest verification public key.", ex);
        }
    }
    public void Verify(ReadOnlySpan<byte> exactManifestBytes, ReadOnlySpan<byte> detachedSignature)
    {
        if (detachedSignature.Length != SignatureBytes) throw new DistributionException("Missing or malformed mandatory manifest signature.");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (!rsa.VerifyData(exactManifestBytes, detachedSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new DistributionException("Manifest signature is not valid for the embedded product trust key.");
        }
        catch (CryptographicException ex) { throw new DistributionException("Manifest signature verification failed.", ex); }
    }
}
