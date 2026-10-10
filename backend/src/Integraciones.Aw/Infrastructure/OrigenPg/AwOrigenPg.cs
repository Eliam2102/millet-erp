using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.SharedKernel.Application.Exceptions;
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

    public sealed class Fabrica : IIntegracionSqlConnectionFactory
    {
        private readonly Func<string?> _leerCadena;

        public Fabrica(string connectionString) => _leerCadena = () => connectionString;

        // La configuración se consulta al usar la conexión, incluso si el adaptador ya fue resuelto.
        public Fabrica(IConfiguration configuration) =>
            _leerCadena = () => configuration.GetConnectionString(ConnectionStringName);

        public DbConnection CreateConnection()
        {
            var cs = _leerCadena();
            if (string.IsNullOrWhiteSpace(cs) || cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("AW_DEMO_NO_CONFIGURADA", "La copia de demo de A+W no está configurada en este ambiente");
            return new NpgsqlConnection(cs);
        }
    }
}
