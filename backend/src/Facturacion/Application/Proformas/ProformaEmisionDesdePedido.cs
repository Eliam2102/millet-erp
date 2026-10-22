using System.Globalization;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Proformas;

/// <summary>Comando de emisión equivalente al pedido + avisos de datos fiscales incompletos.</summary>
public sealed record ProformaEmisionArmada(EmitirFacturaVentaCommand Comando, IReadOnlyList<string> Avisos);

/// <summary>
/// Traduce el detalle de un pedido facturable al <see cref="EmitirFacturaVentaCommand"/>
/// que armaría el formulario de emisión al facturar desde ese pedido (U1.8a),
/// con las MISMAS reglas de prellenado del frontend
/// (<c>emitir-factura/valores.ts</c> + <c>pages/DetallePedido.tsx</c>):
/// receptor = cliente del pedido + datos fiscales vivos del master; uso
/// <c>G03</c>, método <c>PUE</c> y forma <c>01</c> por omisión; unidad
/// <c>H87</c>, objeto de impuesto <c>02</c> y tasa de IVA línea → master →
/// default de la empresa → 0.16; venta directa de activo como venta de activo
/// fijo.
///
/// <para>
/// A diferencia de la emisión, NO bloquea por datos fiscales incompletos: los
/// sustituye por <see cref="SinDato"/> y los reporta en los avisos para que la
/// proforma pueda entregarse y el faltante quede visible.
/// </para>
/// </summary>
public static class ProformaEmisionDesdePedido
{
    /// <summary>Marcador visible para datos obligatorios que faltan en el master.</summary>
    public const string SinDato = "PENDIENTE";

    /// <summary>Folio no fiscal del agregado armado en memoria (nunca se persiste ni se timbra).</summary>
    public const string FolioArmado = "PROFORMA";

    private const string UsoCfdiDefault = "G03";
    private const string MetodoPagoDefault = "PUE";
    private const string FormaPagoDefault = "01";
    private const string ClaveUnidadDefault = "H87";
    private const string ObjetoImpDefault = "02";
    private const decimal TasaIvaHistorica = 0.16m;

    public static ProformaEmisionArmada Armar(PedidoFacturableDetalleResponse pedido, EmpresaFiscalLectura emisor)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(emisor);
        var avisos = new List<string>();
        var cliente = pedido.ClienteFiscal;

        if (cliente is null)
            avisos.Add("El cliente no está en el maestro de clientes: faltan sus datos fiscales.");

        var rfc = Requerido(cliente?.Rfc, "Receptor sin RFC.", avisos);
        var regimen = Requerido(cliente?.RegimenFiscal, "Receptor sin régimen fiscal.", avisos);
        var cp = Requerido(cliente?.CodigoPostalFiscal, "Receptor sin código postal fiscal.", avisos);

        if (pedido.Origen == nameof(OrigenPedido.Manual) && pedido.ClaseManual is null)
            avisos.Add("Pedido manual sin clasificar: clasifícalo (venta directa o emergencia) antes de timbrar.");

        var moneda = pedido.Moneda.ToUpperInvariant();
        if (moneda != "MXN")
            avisos.Add(string.Create(CultureInfo.InvariantCulture,
                $"Moneda {moneda}: el tipo de cambio se fija al timbrar."));

        var comportamiento = Enum.TryParse<ComportamientoFiscal>(pedido.ComportamientoFiscal, out var c)
            ? c
            : ComportamientoFiscal.MostradorInmediato;
        // F6.1: una venta directa de un activo se factura siempre como venta de activo fijo.
        if (pedido.SubtipoVentaDirecta == nameof(SubtipoVentaDirecta.Activo))
            comportamiento = ComportamientoFiscal.VentaActivoFijo;

        var lineas = pedido.Lineas
            .OrderBy(l => l.Posicion)
            .Select(l =>
            {
                var claveProdServ = l.ClaveProdServSat;
                if (string.IsNullOrWhiteSpace(claveProdServ))
                {
                    avisos.Add(string.Create(CultureInfo.InvariantCulture,
                        $"Concepto {l.Posicion} sin clave de producto/servicio SAT."));
                    claveProdServ = SinDato;
                }

                return new EmitirFacturaVentaLinea(
                    ProductoId: l.ProductoId,
                    ClaveProdServSat: claveProdServ,
                    Descripcion: l.ProductoDescripcion,
                    ClaveUnidadSat: string.IsNullOrWhiteSpace(l.ClaveUnidadSat) ? ClaveUnidadDefault : l.ClaveUnidadSat,
                    Cantidad: l.Cantidad,
                    ValorUnitario: l.Precio,
                    Descuento: l.Descuento,
                    ObjetoImp: string.IsNullOrWhiteSpace(l.ObjetoImp) ? ObjetoImpDefault : l.ObjetoImp,
                    TasaIvaTraslado: l.TasaIvaTraslado ?? emisor.TasaIvaDefault ?? TasaIvaHistorica,
                    TasaRetencionIva: l.TasaRetencionIva,
                    TasaRetencionIsr: l.TasaRetencionIsr,
                    RequierePedimento: l.RequierePedimento);
            })
            .ToList();

        var comando = new EmitirFacturaVentaCommand(
            SucursalId: pedido.SucursalId,
            ReceptorRfc: rfc.ToUpperInvariant(),
            ReceptorNombre: pedido.ClienteNombre,
            ReceptorRegimenFiscal: regimen,
            ReceptorCodigoPostal: cp,
            ReceptorUsoCfdi: Opcional(cliente?.UsoCfdiDefault) ?? UsoCfdiDefault,
            ReceptorPais: "MEX",
            RfcEmisor: emisor.Rfc.ToUpperInvariant(),
            RegimenFiscalEmisor: emisor.RegimenFiscal,
            MetodoPago: Opcional(cliente?.MetodoPagoDefault) ?? MetodoPagoDefault,
            FormaPago: Opcional(cliente?.FormaPagoDefault) ?? FormaPagoDefault,
            Moneda: moneda,
            TipoCambio: null,
            CanalVenta: pedido.CanalVentaId,
            ComportamientoFiscal: comportamiento,
            ObraId: null,
            ObraNombre: Opcional(pedido.ObraNombre),
            FacturaAgrupada: false,
            Lineas: lineas,
            PedidoFacturableId: pedido.Id,
            ClienteId: pedido.ClienteId);

        return new ProformaEmisionArmada(comando, avisos);
    }

    /// <summary>Tipo de serie con la que se timbrará el pedido (F6.1: manual clasificado → VentaDirecta).</summary>
    public static string SeriePrevista(bool esVentaDirectaManual) =>
        esVentaDirectaManual ? "VentaDirecta" : "Cfdi";

    private static string Requerido(string? valor, string aviso, List<string> avisos)
    {
        if (!string.IsNullOrWhiteSpace(valor)) return valor.Trim();
        avisos.Add(aviso);
        return SinDato;
    }

    private static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
