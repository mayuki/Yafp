namespace Yafp;

public class YafpOptions
{
    public string? ListenAddress { get; set; }
    public int ListenPort { get; set; } = 18080;
    public YafpCertificateOptions? RootCertificate { get; set; }
}

public class YafpCertificateOptions
{
    /// <summary>
    /// .pfx or .pem/.crt
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// .key
    /// </summary>
    public string? KeyPath { get; set; }

    public string? Password { get; set; }
}