using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// C1.5b (86e3vrh7y, E11) · caja chica, viáticos, tarjetas corporativas y amortización del anticipo a proveedor (Módulo 10 R9;
/// Módulo 08 R9, R12, R14 y R15). Mismo patrón que C1.5: espejo del contrato JSON de CxP e importes con nombre para la plantilla.
/// <para>
/// <c>gasto.registrado</c> y <c>pasivo.autorizado-para-pago</c> traen varios orígenes en un mismo tipo de evento: la plantilla es una y
/// la contrapartida sale del rol por clasificador (<c>CONTRAPARTIDA_GASTO_CAJA_CHICA</c>, <c>_VIATICOS</c>, <c>_TARJETA</c>). Así el
/// tipo del evento no cambia y «Reprocesar» de la bandeja sigue funcionando. Cuentas DEMO mientras Contabilidad mapea V12.
/// </para>
/// </summary>
public static partial class NormalizadorEventos
{
    public const string GastoRegistrado = "cuentas_por_pagar.gasto.registrado.v1";
    public const string PasivoAutorizadoParaPago = "cuentas_por_pagar.pasivo.autorizado-para-pago.v1";
    public const string EstadoCuentaTcCerrado = "cuentas_por_pagar.estado-cuenta-tc.cerrado.v1";
    public const string AnticipoProveedorAmortizado = "cuentas_por_pagar.anticipo.amortizado.v1";

    public static readonly (string Modulo, string Tipo) DocGasto = ("cuentas_por_pagar", "gasto");
    public static readonly (string Modulo, string Tipo) DocPasivoInterno = ("cuentas_por_pagar", "pasivo_interno");
    public static readonly (string Modulo, string Tipo) DocAmortizacionAnticipo = ("cuentas_por_pagar", "amortizacion_anticipo");

    /// <summary>Clasificadores de la contrapartida (sufijo del rol <c>CONTRAPARTIDA_GASTO</c>).</summary>
    public const string OrigenCajaChica = "caja_chica";
    public const string OrigenViaticos = "viaticos";
    public const string OrigenTarjeta = "tarjeta";

    private static EventoContable? NormalizarC15b(string tipo, Guid eventoId, string payload) => tipo switch
    {
        GastoRegistrado => Gasto(eventoId, payload),
        PasivoAutorizadoParaPago => PasivoInterno(eventoId, payload),
        EstadoCuentaTcCerrado => EstadoCuentaTarjeta(eventoId, payload),
        AnticipoProveedorAmortizado => AmortizacionAnticipo(eventoId, payload),
        _ => null,
    };

    /// <summary>
    /// GC: gasto comprobado de caja chica, viáticos o tarjeta (una línea o un movimiento por evento). Cargo GASTO (todo lo que no es IVA
    /// acreditable: subtotal, IVA no acreditable de los no deducibles y otros impuestos) e IVA_ACREDITABLE_PAGADO (H1: el gasto lleva el área o equipo capturado, si lo hay) · abono a la
    /// contrapartida del origen por el total: fondo de caja chica, deudor del empleado o tarjeta por pagar. La captura retroactiva desde
    /// el estado de cuenta es un movimiento de tarjeta.
    /// </summary>
    private static EventoContable Gasto(Guid eventoId, string payload)
    {
        var g = Leer<GastoContrato>(payload);
        var ivaAcreditable = g.IvaTrasladado - g.IvaNoAcreditable;
        var origen = g.Origen == "estado_cuenta" ? OrigenTarjeta : g.Origen;
        return new(GastoRegistrado, eventoId, g.EmpresaId, FechaContable.HoyLocal(g.OcurridoEn),
            Doc(DocGasto, g.LineaId ?? g.DocumentoId, null, g.Uuid), g.SucursalId, g.ProveedorId, null, g.CentroCostoId, null,
            MonedaDe(g.Moneda), g.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["Gasto"] = g.Total - ivaAcreditable, ["IvaAcreditable"] = ivaAcreditable, ["Total"] = g.Total,
            },
            [new LineaEventoContable(null, g.Total, null, false, Clasificador: origen)], payload);
    }

    /// <summary>
    /// PI: pasivo interno hacia Tesorería. Reposición de caja chica → cargo al fondo; préstamo o liquidación de viáticos → cargo al
    /// deudor del empleado; abono ACREEDORES_DIVERSOS. El pasivo de una factura de proveedor no se contabiliza aquí (ya tiene su póliza
    /// TT): sale sin movimiento. Llave por origen: el mismo pasivo publicado dos veces no duplica la póliza.
    /// </summary>
    private static EventoContable PasivoInterno(Guid eventoId, string payload)
    {
        var p = Leer<PasivoInternoContrato>(payload);
        var origen = p.OrigenTipo switch
        {
            "ReposicionCajaChica" => OrigenCajaChica,
            "PrestamoViaticos" or "LiquidacionViaticos" => OrigenViaticos,
            _ => null,
        };
        var monto = origen is null ? 0m : p.MontoTotal;
        var documento = p.OrigenId is { } id && id != Guid.Empty ? id : eventoId;
        return new(PasivoAutorizadoParaPago, eventoId, p.EmpresaId, FechaContable.HoyLocal(p.OcurridoEn),
            Doc(DocPasivoInterno, documento, null), p.SucursalId, null, null, null, null,
            MonedaDe(p.Moneda), p.TipoCambio, new Dictionary<string, decimal?> { ["Total"] = monto },
            origen is null ? [] : [new LineaEventoContable(null, monto, null, false, Clasificador: origen)], payload,
            LlaveOverride: origen is null ? null : $"{PasivoAutorizadoParaPago}:{p.OrigenTipo}:{documento}");
    }

    /// <summary>
    /// TC: cierre del estado de cuenta. La deuda acumulada con cada movimiento pasa a la factura agregada del banco emisor: cargo
    /// CONTRAPARTIDA_GASTO_TARJETA · abono PROVEEDORES (auxiliar = banco). Documento = la factura agregada, para que su pago (#5) y su
    /// cancelación encuentren esta póliza. La diferencia cambiaria del corte queda para CON-11.
    /// </summary>
    private static EventoContable EstadoCuentaTarjeta(Guid eventoId, string payload)
    {
        var c = Leer<EstadoCuentaTcCerradoContrato>(payload);
        return Mxn(EstadoCuentaTcCerrado, eventoId, c.EmpresaId, FechaContable.HoyLocal(c.OcurridoEn),
            Doc(DocFacturaProveedor, c.FacturaProveedorId, null), c.SucursalId,
            importes: new() { ["Total"] = c.TotalBancoMxn }, lineas: [], payload: payload, proveedorId: c.ProveedorId);
    }

    /// <summary>
    /// AA: amortización del anticipo a proveedor al aplicarlo a una factura. Cargo PROVEEDORES (auxiliar = proveedor) · abono
    /// ANTICIPO_A_PROVEEDORES. Sin sucursal en el evento, el enriquecedor la toma de la póliza de la factura.
    /// </summary>
    private static EventoContable AmortizacionAnticipo(Guid eventoId, string payload)
    {
        var a = Leer<AnticipoAmortizadoContrato>(payload);
        return new(AnticipoProveedorAmortizado, eventoId, a.EmpresaId, FechaContable.HoyLocal(a.OcurridoEn),
            Doc(DocAmortizacionAnticipo, a.AplicacionId, null, a.UuidFactura), a.SucursalId, a.ProveedorId, null, null, null,
            MonedaDe(a.Moneda), a.TipoCambio, new Dictionary<string, decimal?> { ["Total"] = a.Monto }, [], payload,
            FacturaOrigenId: a.SucursalId is null ? a.FacturaProveedorId : null);
    }

    // ---- Espejos de contrato (solo los campos que usa Contabilidad) ----

    private sealed record GastoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, string Origen, Guid DocumentoId, Guid? LineaId,
        Guid? ProveedorId, string? Uuid, decimal IvaTrasladado, decimal IvaNoAcreditable, decimal Total, string? Moneda,
        decimal? TipoCambio, Guid? SucursalId, Guid? CentroCostoId = null);

    private sealed record PasivoInternoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, decimal MontoTotal, string? Moneda,
        decimal? TipoCambio, string? OrigenTipo, Guid? OrigenId, Guid? SucursalId);

    private sealed record EstadoCuentaTcCerradoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaProveedorId,
        decimal TotalBancoMxn, Guid? ProveedorId, Guid? SucursalId);

    private sealed record AnticipoAmortizadoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid AplicacionId, Guid FacturaProveedorId,
        Guid ProveedorId, decimal Monto, string? Moneda, decimal? TipoCambio, Guid? SucursalId, string? UuidFactura);
}
