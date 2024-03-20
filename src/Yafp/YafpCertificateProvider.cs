using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Yafp;

internal class YafpCertificateProvider
{
    private readonly X509Certificate2 _rootCACertificate;
    private readonly ConcurrentDictionary<string, X509Certificate2> _certificatesCache = new(StringComparer.OrdinalIgnoreCase);

    public YafpCertificateProvider(IOptions<YafpOptions> options)
    {
        var rootCertOptions = options.Value.RootCertificate;
        if (rootCertOptions is null) throw new InvalidOperationException("RootCertificate is not configured.");
        if (rootCertOptions.Path is null) throw new InvalidOperationException("RootCertificate.Path is not configured.");

        if (rootCertOptions.KeyPath is null)
        {
            // PFX
            if (rootCertOptions.Password is not null)
            {
                _rootCACertificate = new X509Certificate2(File.ReadAllBytes(rootCertOptions.Path), rootCertOptions.Password);
            }
            else
            {
                _rootCACertificate = new X509Certificate2(File.ReadAllBytes(rootCertOptions.Path));
            }
        }
        else
        {
            // PEM
            if (rootCertOptions.Password is not null)
            {
                _rootCACertificate = X509Certificate2.CreateFromEncryptedPem(File.ReadAllText(rootCertOptions.Path), File.ReadAllText(rootCertOptions.KeyPath), rootCertOptions.Password);
            }
            else
            {
                _rootCACertificate = X509Certificate2.CreateFromPem(File.ReadAllText(rootCertOptions.Path), File.ReadAllText(rootCertOptions.KeyPath));
            }
        }
    }

    public X509Certificate2 GetCertificateForHost(string host)
    {
        return _certificatesCache.GetOrAdd(host, host =>
        {
            var certificate = CertificateGenerator.CreateCertificateForHost(_rootCACertificate, host);
            var export = certificate.Export(X509ContentType.Pkcs12, "");
            certificate.Dispose();
            certificate = new X509Certificate2(export, "", X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            return certificate;
        });
    }
}