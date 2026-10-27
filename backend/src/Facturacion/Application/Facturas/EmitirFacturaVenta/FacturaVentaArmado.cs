using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;

/// <summary>
/// Armado del agregado <see cref="FacturaVenta"/> desde el comando de emisión
/// (U1.8a): borrador + líneas + totales + Complemento de Comercio Exterior.
/// Es el ÚNICO lugar donde el comando se traduce al agregado, para que la
/// proforma del pedido (<c>GenerarProformaPedidoHandler</c>), la comparación
/// previa al timbrado y la emisión real (<see cref="EmitirFacturaVentaHandler"/>)
/// calculen importes, impuestos y redondeos exactamente igual.
///
/// <para>
/// No reserva folio, no valida catálogos ni receptor y no persiste: eso sigue
/// siendo responsabilidad del handler de emisión. La proforma lo usa con un
/// folio no fiscal (<c>"PROFORMA"</c>, consecutivo 0) y descarta el agregado.
/// </para>
/// </summary>
public static class FacturaVentaArmado
{
    /// <summary>Snapshot del receptor tal como lo congela la emisión (RFC genérico derivado del RFC).</summary>
    public static DatosFiscalesReceptor ReceptorDe(EmitirFacturaVentaCommand command) =>
        new(
            Rfc: command.ReceptorRfc,
            Nombre: command.ReceptorNombre,
            RegimenFiscal: command.ReceptorRegimenFiscal,
            CodigoPostal: command.ReceptorCodigoPostal,
            UsoCfdi: command.ReceptorUsoCfdi,
            Pais: command.ReceptorPais,
            EsGenerico: DatosFiscalesReceptor.EsRfcGenerico(command.ReceptorRfc));

    /// <summary>
    /// Construye la factura (borrador) con sus líneas, totales y CCE si el
    /// comando lo trae (antes de timbrar — va en el XML; <c>AdjuntarCce</c>
    /// valida que el comportamiento sea <c>ExportacionConCce</c>).
    /// </summary>
    public static FacturaVenta Construir(
        EmitirFacturaVentaCommand command,
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        int anio,
        int mes)
    {
        var factura = FacturaVenta.CrearBorrador(
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: command.SucursalId,
            // CAJAS-PR4 (§6): CajaId ya no viene del comando — lo asigna
            // RegistrarCobroMostrador vía AsignarCajaCobro (Capa B).
            cajaId: null,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            metodoPago: command.MetodoPago,
            formaPago: command.FormaPago,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            periodoAnio: anio,
            periodoMes: mes,
            canalVentaId: command.CanalVenta,
            comportamientoFiscal: command.ComportamientoFiscal,
            pedidoFacturableId: command.PedidoFacturableId,
            obraId: command.ObraId,
            obraNombre: command.ObraNombre,
            facturaAgrupada: command.FacturaAgrupada);

        foreach (var l in command.Lineas)
        {
            factura.AgregarLinea(
                productoId: l.ProductoId,
                claveProdServSat: l.ClaveProdServSat,
                descripcion: l.Descripcion,
                claveUnidadSat: l.ClaveUnidadSat,
                cantidad: l.Cantidad,
                valorUnitario: l.ValorUnitario,
                descuento: l.Descuento,
                objetoImp: l.ObjetoImp,
                tasaIvaTraslado: l.TasaIvaTraslado,
                tasaRetencionIva: l.TasaRetencionIva,
                tasaRetencionIsr: l.TasaRetencionIsr,
                requierePedimento: l.RequierePedimento, referencia: l.Referencia, pesoKg: l.PesoKg, areaPie2: l.AreaPie2);
        }

        factura.RecalcularTotales();

        // F7-PR1: Complemento de Comercio Exterior.
        if (command.Cce is { } cceDatos)
        {
            var cce = ComplementoCce.Crear(
                factura.Id, cceDatos.TipoOperacion, cceDatos.Incoterm, cceDatos.TcDof,
                cceDatos.ReceptorNumRegIdTrib, cceDatos.ReceptorPaisResidencia,
                cceDatos.ClaveDePedimento, cceDatos.CertificadoOrigen,
                cceDatos.ReceptorDomicilioCalle, cceDatos.ReceptorDomicilioEstado,
                cceDatos.ReceptorDomicilioCodigoPostal);
            foreach (var l in cceDatos.Lineas)
                cce.AgregarLinea(l.FraccionArancelaria, l.UnidadAduana, l.CantidadAduana, l.ValorUnitarioAduana, l.ValorDolares, l.AplicaIva0);
            factura.AdjuntarCce(cce);
        }

        return factura;
    }
}
