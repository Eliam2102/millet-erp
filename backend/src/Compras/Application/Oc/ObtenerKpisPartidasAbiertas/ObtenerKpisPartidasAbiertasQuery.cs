using MediatR;

namespace Millet.Compras.Application.Oc.ObtenerKpisPartidasAbiertas;

/// <summary>
/// KPIs agregados de partidas abiertas (F7-PR3, brecha §14.4 / FOC10).
/// Devuelve 4 contadores reactivos a los mismos filtros de
/// <see cref="ListarPartidasAbiertas.ListarPartidasAbiertasQuery"/>:
/// cantidad pendiente de recibir, cantidad pendiente de facturar,
/// monto pendiente de pago y count de OCs atrasadas.
/// </summary>
public sealed record ObtenerKpisPartidasAbiertasQuery(
    Guid? ProveedorId = null,
    Guid? CompradorTitularId = null,
    DateOnly? FechaDocumentoDesde = null,
    DateOnly? FechaDocumentoHasta = null) : IRequest<KpisPartidasAbiertasResponse>;

public sealed record KpisPartidasAbiertasResponse(
    int CountPartidasAbiertas,
    int CountAtrasadas,
    int CountConRecepcionParcial,
    int CountConFacturacionParcial,
    int CountSinPago);
