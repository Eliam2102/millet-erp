using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapters SQL de <see cref="IAwClientesReader"/> / <see cref="IAwArticulosReader"/>
/// sobre las vistas de masters de <c>MILLET_INTEGRACION</c> (flujo 2,
/// ADR-0048). Misma plomería que <see cref="AwSolicitudesSqlReader"/>:
/// connection per-call, timeout duro de OpenAsync, clasificación
/// <see cref="AwReaderException"/>.
/// </summary>
public sealed class AwClientesSqlReader : IAwClientesReader
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwClientesSqlReader> _logger;

    public AwClientesSqlReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwClientesSqlReader> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwClienteMaster?> LeerClienteAsync(
        string clienteRef, CancellationToken cancellationToken)
    {
        // PLATFORM-TODO(<AwClienteNumRegIdTrib>): cuando A+W agregue
        // num_reg_id_trib (tax id extranjero) a vw_erp_cliente, ampliar el
        // SELECT y mapearlo abajo (hoy llega null → captura en Datos Maestros).
        const string sql = """
            SELECT cliente_ref, razon_social, rfc, calle, colonia, cp,
                   ciudad, estado, pais, telefono
              FROM dbo.vw_erp_cliente
             WHERE cliente_ref = @ref
            """;

        return await SqlPlumbing.EjecutarAsync(_connectionFactory, _options, _logger,
            "LeerCliente", async connection =>
            {
                using var command = SqlPlumbing.CrearCommand(connection, sql, _options);
                command.Parameters.Add(new SqlParameter("@ref", SqlDbType.NVarChar, 50) { Value = clienteRef });

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;

                return new AwClienteMaster(
                    ClienteRef: reader.GetString(0),
                    RazonSocial: SqlPlumbing.GetStringOrNull(reader, 1) ?? string.Empty,
                    Rfc: SqlPlumbing.GetStringOrNull(reader, 2),
                    Calle: SqlPlumbing.GetStringOrNull(reader, 3),
                    Colonia: SqlPlumbing.GetStringOrNull(reader, 4),
                    Cp: SqlPlumbing.GetStringOrNull(reader, 5),
                    Ciudad: SqlPlumbing.GetStringOrNull(reader, 6),
                    Estado: SqlPlumbing.GetStringOrNull(reader, 7),
                    Pais: SqlPlumbing.GetStringOrNull(reader, 8),
                    Telefono: SqlPlumbing.GetStringOrNull(reader, 9));
            }, cancellationToken);
    }
}

public sealed class AwArticulosSqlReader : IAwArticulosReader
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwArticulosSqlReader> _logger;

    public AwArticulosSqlReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwArticulosSqlReader> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwArticuloMaster?> LeerArticuloAsync(
        string articuloRef, CancellationToken cancellationToken)
    {
        // PLATFORM-TODO(<AwArticuloAduana>): cuando A+W agregue fraccion_arancelaria
        // y peso_unitario_kg a vw_erp_articulo, ampliar el SELECT y mapearlos abajo
        // (hoy llegan null → el operador los captura en Datos Maestros).
        const string sql = """
            SELECT producto_ref, descripcion, unidad_medida
              FROM dbo.vw_erp_articulo
             WHERE producto_ref = @ref
            """;

        return await SqlPlumbing.EjecutarAsync(_connectionFactory, _options, _logger,
            "LeerArticulo", async connection =>
            {
                using var command = SqlPlumbing.CrearCommand(connection, sql, _options);
                command.Parameters.Add(new SqlParameter("@ref", SqlDbType.NVarChar, 50) { Value = articuloRef });

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;

                return new AwArticuloMaster(
                    ProductoRef: reader.GetString(0),
                    Descripcion: SqlPlumbing.GetStringOrNull(reader, 1) ?? string.Empty,
                    UnidadMedida: SqlPlumbing.GetStringOrNull(reader, 2) ?? string.Empty);
            }, cancellationToken);
    }
}

/// <summary>
/// Plomería SQL compartida de la sub-área Pedidos (conexión + clasificación
/// de errores — mismo patrón del flujo 1, factorizada para los readers de
/// masters). <c>AwSolicitudesSqlReader</c> mantiene su copia local por
/// legibilidad de su flujo de 3 queries.
/// </summary>
internal static class SqlPlumbing
{
    internal static async Task<T> EjecutarAsync<T>(
        IIntegracionSqlConnectionFactory factory,
        AwPedidosOptions options,
        ILogger logger,
        string operacion,
        Func<DbConnection, Task<T>> accion,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = factory.CreateConnection();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(options.SqlConnectTimeoutSeconds));
            try
            {
                await connection.OpenAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (
                connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new AwReaderException(
                    $"SQL connect timeout tras {options.SqlConnectTimeoutSeconds}s (op={operacion}).",
                    kind: "connect_timeout", isTransient: true);
            }

            return await accion(connection);
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            logger.LogError(ex, "[Pedidos.SqlPlumbing] auth failure op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL Server auth failed (#{ex.Number}): {ex.Message}",
                kind: "auth", isTransient: false, inner: ex);
        }
        catch (SqlException ex) when (ex.Number is -2 or 11 or 121)
        {
            logger.LogWarning(ex, "[Pedidos.SqlPlumbing] timeout op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL query timeout ({options.SqlQueryTimeoutSeconds}s).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "[Pedidos.SqlPlumbing] SQL error op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL Server error (#{ex.Number}): {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "[Pedidos.SqlPlumbing] connection state error op={Op}", operacion);
            throw new AwReaderException(
                $"SQL connection state error: {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
    }

    internal static DbCommand CrearCommand(DbConnection connection, string sql, AwPedidosOptions options)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = options.SqlQueryTimeoutSeconds;
        return command;
    }

    internal static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
