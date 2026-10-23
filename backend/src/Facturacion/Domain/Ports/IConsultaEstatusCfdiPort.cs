using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Consulta del estatus de un CFDI emitido ante el SAT (F6.7, monitor de
/// estatus por sucursal y día; Manual de Sucursales §2.4). Dos adaptadores,
/// elegidos por <c>Facturacion:ConsultaEstatusSat:Proveedor</c>:
/// <c>FiscalApi</c> (real, delega en <c>ICfdiTimbradoPort.ConsultarEstatusAsync</c>)
/// y <c>Simulado</c> (adaptador de prueba para ambientes sin credenciales PAC).
/// </summary>
public interface IConsultaEstatusCfdiPort
{
    /// <summary>Etiqueta visible de la fuente («SAT (FiscalAPI)» o «Simulado»), para que nadie confunda una prueba con el SAT.</summary>
    string Fuente { get; }

    Task<EstatusCfdiSat> ConsultarAsync(ConsultaEstatusCfdi consulta, CancellationToken cancellationToken);
}

/// <summary>
/// Datos con los que el SAT identifica un CFDI (uuid + RFCs + total + últimos 8
/// del sello). <paramref name="EstadoLocal"/> solo lo usa el adaptador simulado.
/// </summary>
public sealed record ConsultaEstatusCfdi(
    Guid EmpresaId,
    string Uuid,
    string RfcEmisor,
    string RfcReceptor,
    decimal Total,
    string? Sello8,
    EstadoTimbrado EstadoLocal);

/// <summary>Respuesta normalizada: <see cref="EstatusSat"/> es «Vigente», «Cancelado» o «No encontrado».</summary>
public sealed record EstatusCfdiSat(string EstatusSat, string? EsCancelable);

public static class EstatusSat
{
    public const string Vigente = "Vigente";
    public const string Cancelado = "Cancelado";
    public const string NoEncontrado = "No encontrado";
}
