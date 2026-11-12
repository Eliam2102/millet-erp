using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// Traspaso de producto terminado entre sucursales (M-249; Fernando Alejos y Laura Cerón, 11-nov): no es un préstamo, es un movimiento de inventario con cuenta puente.
/// <list type="bullet">
///   <item>Salida (se autoriza el traspaso): cargo INVENTARIO_POR_TRASPASO (115.05.01.00) · abono INVENTARIO_PT de la sucursal origen.</item>
///   <item>Entrada (la pieza llega a destino, regresa al origen o se da por perdida): cargo INVENTARIO_PT de la sucursal donde queda · abono INVENTARIO_POR_TRASPASO.</item>
/// </list>
/// El puente siempre termina en cero porque cada pieza que sale entra exactamente una vez. Los eventos no traen costo: los valúa
/// <see cref="EnriquecedorCostoTraspasoPt"/> con el mismo puerto de PCV; sin valuación esperan en la bandeja de rechazos como dato faltante.
/// El evento sin piezas sale sin movimiento. Documento = el traspaso; la llave por evento hace que cada salida o entrada tenga su póliza.
/// </summary>
public static partial class NormalizadorEventos
{
    public const string TraspasoPtSalidaAutorizada = "producto_terminado.traspaso.salida-autorizada.v1";
    public const string TraspasoPtEntradaRegistrada = "producto_terminado.traspaso.entrada-registrada.v1";

    public static readonly (string Modulo, string Tipo) DocTraspasoPt = ("producto_terminado", "traspaso");

    private static EventoContable? NormalizarTraspasoPt(string tipo, Guid eventoId, string payload) => tipo switch
    {
        TraspasoPtSalidaAutorizada or TraspasoPtEntradaRegistrada => MovimientoTraspasoPt(tipo, eventoId, payload),
        _ => null,
    };

    private static EventoContable MovimientoTraspasoPt(string tipo, Guid eventoId, string payload)
    {
        var e = LeerTraspasoPt(tipo, payload);
        var importes = e.Pedidos!.SelectMany(p => p.Posiciones ?? []).Any(p => p.Piezas > 0) ? new Dictionary<string, decimal?>() : new Dictionary<string, decimal?> { ["Total"] = 0m };
        return new(tipo, eventoId, e.EmpresaId, FechaContable.HoyLocal(e.OcurridoEn),
            Doc(DocTraspasoPt, e.TraspasoId, e.Folio), tipo == TraspasoPtSalidaAutorizada ? e.SucursalOrigenId : e.SucursalId, null, null, null, null,
            MonedaDe(null), null, importes, [], payload);
    }

    /// <summary>Contrato de los eventos de traspaso de PT (solo lo que usa Contabilidad).</summary>
    public static TraspasoPtContrato LeerTraspasoPt(string tipo, string payload)
    {
        var e = Leer<TraspasoPtContrato>(payload);
        if (e.TraspasoId == Guid.Empty || string.IsNullOrWhiteSpace(e.Folio))
            throw new EventoIlegibleException($"{tipo} sin traspaso o sin folio.");
        if ((tipo == TraspasoPtSalidaAutorizada ? e.SucursalOrigenId : e.SucursalId) is null)
            throw new EventoIlegibleException($"{tipo} sin la sucursal del movimiento.");
        return e with { Pedidos = e.Pedidos ?? [] };
    }

    /// <summary><c>SucursalOrigenId</c> solo viene en la salida y <c>SucursalId</c> solo en la entrada.</summary>
    public sealed record TraspasoPtContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid TraspasoId, string Folio, Guid? SucursalOrigenId, Guid? SucursalId,
        IReadOnlyList<PedidoTraspasoPt>? Pedidos);

    public sealed record PedidoTraspasoPt(Guid PedidoPtId, string NumeroPedido, IReadOnlyList<PosicionEntregadaPt>? Posiciones);
}
