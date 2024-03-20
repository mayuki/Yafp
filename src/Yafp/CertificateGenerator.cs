using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace Yafp;

public static class CertificateGenerator
{
    private const int RSAMinimumKeySizeInBits = 2048;

    private const string ServerAuthenticationEnhancedKeyUsageOid = "1.3.6.1.5.5.7.3.1";
    private const string ServerAuthenticationEnhancedKeyUsageOidFriendlyName = "Server Authentication";

    public static X509Certificate2 CreateCertificateForHost(X509Certificate2 rootCACert, string host)
    {
        // dotnet dev-cert
        // https://github.com/dotnet/aspnetcore/blob/main/src/Shared/CertificateGeneration/CertificateManager.cs
        var subject = new X500DistinguishedName("CN=" + host);
        var extensions = new List<X509Extension>();
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(host);

        var keyUsage = new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature, critical: true);
        var enhancedKeyUsage = new X509EnhancedKeyUsageExtension(
            new OidCollection() {
            new Oid(
                ServerAuthenticationEnhancedKeyUsageOid,
                ServerAuthenticationEnhancedKeyUsageOidFriendlyName)
            },
            critical: true);

        var basicConstraints = new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true);

        extensions.Add(basicConstraints);
        extensions.Add(keyUsage);
        extensions.Add(enhancedKeyUsage);
        extensions.Add(sanBuilder.Build(critical: true));

        var certificate = CreateCertificate(rootCACert, subject, extensions);
        return certificate;
    }

    public static X509Certificate2 CreateCertificate(X509Certificate2 rootCACert, X500DistinguishedName subject, IEnumerable<X509Extension> extensions)
    {
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        using var key = CreateKeyMaterial(RSAMinimumKeySizeInBits);

        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        foreach (var extension in extensions)
        {
            request.CertificateExtensions.Add(extension);
        }

        var serial = new byte[20];
        RandomNumberGenerator.Fill(serial);
        var result = request.Create(rootCACert, notBefore, notAfter, serial);
        //var result = request.CreateSelfSigned(notBefore, notAfter);
        result = result.CopyWithPrivateKey(key);
        return result;

        static RSA CreateKeyMaterial(int minimumKeySize)
        {
            var rsa = RSA.Create(minimumKeySize);
            if (rsa.KeySize < minimumKeySize)
            {
                throw new InvalidOperationException($"Failed to create a key with a size of {minimumKeySize} bits");
            }

            return rsa;
        }
    }
}
