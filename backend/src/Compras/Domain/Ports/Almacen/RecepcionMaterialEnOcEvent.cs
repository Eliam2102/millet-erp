using MediatR;

namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Evento in-process emitido por el módulo Almacén cuando registra una
/// recepción de material contra una línea de OC (F5-PR2). El listener
/// <c>RecepcionMaterialEnOcListener</c> en Application/Oc/Eventos lo
/// consume y llama <see cref="Oc.OrdenCompra.RegistrarRecepcionLinea"/>.
///
/// <para>
/// <b>Idempotencia</b>: <see cref="CantidadAcumulada"/> es el acumulado
/// (set semantics), no el delta. Repetir el mismo evento es no-op.
/// Si llega un acumulado menor al actual (devolución), usar
/// <see cref="OcDevolucionRegistradaEvent"/> que aclara intención.
/// </para>
/// </summary>
public sealed record RecepcionMaterialEnOcEvent(
    Guid OrdenCompraId,
    Guid LineaOrdenCompraId,
    Guid EmpresaId,
    decimal CantidadAcumulada,
    DateTimeOffset OcurridoEn) : INotification;
