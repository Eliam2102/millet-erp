using System.Data.Common;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>Utilidades comunes: conexión por la fábrica PRODUCTIVA de pedidos y confianza temporal del certificado.</summary>
internal static class AwTlsPruebas
{
    /// <summary>Fija el PEM como certificado de las conexiones A+W (como en App Service) y lo suelta al disponer.</summary>
    public static IDisposable ConfiarTemporalmente(string pemPath)
    {
        var dir = Directory.CreateTempSubdirectory("millet-aw-tls-").FullName;
        var r = AwSqlCertificadoServidor.Instalar(File.ReadAllText(pemPath), dir);
        r.Error.Should().BeNull();
        return new Retiro(dir);
    }

    private sealed class Retiro(string dir) : IDisposable
    {
        public void Dispose()
        {
            AwSqlTls.CertificadoServidorRuta = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    public static DbConnection ViaFabricaProductiva(string connectionString)
        => new IntegracionSqlConnectionFactory(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AwIntegracionDb"] = connectionString })
            .Build()).CreateConnection();

    /// <summary>
    /// encrypt_option de la sesión. Los usuarios de solo lectura no tienen VIEW SERVER PERFORMANCE STATE: en ese caso
    /// el cifrado verificado lo prueba que SqlClient abrió con Encrypt=True y cadena validada (y que sin cert/host falla).
    /// </summary>
    public static async Task<string> CifradoAsync(DbConnection c)
    {
        try
        {
            var v = await EscalarAsync(c, "SELECT CONVERT(varchar(10), encrypt_option) FROM sys.dm_exec_connections WHERE session_id = @@SPID");
            v.Should().Be("TRUE");
            return v;
        }
        catch (SqlException ex) when (ex.Number is 300 or 229)
        {
            return "no consultable (sin VIEW SERVER PERFORMANCE STATE); abrió con cadena verificada";
        }
    }

    public static async Task<string> EscalarAsync(DbConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql; // solo SELECT
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}

/// <summary>
/// A+W-QA-4: TLS verificado contra el SER-DATA REAL por VPN (solo lectura, solo MILLET_INTEGRACION_DEV).
/// OPT-IN: requiere SERDATA_VPN_ENVFILE (archivo con SQLCMDUSER/SQLCMDPASSWORD) y SERDATA_CERT_PEM (ruta del PEM público).
/// Nunca imprime credenciales; solo ejecuta SELECT.
/// </summary>
[Trait("Category", "SerDataVpn")]
public class AwSqlTlsSerDataVpnTests(ITestOutputHelper salida)
{
    private const string Servidor = "Server=192.168.1.8,1433;Database=MILLET_INTEGRACION_DEV;Encrypt=True;TrustServerCertificate=False;Pooling=False;Connection Timeout=10;";
    private static readonly string? EnvFile = Environment.GetEnvironmentVariable("SERDATA_VPN_ENVFILE");
    private static readonly string? PemPath = Environment.GetEnvironmentVariable("SERDATA_CERT_PEM");

    private bool Activo
    {
        get
        {
            var ok = !string.IsNullOrEmpty(EnvFile) && !string.IsNullOrEmpty(PemPath);
            if (!ok) salida.WriteLine("OMITIDO: faltan SERDATA_VPN_ENVFILE y SERDATA_CERT_PEM.");
            return ok;
        }
    }

    private static string Credenciales()
    {
        var kv = File.ReadAllLines(EnvFile!).Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim().Trim('"', '\''));
        return $"User ID={kv["SQLCMDUSER"]};Password={kv["SQLCMDPASSWORD"]};";
    }

    [Fact]
    public async Task Sin_certificado_confiado_falla_y_con_el_certificado_verifica_cadena_y_nombre()
    {
        if (!Activo) return;
        var cs = Servidor + Credenciales() + "HostNameInCertificate=aw-sql-onprem;";

        // Sin confiar en el PEM la cadena no valida (UntrustedRoot) — salvo que la máquina ya lo confíe.
        using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadOnly);
            using var c = X509Certificate2.CreateFromPem(File.ReadAllText(PemPath!));
            if (store.Certificates.Find(X509FindType.FindByThumbprint, c.Thumbprint, false).Count == 0)
            {
                await using var sinCert = AwTlsPruebas.ViaFabricaProductiva(cs);
                var ex = await Assert.ThrowsAsync<SqlException>(() => sinCert.OpenAsync());
                salida.WriteLine($"Sin cert confiado: falla como se espera ({ex.GetType().Name}).");
            }
        }

        using var _ = AwTlsPruebas.ConfiarTemporalmente(PemPath!);
        await using var conn = AwTlsPruebas.ViaFabricaProductiva(cs);
        await conn.OpenAsync();

        salida.WriteLine($"encrypt_option: {await AwTlsPruebas.CifradoAsync(conn)}");
        var cab = await AwTlsPruebas.EscalarAsync(conn, "SELECT TOP 1 1 FROM dbo.vw_erp_pedido_cabecera");
        var puente = await AwTlsPruebas.EscalarAsync(conn, "SELECT COUNT(*) FROM dbo.aw_solicitud_pedido");
        salida.WriteLine($"Conectado con TLS verificado (cadena y nombre). vw_erp_pedido_cabecera top1={cab}; aw_solicitud_pedido filas={puente}.");
    }

    [Fact]
    public async Task Con_otro_certificado_fijado_falla()
    {
        if (!Activo) return;
        // Con ServerCertificate el servidor debe presentar exactamente el certificado fijado (más estricto que el nombre).
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var otro = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=aw-sql-onprem", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = Path.Combine(Directory.CreateTempSubdirectory("millet-aw-tls-").FullName, "otro.pem");
        File.WriteAllText(pem, otro.ExportCertificatePem());
        using var _ = AwTlsPruebas.ConfiarTemporalmente(pem);
        await using var conn = AwTlsPruebas.ViaFabricaProductiva(Servidor + Credenciales() + "HostNameInCertificate=aw-sql-onprem;");
        var ex = await Assert.ThrowsAsync<SqlException>(() => conn.OpenAsync());
        salida.WriteLine($"Otro certificado fijado: falla como se espera ({ex.GetType().Name}).");
    }

    [Fact]
    public void La_fabrica_productiva_rechaza_las_cadenas_inseguras()
    {
        foreach (var sufijo in new[] { "Encrypt=False;", "Encrypt=Optional;", "TrustServerCertificate=True;", "Encrypt=Strict;" })
            Assert.Throws<InvalidOperationException>(() => AwTlsPruebas.ViaFabricaProductiva(Servidor.Replace("Encrypt=True;TrustServerCertificate=False;", "") + sufijo));
    }
}

/// <summary>
/// A+W-QA-4: mismo camino productivo contra el sandbox local (SQL 2022, certificado propio CN=localhost).
/// OPT-IN: requiere AW_SANDBOX_CONN (usuario RO) y AW_SANDBOX_CERT_PEM (ruta del PEM del contenedor).
/// </summary>
[Trait("Category", "AwSandbox")]
public class AwSqlTlsSandboxTests(ITestOutputHelper salida)
{
    private static readonly string? Conn = Environment.GetEnvironmentVariable("AW_SANDBOX_CONN");
    private static readonly string? PemPath = Environment.GetEnvironmentVariable("AW_SANDBOX_CERT_PEM");

    private bool Activo
    {
        get
        {
            var ok = !string.IsNullOrEmpty(Conn) && !string.IsNullOrEmpty(PemPath);
            if (!ok) salida.WriteLine("OMITIDO: faltan AW_SANDBOX_CONN y AW_SANDBOX_CERT_PEM.");
            return ok;
        }
    }

    private static string Verificada() => new SqlConnectionStringBuilder(Conn!)
    {
        Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = false, Pooling = false,
    }.ConnectionString;

    [Fact]
    public async Task Fabrica_productiva_conecta_cifrado_y_verificado()
    {
        if (!Activo) return;
        using var _ = AwTlsPruebas.ConfiarTemporalmente(PemPath!);
        await using var conn = AwTlsPruebas.ViaFabricaProductiva(Verificada());
        await conn.OpenAsync();
        salida.WriteLine($"encrypt_option: {await AwTlsPruebas.CifradoAsync(conn)}");
        salida.WriteLine("Sandbox: conectado por la fábrica productiva con cadena verificada.");
    }

    [Fact]
    public void Fabrica_productiva_rechaza_TrustServerCertificate()
    {
        if (!Activo) return;
        var b = new SqlConnectionStringBuilder(Conn!) { Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true };
        Assert.Throws<InvalidOperationException>(() => AwTlsPruebas.ViaFabricaProductiva(b.ConnectionString));
    }
}
