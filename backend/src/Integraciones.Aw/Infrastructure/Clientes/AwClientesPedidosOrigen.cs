using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Clientes;

/// <summary>
/// Historial por cliente en A+W: SOLO-SELECT sobre <c>SYSADM.BW_AUFTR_KOPF</c> (pedidos) y <c>SYSADM.BW_ANGEB_KOPF</c>
/// (cotizaciones); en ambas <c>AH_IDENT</c> = <c>KU_KUNDEN.ID</c> y <c>DATUM_ERF</c> = fecha de alta.
/// Misma conexión de clientes (TLS verificado).
/// </summary>
public sealed class AwClientesPedidosSqlOrigen : IAwClientesPedidosOrigen
{
    internal const int TamanoLote = 500;
    private readonly IIntegracionSqlConnectionFactory _factory;
    private readonly AwClientesOptions _options;
    private readonly AwPedidosOptions _plomeria;
    private readonly ILogger<AwClientesPedidosSqlOrigen> _logger;

    public AwClientesPedidosSqlOrigen(
        IIntegracionSqlConnectionFactory factory, IOptions<AwClientesOptions> options, ILogger<AwClientesPedidosSqlOrigen> logger)
    {
        _factory = factory;
        _options = options.Value;
        _plomeria = new AwPedidosOptions
        {
            SqlQueryTimeoutSeconds = _options.SqlQueryTimeoutSeconds,
            SqlConnectTimeoutSeconds = _options.SqlConnectTimeoutSeconds,
        };
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, AwPedidosCliente>> ContarAsync(IReadOnlyCollection<string> referencias, CancellationToken ct)
    {
        var ids = referencias.Select(r => int.TryParse(r, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? (int?)n : null)
            .Where(n => n is not null).Select(n => n!.Value).Distinct().ToArray();
        var resultado = new Dictionary<string, AwPedidosCliente>(StringComparer.Ordinal);
        if (ids.Length == 0) return resultado;
        // Sin lectura habilitada no se adivina "sin pedidos": un Fusionar equivocado ocultaría historial real.
        if (!_options.LecturaHabilitada)
            throw new AwClientesSyncException("lectura_deshabilitada", "La lectura de clientes A+W está apagada en este ambiente. Avisa a TI.");
        try
        {
            foreach (var lote in ids.Chunk(TamanoLote))
                await SqlPlumbing.EjecutarAsync(_factory, _plomeria, _logger, "ContarPedidosPorCliente", async connection =>
                {
                    using var command = SqlPlumbing.CrearCommand(connection, Sql(lote.Length), _plomeria);
                    for (var i = 0; i < lote.Length; i++)
                        command.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.Int) { Value = lote[i] });
                    using var r = await command.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct))
                    {
                        var clave = r.GetInt32(0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var previo = resultado.GetValueOrDefault(clave) ?? new AwPedidosCliente(0, null);
                        var fecha = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3);
                        resultado[clave] = r.GetInt32(1) == 0
                            ? previo with { Pedidos = r.GetInt32(2), Ultimo = fecha }
                            : previo with { Cotizaciones = r.GetInt32(2), UltimaCotizacion = fecha };
                    }
                    return true;
                }, ct);
        }
        catch (AwReaderException ex)
        {
            throw new AwClientesSyncException("origen_no_disponible", $"No se pudo leer el historial de pedidos de A+W: {ex.Message}");
        }
        return resultado;
    }

    /// <summary>Solo parámetros numéricos (@p0..@pN): sin concatenar valores.</summary>
    internal static string Sql(int n)
    {
        var inn = string.Join(",", Enumerable.Range(0, n).Select(i => $"@p{i}"));
        return $"SELECT AH_IDENT, 0, COUNT(*), MAX(DATUM_ERF) FROM SYSADM.BW_AUFTR_KOPF WHERE AH_IDENT IN ({inn}) GROUP BY AH_IDENT "
            + $"UNION ALL SELECT AH_IDENT, 1, COUNT(*), MAX(DATUM_ERF) FROM SYSADM.BW_ANGEB_KOPF WHERE AH_IDENT IN ({inn}) GROUP BY AH_IDENT";
    }
}

/// <summary>Origen simulado/demo: sin historial en A+W (todo conteo en cero).</summary>
public sealed class AwClientesPedidosVacio : IAwClientesPedidosOrigen
{
    public Task<IReadOnlyDictionary<string, AwPedidosCliente>> ContarAsync(IReadOnlyCollection<string> referencias, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, AwPedidosCliente>>(new Dictionary<string, AwPedidosCliente>());
}
