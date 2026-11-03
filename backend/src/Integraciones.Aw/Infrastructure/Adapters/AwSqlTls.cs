using Microsoft.Data.SqlClient;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Política única de TLS hacia SER-DATA para todos los flujos A+W (ADR-0048): conexión cifrada
/// y verificada. El SQL Server 2016 de SER-DATA no soporta TDS 8, así que <c>Strict</c> se rechaza.
/// </summary>
public static class AwSqlTls
{
    /// <summary>
    /// Ruta local del PEM público de SER-DATA que dejó <see cref="AwSqlCertificadoServidor.Instalar"/>. Con ella cada
    /// conexión fija ese certificado (<c>ServerCertificate</c>) en vez de depender del almacén de confianza del sistema.
    /// ponytail: estado de proceso; se fija una vez al registrar servicios, antes de abrir cualquier conexión.
    /// </summary>
    public static string? CertificadoServidorRuta { get; set; }

    /// <summary>Devuelve la cadena normalizada o lanza <see cref="InvalidOperationException"/> sin incluir secretos.</summary>
    public static string Validar(string connectionString, string nombre)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        if (b.Encrypt == SqlConnectionEncryptOption.Strict)
            throw new InvalidOperationException(
                $"ConnectionStrings:{nombre}: Encrypt=Strict no es válido; el SQL Server 2016 de SER-DATA no soporta TDS 8. Use Encrypt=True.");
        if (b.Encrypt != SqlConnectionEncryptOption.Mandatory || b.TrustServerCertificate)
            throw new InvalidOperationException(
                $"ConnectionStrings:{nombre} exige TLS verificado (Encrypt=True;TrustServerCertificate=False).");
        if (CertificadoServidorRuta is { } ruta && string.IsNullOrEmpty(b.ServerCertificate))
            b.ServerCertificate = ruta;
        return b.ConnectionString;
    }
}
