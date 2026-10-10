using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Clientes;

/// <summary>
/// Lector SOLO-SELECT de clientes A+W (<c>SYSADM.KU_KUNDEN</c> + catálogo <c>SYSADM.KA_ZAHLBED</c>).
/// Deshabilitado por defecto (ver <see cref="ClientesDependencyInjection"/>). Reusa la plomería
/// (timeouts, clasificación a <c>AwReaderException</c>) de Pedidos.
/// </summary>
public sealed class AwClientesSqlOrigen : IAwClientesOrigen
{
    internal const string Columnas = """
        k.ID, k.MANDANT, k.NAME1, k.NAME2, k.NAME3, k.STRASSE, k.ORT, k.PLZ, k.PROVINZ, k.LAND,
        k.UST_ID, k.STEUERNUMMER, k.TLF1, k.TLF2, k.MAIL, k.ZAHLBED, k.WAEHRUNG,
        k.KREDIT_LIMIT, k.KREDIT_LIMIT1, k.KREDIT_LIMIT_NET, k.KZ_STATUS, k.KZ_GESPERRT,
        k.DATUM, k.TRANSACTION_TIME, z.BEZ, z.NUMMER, z.BRUTTOTAGE
        """;

    // El TOP va en la tabla derivada para que el lote cuente clientes y no filas del join con el catálogo.
    public const string SqlPagina =
        "SELECT " + Columnas + """

          FROM (SELECT TOP (@n) ID, MANDANT, NAME1, NAME2, NAME3, STRASSE, ORT, PLZ, PROVINZ, LAND,
                       UST_ID, STEUERNUMMER, TLF1, TLF2, MAIL, ZAHLBED, WAEHRUNG,
                       KREDIT_LIMIT, KREDIT_LIMIT1, KREDIT_LIMIT_NET, KZ_STATUS, KZ_GESPERRT,
                       DATUM, TRANSACTION_TIME
                  FROM SYSADM.KU_KUNDEN WHERE ID > @cursor ORDER BY ID) AS k
          LEFT JOIN SYSADM.KA_ZAHLBED AS z ON z.BEZ = k.ZAHLBED
         ORDER BY k.ID, z.NUMMER
        """;

    public const string SqlReferencia =
        "SELECT " + Columnas + """

          FROM (SELECT TOP (1) ID, MANDANT, NAME1, NAME2, NAME3, STRASSE, ORT, PLZ, PROVINZ, LAND,
                       UST_ID, STEUERNUMMER, TLF1, TLF2, MAIL, ZAHLBED, WAEHRUNG,
                       KREDIT_LIMIT, KREDIT_LIMIT1, KREDIT_LIMIT_NET, KZ_STATUS, KZ_GESPERRT,
                       DATUM, TRANSACTION_TIME
                  FROM SYSADM.KU_KUNDEN WHERE ID = @cursor ORDER BY ID) AS k
          LEFT JOIN SYSADM.KA_ZAHLBED AS z ON z.BEZ = k.ZAHLBED
         ORDER BY k.ID, z.NUMMER
        """;

    private readonly IIntegracionSqlConnectionFactory _factory;
    private readonly AwPedidosOptions _plomeria; // solo timeouts, para reusar SqlPlumbing sin tocar Pedidos
    private readonly ILogger<AwClientesSqlOrigen> _logger;

    public AwClientesSqlOrigen(
        IIntegracionSqlConnectionFactory factory,
        IOptions<AwClientesOptions> options,
        ILogger<AwClientesSqlOrigen> logger)
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
        var filas = await LeerAsync("LeerClientePorReferencia", SqlReferencia, id, null, ct);
        return filas.FirstOrDefault();
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
            command.Parameters.Add(new SqlParameter("@cursor", SqlDbType.Int) { Value = cursor });
            if (n is not null) command.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = n });

            var resultado = new List<AwClienteOrigenFila>();
            List<AwCondicionCoincidencia>? actual = null;
            using var r = await command.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (resultado.Count == 0 || resultado[^1].Id != id)
                {
                    actual = [];
                    resultado.Add(Fila(r, actual));
                }
                if (!r.IsDBNull(24)) // BEZ presente = hay coincidencia (NUMMER/BRUTTOTAGE pueden ser nulos)
                    actual!.Add(new(Int(r, 25), Int(r, 26)));
            }
            return resultado;
        }, ct);

    internal static AwClienteOrigenFila Fila(DbDataReader r, List<AwCondicionCoincidencia> coincidencias) => new(
        r.GetInt32(0), r.GetInt32(1),
        SqlPlumbing.GetStringOrNull(r, 2), SqlPlumbing.GetStringOrNull(r, 3), SqlPlumbing.GetStringOrNull(r, 4),
        SqlPlumbing.GetStringOrNull(r, 5), SqlPlumbing.GetStringOrNull(r, 6), SqlPlumbing.GetStringOrNull(r, 7),
        SqlPlumbing.GetStringOrNull(r, 8), SqlPlumbing.GetStringOrNull(r, 9), SqlPlumbing.GetStringOrNull(r, 10),
        SqlPlumbing.GetStringOrNull(r, 11), SqlPlumbing.GetStringOrNull(r, 12), SqlPlumbing.GetStringOrNull(r, 13),
        SqlPlumbing.GetStringOrNull(r, 14), SqlPlumbing.GetStringOrNull(r, 15), SqlPlumbing.GetStringOrNull(r, 16),
        r.IsDBNull(17) ? null : r.GetDecimal(17), r.IsDBNull(18) ? null : r.GetDecimal(18),
        r.IsDBNull(19) ? null : r.GetDouble(19), Int(r, 20), r.GetInt32(21),
        r.IsDBNull(22) ? null : DateOnly.FromDateTime(r.GetDateTime(22)),
        r.IsDBNull(23) ? null : r.GetDateTime(23),
        coincidencias);

    private static int? Int(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
}
