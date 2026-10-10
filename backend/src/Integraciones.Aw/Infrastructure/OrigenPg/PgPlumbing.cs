using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Npgsql;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>Conexiones por llamada y errores del origen de demo; no altera la plomería SQL Server.</summary>
internal static class PgPlumbing
{
    internal static async Task<T> EjecutarAsync<T>(IIntegracionSqlConnectionFactory factory,
        AwPedidosOptions options, ILogger logger, string operacion,
        Func<DbConnection, Task<T>> accion, CancellationToken ct)
    {
        try
        {
            await using var connection = factory.CreateConnection();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.SqlConnectTimeoutSeconds));
            try { await connection.OpenAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AwReaderException("Se agotó el tiempo de conexión a la copia de demo de A+W.",
                    "connect_timeout", true);
            }
            return await accion(connection);
        }
        catch (NpgsqlException ex)
        {
            logger.LogWarning("Error de PostgreSQL en copia de demo A+W: {Operacion}, {Tipo}", operacion, ex.GetType().Name);
            var auth = ex is PostgresException { SqlState: "28P01" or "28000" };
            throw new AwReaderException(auth
                ? "No se pudo autenticar la conexión a la copia de demo de A+W. Revise su configuración."
                : "No se pudo leer o escribir la copia de demo de A+W. Revise su conexión y sus tablas.",
                auth ? "auth" : "connection", !auth && ex.IsTransient, ex);
        }
    }

    internal static DbCommand CrearCommand(DbConnection connection, string sql, AwPedidosOptions options)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.SqlQueryTimeoutSeconds;
        return command;
    }

    internal static void Param(DbCommand command, string nombre, DbType tipo, object? valor)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = nombre;
        parameter.DbType = tipo;
        parameter.Value = valor ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    internal static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
