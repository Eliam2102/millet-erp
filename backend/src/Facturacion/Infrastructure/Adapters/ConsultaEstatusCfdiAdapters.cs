using Microsoft.Extensions.Configuration;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Adapters;

/// <summary>
/// Adaptador real (F6.7): consulta el estatus por valores en el servicio del
/// SAT a través de FiscalAPI, el mismo camino que el poller de cancelaciones.
/// </summary>
public sealed class ConsultaEstatusCfdiFiscalApiAdapter(ICfdiTimbradoPort fiscal) : IConsultaEstatusCfdiPort
{
    public string Fuente => "SAT (FiscalAPI)";

    public async Task<EstatusCfdiSat> ConsultarAsync(ConsultaEstatusCfdi consulta, CancellationToken cancellationToken)
    {
        var r = await fiscal.ConsultarEstatusAsync(
            new EstatusCfdiSolicitud(consulta.EmpresaId, consulta.Uuid, consulta.RfcEmisor,
                consulta.RfcReceptor, consulta.Total, consulta.Sello8),
            cancellationToken);
        var estatus = r.EsVigente ? EstatusSat.Vigente
            : r.EstatusSat.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? EstatusSat.Cancelado
            : EstatusSat.NoEncontrado;
        return new EstatusCfdiSat(estatus, r.EsCancelable switch
        {
            true => r.EstadoCancelacion ?? "Cancelable",
            false => "No cancelable",
            null => r.EstadoCancelacion,
        });
    }
}

/// <summary>
/// Adaptador de prueba (F6.7; Eliam 20-oct: «la consulta al SAT va con el
/// adaptador de prueba»). Responde según el estado local del comprobante
/// (Cancelado → Cancelado; el resto → Vigente) y permite forzar UUID a
/// «Cancelado» o «No encontrado» para probar discrepancias:
/// <c>Facturacion:ConsultaEstatusSat:UuidsCancelados</c> y
/// <c>:UuidsNoEncontrados</c> (listas separadas por coma).
/// </summary>
public sealed class ConsultaEstatusCfdiSimuladoAdapter : IConsultaEstatusCfdiPort
{
    private readonly HashSet<string> _cancelados;
    private readonly HashSet<string> _noEncontrados;

    // Un solo constructor público: con dos, el contenedor de DI no sabe cuál usar
    // (IEnumerable<string> también es resoluble) y la API no arranca.
    public ConsultaEstatusCfdiSimuladoAdapter(IConfiguration configuration)
        : this(
            Lista(configuration[$"{ConsultaEstatusSatConfig.Seccion}:UuidsCancelados"]),
            Lista(configuration[$"{ConsultaEstatusSatConfig.Seccion}:UuidsNoEncontrados"]))
    {
    }

    private ConsultaEstatusCfdiSimuladoAdapter(IEnumerable<string> cancelados, IEnumerable<string> noEncontrados)
    {
        _cancelados = new HashSet<string>(cancelados, StringComparer.OrdinalIgnoreCase);
        _noEncontrados = new HashSet<string>(noEncontrados, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Para pruebas: UUID forzados sin pasar por configuración.</summary>
    public static ConsultaEstatusCfdiSimuladoAdapter Con(IEnumerable<string> cancelados, IEnumerable<string> noEncontrados) =>
        new(cancelados, noEncontrados);

    public string Fuente => "Simulado (adaptador de prueba)";

    public Task<EstatusCfdiSat> ConsultarAsync(ConsultaEstatusCfdi consulta, CancellationToken cancellationToken)
    {
        EstatusCfdiSat r = _noEncontrados.Contains(consulta.Uuid) ? new(EstatusSat.NoEncontrado, null)
            : _cancelados.Contains(consulta.Uuid) || consulta.EstadoLocal == EstadoTimbrado.Cancelado
                ? new(EstatusSat.Cancelado, null)
            : new(EstatusSat.Vigente, "Cancelable con aceptación");
        return Task.FromResult(r);
    }

    private static string[] Lista(string? valor) =>
        (valor ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class ConsultaEstatusSatConfig
{
    public const string Seccion = "Facturacion:ConsultaEstatusSat";

    /// <summary>Por omisión se usa el simulado: el real requiere FiscalAPI habilitado (prod lo configura explícitamente).</summary>
    public static bool UsarFiscalApi(IConfiguration configuration) =>
        string.Equals(configuration[$"{Seccion}:Proveedor"], "FiscalApi", StringComparison.OrdinalIgnoreCase);
}
