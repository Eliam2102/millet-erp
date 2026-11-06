using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// PCV (86e3ke10b) · póliza de costo de ventas al confirmarse la entrega de producto terminado (PT5.6). Lo recibido por posición es lo
/// que reconoce el costo: una entrega parcial de 20 de 30 piezas lleva solo esas 20. El evento no trae costo ni sucursal: los pone
/// <see cref="EnriquecedorCostoVentasPt"/> desde la valuación de PT (PT5.12); mientras no exista, el evento espera en la bandeja de
/// rechazos como dato faltante y se reprocesa desde ahí.
/// <para>
/// Documento = el pedido de PT (el evento no trae id del albarán); la llave por pedido y albarán hace que la misma entrega no duplique la
/// póliza aunque el evento llegue dos veces o se reprocese, y que cada entrega parcial del pedido tenga la suya.
/// </para>
/// </summary>
public static partial class NormalizadorEventos
{
    public const string EntregaPtConfirmada = "producto_terminado.entrega.confirmada.v1";

    public static readonly (string Modulo, string Tipo) DocEntregaPt = ("producto_terminado", "entrega");

    private static EventoContable? NormalizarPcv(string tipo, Guid eventoId, string payload) =>
        tipo == EntregaPtConfirmada ? CostoVentasPt(eventoId, payload) : null;

    /// <summary>
    /// SM: cargo COSTO_DE_VENTAS (rol por tipo de producto si existe) · abono INVENTARIO_PT, una línea por posición entregada. Importes
    /// vacíos a propósito: sin costo el evento no es «sin movimiento», va al motor y el enriquecedor decide (costo o dato faltante).
    /// Una entrega sin piezas recibidas (rechazo total en sitio, PT5.8) no mueve inventario: sale sin movimiento.
    /// </summary>
    private static EventoContable CostoVentasPt(Guid eventoId, string payload)
    {
        var e = LeerEntregaPt(payload);
        var importes = e.Entregadas!.Any(p => p.Piezas > 0) ? new Dictionary<string, decimal?>() : new Dictionary<string, decimal?> { ["Total"] = 0m };
        return new(EntregaPtConfirmada, eventoId, e.EmpresaId, FechaContable.HoyLocal(e.OcurridoEn),
            Doc(DocEntregaPt, e.PedidoPtId, $"{e.NumeroPedido} · {e.AlbaranFolio}"), null, null, null, null, null,
            MonedaDe(null), null, importes, [], payload,
            LlaveOverride: LlaveEntregaPt(e.PedidoPtId, e.AlbaranFolio));
    }

    /// <summary>Una póliza por albarán del pedido: la entrega repetida o reprocesada no se duplica.</summary>
    public static string LlaveEntregaPt(Guid pedidoPtId, string albaranFolio) => $"{EntregaPtConfirmada}:{pedidoPtId}:{albaranFolio}";

    /// <summary>Contrato de <c>producto_terminado.entrega.confirmada.v1</c> (solo lo que usa Contabilidad).</summary>
    public static EntregaPtContrato LeerEntregaPt(string payload)
    {
        var e = Leer<EntregaPtContrato>(payload);
        if (e.PedidoPtId == Guid.Empty || string.IsNullOrWhiteSpace(e.AlbaranFolio))
            throw new EventoIlegibleException($"{EntregaPtConfirmada} sin pedido o sin folio de albarán.");
        return e with { Entregadas = e.Entregadas ?? [] };
    }

    public sealed record EntregaPtContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid PedidoPtId, string NumeroPedido,
        string AlbaranFolio, IReadOnlyList<PosicionEntregadaPt>? Entregadas);

    public sealed record PosicionEntregadaPt(int Posicion, int Piezas);
}
