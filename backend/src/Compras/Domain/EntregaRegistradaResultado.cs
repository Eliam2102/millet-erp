namespace Millet.Compras.Domain;

/// <summary>
/// Resultado de <see cref="Requisicion.RegistrarEntrega"/> (ADR-0043, canal
/// de entrega Almacén→Compras).
///
/// <para>El dominio no loggea (no tiene <c>ILogger</c>): cuando la entrega
/// excede el techo físico disponible la entrega NO lanza (decisión robusta)
/// pero lo <b>señala</b> aquí, para que el handler de Application emita la
/// advertencia estructurada con <c>RqId</c>/<c>LineaRqId</c>/montos.</para>
/// </summary>
/// <param name="CierreEvento">
/// <see cref="Events.RequisicionCerradaEvent"/> si esta entrega completó la
/// RQ (todas las líneas con <c>CantidadEntregada &gt;= Cantidad</c>) estando
/// en <see cref="EstadoRequisicion.EnSurtido"/>; <c>null</c> si no cerró. En
/// el PR #1 queda dormido (el cierre viejo gana antes).
/// </param>
/// <param name="ExcedioTecho">
/// <c>true</c> si el total entregado de la línea superó el techo físico
/// disponible (<c>CantidadDeAlmacen + CantidadRecibida</c>). El handler
/// emite una advertencia; no es un error.
/// </param>
/// <param name="LineaId">Línea sobre la que se aplicó la entrega.</param>
/// <param name="TotalEntregado">Acumulado de la línea tras esta entrega.</param>
/// <param name="TechoDisponible">Máximo físicamente entregable al momento.</param>
public sealed record EntregaRegistradaResultado(
    Events.RequisicionCerradaEvent? CierreEvento,
    bool ExcedioTecho,
    Guid LineaId,
    decimal TotalEntregado,
    decimal TechoDisponible);
