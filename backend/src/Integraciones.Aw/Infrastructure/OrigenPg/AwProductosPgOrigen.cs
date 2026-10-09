using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Npgsql;
using NpgsqlTypes;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>
/// Gemelo de <see cref="Productos.AwProductosSqlOrigen"/> sobre <c>aw_origen.erp_articulo</c>: una fila por producto con la
/// forma de <c>vw_erp_articulo</c> (doc integration/06 §3). Variantes y árbol de piezas viven ya armados en columnas
/// jsonb (con las llaves de <see cref="AwProductoOrigenVariante"/>/<see cref="AwProductoOrigenComponente"/>), así que
/// no hay joins ni composición en C#. Mismo cursor por <c>producto_ref</c> y mismo filtro <c>TiposExcluidos</c>.
/// </summary>
public sealed class AwProductosPgOrigen : IAwProductosOrigen
{
    private const string Columnas = """
        producto_ref, descripcion, unidad_medida, baja, transaction_time, codigo_modelo, grupo, tipo,
        wgr, wgr_descripcion, variantes::text, componentes::text
        """;

    // Como en SQL Server: el filtro de tipos solo aplica a la página, no a la lectura por referencia.
    private const string SqlPagina = "SELECT " + Columnas + """

          FROM aw_origen.erp_articulo
         WHERE producto_ref > @cursor AND (cardinality(@excluidos) = 0 OR tipo <> ALL(@excluidos))
         ORDER BY producto_ref LIMIT @n
        """;

    private const string SqlReferencia = "SELECT " + Columnas + """

          FROM aw_origen.erp_articulo WHERE producto_ref = @cursor
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IIntegracionSqlConnectionFactory _factory;
    private readonly AwPedidosOptions _plomeria; // solo timeouts, para reusar SqlPlumbing
    private readonly string[] _tiposExcluidos;
    private readonly ILogger<AwProductosPgOrigen> _logger;

    public AwProductosPgOrigen(
        IIntegracionSqlConnectionFactory factory, IOptions<AwProductosOptions> options, ILogger<AwProductosPgOrigen> logger)
    {
        _factory = factory;
        _plomeria = new AwPedidosOptions
        {
            SqlQueryTimeoutSeconds = options.Value.SqlQueryTimeoutSeconds,
            SqlConnectTimeoutSeconds = options.Value.SqlConnectTimeoutSeconds,
        };
        _tiposExcluidos = options.Value.TiposExcluidos ?? [];
        _logger = logger;
    }

    public async Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        if (!int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
        return (await LeerAsync("LeerProductoPorReferencia", SqlReferencia, id, null, ct)).FirstOrDefault();
    }

    public async Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        tamano = Math.Clamp(tamano, 1, AwProductosOptions.TamanoLoteMaximo);
        var desde = 0;
        if (cursor is not null && !int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out desde))
            throw new ArgumentException("Cursor inválido.", nameof(cursor));

        var filas = await LeerAsync("LeerPaginaProductos", SqlPagina, desde, tamano, ct);
        return new(filas, filas.Count == tamano ? filas[^1].ProductoRef : null);
    }

    private Task<List<AwProductoOrigenFila>> LeerAsync(string operacion, string sql, int cursor, int? n, CancellationToken ct) =>
        SqlPlumbing.EjecutarAsync(_factory, _plomeria, _logger, operacion, async connection =>
        {
            using var command = SqlPlumbing.CrearCommand(connection, sql, _plomeria);
            command.Parameters.Add(new NpgsqlParameter("cursor", NpgsqlDbType.Integer) { Value = cursor });
            if (n is not null)
            {
                command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = n });
                command.Parameters.Add(new NpgsqlParameter("excluidos", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = _tiposExcluidos });
            }

            var resultado = new List<AwProductoOrigenFila>();
            using var r = await command.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                resultado.Add(new(
                    r.GetInt32(0).ToString(CultureInfo.InvariantCulture),
                    SqlPlumbing.GetStringOrNull(r, 1), SqlPlumbing.GetStringOrNull(r, 2), r.GetBoolean(3),
                    JsonSerializer.Deserialize<List<AwProductoOrigenVariante>>(r.GetString(10), Json) ?? [],
                    r.IsDBNull(4) ? null : r.GetDateTime(4),
                    SqlPlumbing.GetStringOrNull(r, 5), SqlPlumbing.GetStringOrNull(r, 6), SqlPlumbing.GetStringOrNull(r, 7),
                    JsonSerializer.Deserialize<List<AwProductoOrigenComponente>>(r.GetString(11), Json) ?? [],
                    SqlPlumbing.GetStringOrNull(r, 8), SqlPlumbing.GetStringOrNull(r, 9)));
            return resultado;
        }, ct);
}
