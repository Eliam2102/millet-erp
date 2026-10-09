using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Npgsql;
using NpgsqlTypes;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>
/// Gemelo de <see cref="AwClientesSqlOrigen"/> sobre <c>aw_origen.ku_kunden</c> + <c>aw_origen.ka_zahlbed</c> (mismas
/// columnas y nombres que A+W). Misma lista de columnas, mismo mapeo de fila, mismo cursor por ID y mismas
/// coincidencias con el catálogo de condiciones; solo cambia <c>TOP</c> por <c>LIMIT</c> y el esquema.
/// </summary>
public sealed class AwClientesPgOrigen : IAwClientesOrigen
{
    private const string Resto = """

          LEFT JOIN aw_origen.ka_zahlbed AS z ON z.bez = k.zahlbed
         ORDER BY k.id, z.nummer
        """;

    private const string SqlPagina = "SELECT " + AwClientesSqlOrigen.Columnas +
        " FROM (SELECT * FROM aw_origen.ku_kunden WHERE id > @cursor ORDER BY id LIMIT @n) AS k" + Resto;

    private const string SqlReferencia = "SELECT " + AwClientesSqlOrigen.Columnas +
        " FROM (SELECT * FROM aw_origen.ku_kunden WHERE id = @cursor LIMIT 1) AS k" + Resto;

    private readonly IIntegracionSqlConnectionFactory _factory;
    private readonly AwPedidosOptions _plomeria; // solo timeouts, para reusar SqlPlumbing
    private readonly ILogger<AwClientesPgOrigen> _logger;

    public AwClientesPgOrigen(
        IIntegracionSqlConnectionFactory factory, IOptions<AwClientesOptions> options, ILogger<AwClientesPgOrigen> logger)
    {
        _factory = factory;
        _plomeria = new AwPedidosOptions
        {
            SqlQueryTimeoutSeconds = options.Value.SqlQueryTimeoutSeconds,
            SqlConnectTimeoutSeconds = options.Value.SqlConnectTimeoutSeconds,
        };
        _logger = logger;
    }

    public async Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        if (!int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return null;
        return (await LeerAsync("LeerClientePorReferencia", SqlReferencia, id, null, ct)).FirstOrDefault();
    }

    public async Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        tamano = Math.Clamp(tamano, 1, AwClientesOptions.TamanoLoteMaximo);
        var desde = 0;
        if (cursor is not null && !int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out desde))
            throw new ArgumentException("Cursor inválido.", nameof(cursor));

        var filas = await LeerAsync("LeerPaginaClientes", SqlPagina, desde, tamano, ct);
        return new(filas, filas.Count == tamano ? filas[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
    }

    private Task<List<AwClienteOrigenFila>> LeerAsync(string operacion, string sql, int cursor, int? n, CancellationToken ct) =>
        SqlPlumbing.EjecutarAsync(_factory, _plomeria, _logger, operacion, async connection =>
        {
            using var command = SqlPlumbing.CrearCommand(connection, sql, _plomeria);
            command.Parameters.Add(new NpgsqlParameter("cursor", NpgsqlDbType.Integer) { Value = cursor });
            if (n is not null) command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = n });

            var resultado = new List<AwClienteOrigenFila>();
            List<AwCondicionCoincidencia>? actual = null;
            using var r = await command.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (resultado.Count == 0 || resultado[^1].Id != id)
                {
                    actual = [];
                    resultado.Add(AwClientesSqlOrigen.Fila(r, actual));
                }
                if (!r.IsDBNull(24)) // BEZ presente = hay coincidencia (NUMMER/BRUTTOTAGE pueden ser nulos)
                    actual!.Add(new(Int(r, 25), Int(r, 26)));
            }
            return resultado;
        }, ct);

    private static int? Int(System.Data.Common.DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
}
