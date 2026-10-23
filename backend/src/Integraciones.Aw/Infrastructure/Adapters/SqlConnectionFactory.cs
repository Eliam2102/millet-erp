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
/// <c>Server=aw-sql-onprem,1433;Database=MILMAIN;User ID=millet_erp_reader;
/// Password=...;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;</c>
/// </para>
///
/// <para>
/// TLS verificado obligatorio (<see cref="AwSqlTls"/>); el cert de SER-DATA se
/// confía vía <c>IntegracionesAw:SqlTls:CertificadoServidorPem</c>.
/// </para>
/// </summary>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = AwSqlTls.Validar(configuration.GetConnectionString("AwSqlServer")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:AwSqlServer no configurado. " +
                "En QA/Prod viene de KV ref aw-sql-connection-string. " +
                "En dev local agregar a appsettings.Development.json o env var " +
                "ConnectionStrings__AwSqlServer."), "AwSqlServer");
    }

    public DbConnection CreateConnection() => new SqlConnection(_connectionString);
}
