using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.ListarPartidasAbiertas;

/// <summary>
/// Reporte de partidas abiertas (F7-PR1). Una "partida abierta" es una
/// OC no terminal con al menos una dimensión sub-estado sin cerrar
/// (recepción != Completa, o facturación != Completa, o pago != Pagada).
/// Por convención, las OCs en estado <c>Cerrada</c> / <c>Cancelada</c> /
/// <c>Rechazada</c> NO aparecen en este reporte.
///
/// <para>
/// Filtros del §7 del 02-plan + §8.2 del 01-diseño:
/// estado, sub-recepción, sub-facturación, sub-pago, proveedor,
/// comprador, fecha de documento (rango), contenedor, ruta, semana de
/// embarque, días atrasados (computed contra fecha_entrega_esperada).
/// </para>
///
/// <para>
/// Filtros NO implementados en F7-PR1: rango por <c>importe</c> (requiere
/// denormalizar <c>total_a_pagar</c>; pospuesto a F10-PR2 si la
/// performance lo justifica).
/// </para>
/// </summary>
public sealed record ListarPartidasAbiertasQuery(
    EstadoOrdenCompra? Estado = null,
    SubEstadoRecepcion? SubEstadoRecepcion = null,
    SubEstadoFacturacion? SubEstadoFacturacion = null,
    SubEstadoPago? SubEstadoPago = null,
    Guid? ProveedorId = null,
    Guid? CompradorTitularId = null,
    DateOnly? FechaDocumentoDesde = null,
    DateOnly? FechaDocumentoHasta = null,
    string? NumeroContenedor = null,
    string? CodigoRuta = null,
    string? SemanaEmbarque = null,
    int? DiasAtrasadosMinimos = null,
    int Page = 1,
    int PageSize = 50) : IRequest<ListarPartidasAbiertasResponse>;

public sealed record ListarPartidasAbiertasResponse(
    IReadOnlyList<PartidaAbiertaResumen> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PartidaAbiertaResumen(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoOrdenCompra Estado,
    SubEstadoRecepcion SubEstadoRecepcion,
    SubEstadoFacturacion SubEstadoFacturacion,
    SubEstadoPago SubEstadoPago,
    Guid ProveedorId,
    Guid CompradorTitularId,
    string Moneda,
    DateOnly FechaDocumento,
    DateOnly? FechaEntregaEsperada,
    int DiasAtrasados,
    string? ReferenciaProveedor,
    string? NumeroContenedor,
    string? CodigoRuta,
    string? SemanaEmbarque);
