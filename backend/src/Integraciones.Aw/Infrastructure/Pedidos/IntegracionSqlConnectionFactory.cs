using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Factory de conexiones a la BD de integración on-prem
/// <c>MILLET_INTEGRACION(_DEV)</c> (ADR-0048 D1) — flujo 2, ingesta de
/// pedidos. Interfaz PROPIA (no reutiliza <c>ISqlConnectionFactory</c> del
/// flujo 1) porque apunta a otra base con otro secreto:
/// <c>ConnectionStrings:AwIntegracionDb</c> (KV
/// <c>aw-integracion-connection-string</c>).
///
/// <para>
/// Formato esperado: <c>Server=aw-sql-onprem,1433;Database=MILLET_INTEGRACION;
/// User ID=millet_erp_integracion;Password=...;Encrypt=True;
/// TrustServerCertificate=False;Connection Timeout=10;</c>. TLS verificado
/// (<see cref="AwSqlTls"/>); el cert de SER-DATA se confía vía
/// <c>IntegracionesAw:SqlTls:CertificadoServidorPem</c>.
/// </para>
/// </summary>
public interface IIntegracionSqlConnectionFactory
{
    DbConnection CreateConnection();
}

public sealed class IntegracionSqlConnectionFactory : IIntegracionSqlConnectionFactory
{
    private readonly string _connectionString;

    public IntegracionSqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = AwSqlTls.Validar(configuration.GetConnectionString("AwIntegracionDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:AwIntegracionDb no configurado. " +
                "En QA/Prod viene de KV ref aw-integracion-connection-string. " +
                "En dev local agregar a appsettings.Development.json o env var " +
                "ConnectionStrings__AwIntegracionDb. (Sin connection string, " +
                "Program.cs deja registrados los stubs de F3 — este factory " +
                "no debería resolverse.)"), "AwIntegracionDb");
    }

    public DbConnection CreateConnection() => new SqlConnection(_connectionString);
}
