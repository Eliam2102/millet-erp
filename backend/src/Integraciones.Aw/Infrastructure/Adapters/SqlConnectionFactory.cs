using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación productiva de <see cref="ISqlConnectionFactory"/>.
/// Lee la connection string de <c>ConnectionStrings:AwSqlServer</c>.
///
/// <para>
/// Connection string esperada (formato típico para SQL Server on-prem
/// vía Hybrid Connection):
/// <c>Server=SER-DATA,1433;Database=MILMAIN;User ID=millet_erp_reader;
/// Password=...;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10;</c>
/// </para>
///
/// <para>
/// // PLATFORM-TODO(&lt;SqlTlsHardening&gt;): <c>Encrypt=False</c> es
/// defensible en fase 1 porque el tráfico va por Hybrid Connection (TLS
/// at relay layer). Cuando SQL Server on-prem tenga cert válido (AD CS o
/// auto-firmado importado en el truststore del App Service), cambiar la
/// connection string a <c>Encrypt=True;TrustServerCertificate=False</c>.
/// El cambio es solo de configuración (KV secret aw-sql-connection-string),
/// no requiere modificar este adapter.
/// </para>
/// </summary>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AwSqlServer")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:AwSqlServer no configurado. " +
                "En QA/Prod viene de KV ref aw-sql-connection-string. " +
                "En dev local agregar a appsettings.Development.json o env var " +
                "ConnectionStrings__AwSqlServer.");
    }

    public DbConnection CreateConnection() => new SqlConnection(_connectionString);
}
