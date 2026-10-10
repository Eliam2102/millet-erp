using System.Data.Common;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Npgsql;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>
/// Origen de DEMO: en vez de leer A+W (SQL Server on-prem), clientes y productos se leen de las tablas
/// <c>aw_origen.*</c> de una BD PostgreSQL aparte (esquema y seeder en <c>tools/aw-origen-demo</c>). Los puertos
/// <c>IAwClientesOrigen</c>/<c>IAwProductosOrigen</c> y todo lo que está aguas abajo (sincronizadores, mapeo,
/// aplicación al maestro, errores) son los mismos que con A+W real; solo cambia el SELECT.
/// Se selecciona en la base del ERP con el botón de administradores y <c>ConnectionStrings:AwOrigenPgDb</c>.
/// </summary>
public static class AwOrigenPg
{
    public const string ConnectionStringName = "AwOrigenPgDb";

    public sealed class Fabrica(string connectionString) : IIntegracionSqlConnectionFactory
    {
        public DbConnection CreateConnection() => new NpgsqlConnection(connectionString);
    }
}
