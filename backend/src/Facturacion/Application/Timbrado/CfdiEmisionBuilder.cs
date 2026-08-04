using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
using DominioCartaPorte = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>
/// Construye la solicitud estructurada <see cref="CfdiEmision"/> desde los
/// agregados del módulo (F12-PR1 — reemplaza los <c>ConstruirXmlPlaceholder</c>
/// dispersos en los handlers). Es el único punto donde el dominio se traduce al
/// contrato PAC-neutral del puerto <c>ICfdiTimbradoPort</c>.
///
/// <para>Responsabilidades transversales que concentra:</para>
/// <list type="bullet">
/// <item><b>Fecha fiscal</b>: convierte a <c>America/Mexico_City</c> (el SDK
/// del PAC no convierte husos; el SAT rechaza fechas fuera de rango).</item>
/// <item><b>Tasas a 6 decimales</b> (CFDI40179).</item>
/// <item><b>Split Serie/Folio</b>: el folio interno formateado
/// (<c>{prefijo}-{consecutivo}</c>, Compartido.Series) se separa en los
/// atributos XML <c>Serie</c>/<c>Folio</c> por el último guion.</item>
/// </list>
/// </summary>
public static class CfdiEmisionBuilder
{
    /// <summary>Zona horaria fiscal de México (IANA; .NET la resuelve también en Windows vía ICU).</summary>
    private static readonly TimeZoneInfo ZonaFiscal =
        TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    /// <summary>Clave SAT del concepto fijo de servicios de facturación (anticipos/NC, §6.2).</summary>
    private const string ObjetoImpGravado = "02";

    public static CfdiEmision DesdeFacturaVenta(FacturaVenta factura, DateTimeOffset ahoraUtc)
    {
        var lineasOrdenadas = factura.Lineas.OrderBy(l => l.Posicion).ToList();

        // En exportación (CCE) cada mercancía del complemento se liga a su
        // concepto por NoIdentificacion == ItemSku (contrato FiscalAPI, ver
        // ComercioExteriorValuesForm del SDK oficial: A0001↔A0001). Se asigna
        // un SKU explícito y único por línea solo en exportación (P10-H2); el
        // resto de comprobantes conserva NoIdentificacion nulo → ItemSku =
        // ClaveProdServ, sin cambio de comportamiento.
        var esExportacion = factura.ComplementoCce is not null;

        var conceptos = lineasOrdenadas
            .Select(l => new ConceptoCfdi(
                ClaveProdServ: l.ClaveProdServSat,
                ClaveUnidad: l.ClaveUnidadSat,
                Cantidad: l.Cantidad,
                Descripcion: l.Descripcion,
                ValorUnitario: l.ValorUnitario,
                Importe: l.Importe,
                Descuento: l.Descuento,
                ObjetoImp: l.ObjetoImp,
                Impuestos: ImpuestosDeLinea(l),
                Pedimento: l.Pedimento is null
                    ? null
                    : new PedimentoCfdi(l.Pedimento, l.FechaDocAduanero, l.IdentificacionMercancia),
                NoIdentificacion: esExportacion ? NoIdentificacionCce(l) : null))
            .ToList();

        var cce = ConstruirCce(factura, lineasOrdenadas);

        return new CfdiEmision(
            EmpresaId: factura.EmpresaId,
            Tipo: TipoCfdi.Ingreso,
            Emisor: EmisorDe(factura),
            Receptor: ReceptorDe(factura, numRegIdTrib: cce?.ReceptorNumRegIdTrib),
            Serie: SerieDe(factura),
            Folio: FolioDe(factura),
            FechaLocal: FechaFiscal(ahoraUtc),
            Moneda: factura.Moneda,
            TipoCambio: factura.TipoCambio,
            FormaPago: factura.FormaPago,
            MetodoPago: factura.MetodoPago,
            Exportacion: factura.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce ? "02" : "01",
            Conceptos: conceptos,
            Relaciones: RelacionesDe(factura),
            ComplementoCce: cce,
            ReferenciaInterna: factura.Id);
    }

    public static CfdiEmision DesdeNotaCredito(NotaCredito nc, DateTimeOffset ahoraUtc)
    {
        return new CfdiEmision(
            EmpresaId: nc.EmpresaId,
            Tipo: TipoCfdi.Egreso,
            Emisor: EmisorDe(nc),
            Receptor: ReceptorDe(nc),
            Serie: SerieDe(nc),
            Folio: FolioDe(nc),
            FechaLocal: FechaFiscal(ahoraUtc),
            Moneda: nc.Moneda,
            TipoCambio: nc.TipoCambio,
            FormaPago: nc.FormaPago,
            MetodoPago: nc.MetodoPago,
            Exportacion: "01",
            Conceptos: [ConceptoUnico(nc.ClaveProdServSat, nc.ClaveUnidadSat, nc.Descripcion, nc.Subtotal, nc.ImpuestosTrasladados)],
            Relaciones: RelacionesDe(nc),
            ReferenciaInterna: nc.Id);
    }

    public static CfdiEmision DesdeFacturaAnticipo(FacturaAnticipo factura, DateTimeOffset ahoraUtc)
    {
        return new CfdiEmision(
            EmpresaId: factura.EmpresaId,
            Tipo: TipoCfdi.Ingreso,
            Emisor: EmisorDe(factura),
            Receptor: ReceptorDe(factura),
            Serie: SerieDe(factura),
            Folio: FolioDe(factura),
            FechaLocal: FechaFiscal(ahoraUtc),
            Moneda: factura.Moneda,
            TipoCambio: factura.TipoCambio,
            FormaPago: factura.FormaPago,
            MetodoPago: factura.MetodoPago,
            Exportacion: "01",
            Conceptos: [ConceptoUnico(factura.ClaveProdServSat, factura.ClaveUnidadSat, factura.Descripcion, factura.Subtotal, factura.ImpuestosTrasladados)],
            Relaciones: RelacionesDe(factura),
            ReferenciaInterna: factura.Id);
    }

    /// <summary>
    /// REPP (CFDI tipo P): comprobante sin importes (totales 0, moneda XXX) con
    /// el concepto fijo que exige el SAT y el complemento Pago 2.0.
    /// </summary>
    public static CfdiEmision DesdeReciboPago(ReciboPago repp, DateTimeOffset ahoraUtc)
    {
        var primeraForma = repp.FacturasPagadas
            .Select(f => f.FormaPagoReal)
            .FirstOrDefault() ?? "03";

        var complementoPago = new ComplementoPagoCfdi(
            FechaPago: repp.FechaPago,
            FormaPago: primeraForma,
            Moneda: repp.MonedaPago,
            TipoCambio: repp.FacturasPagadas.Select(f => f.TcPago).FirstOrDefault(),
            Monto: repp.ImporteTotalPago,
            CuentaOrdenante: repp.FacturasPagadas.Select(f => f.CuentaOrdenante).FirstOrDefault(),
            CuentaBeneficiaria: repp.FacturasPagadas.Select(f => f.CuentaBeneficiaria).FirstOrDefault(),
            ReferenciaPago: repp.FacturasPagadas.Select(f => f.ReferenciaPago).FirstOrDefault(),
            Documentos: repp.FacturasPagadas
                .Select(f => new DocumentoPagoCfdi(
                    FacturaUuid: f.FacturaUuid,
                    Serie: null,
                    Folio: null,
                    Moneda: f.MonedaFactura,
                    NumParcialidad: f.NumParcialidad,
                    SaldoAnterior: f.SaldoAnterior,
                    ImportePagado: f.ImportePagado,
                    SaldoInsoluto: f.SaldoInsoluto,
                    ObjetoImpDR: f.ObjetoImpDR,
                    Equivalencia: f.Equivalencia,
                    Subtotal: f.BaseGravablePagada,
                    Impuestos: f.Impuestos
                        .Select(i => new ImpuestoDocumentoPagoCfdi(
                            i.Impuesto, i.TipoFactor, i.TasaOCuota, i.EsRetencion, i.BaseDR))
                        .ToList()))
                .ToList());

        // Concepto fijo del CFDI tipo P (Pago 2.0): cantidad 1, valor 0,
        // ClaveProdServ 84111506, ClaveUnidad ACT, descripción "Pago".
        var conceptoPago = new ConceptoCfdi(
            ClaveProdServ: "84111506",
            ClaveUnidad: "ACT",
            Cantidad: 1m,
            Descripcion: "Pago",
            ValorUnitario: 0m,
            Importe: 0m,
            Descuento: 0m,
            ObjetoImp: "01",
            Impuestos: []);

        return new CfdiEmision(
            EmpresaId: repp.EmpresaId,
            Tipo: TipoCfdi.Pago,
            Emisor: EmisorDe(repp),
            // CFDI tipo P: el SAT exige UsoCFDI = CP01 (Pagos) sin importar el
            // uso de las facturas cubiertas; el snapshot del receptor en el
            // comprobante conserva el uso original.
            Receptor: ReceptorDe(repp) with { UsoCfdi = "CP01" },
            Serie: SerieDe(repp),
            Folio: FolioDe(repp),
            FechaLocal: FechaFiscal(ahoraUtc),
            Moneda: repp.Moneda,           // "XXX" por regla SAT del tipo P
            TipoCambio: null,
            FormaPago: repp.FormaPago,
            MetodoPago: repp.MetodoPago,
            Exportacion: "01",
            Conceptos: [conceptoPago],
            Relaciones: RelacionesDe(repp),
            ComplementoPago: complementoPago,
            ReferenciaInterna: repp.Id);
    }

    /// <summary>
    /// Carta Porte 3.1: tipo T (mercancía propia, conceptos = mercancías con
    /// valor 0) o tipo I (concepto = servicio de transporte facturado). El
    /// vehículo y el operador se pasan resueltos (catálogos del módulo).
    /// Valida pre-vuelo los datos que el SAT exige (domicilio de las
    /// ubicaciones + peso bruto vehicular) — falla ANTES de quemar el timbre.
    /// </summary>
    public static CfdiEmision DesdeCartaPorte(
        DominioCartaPorte.CartaPorte cp,
        DominioCartaPorte.Vehiculo vehiculo,
        DominioCartaPorte.Operador operador,
        DateTimeOffset ahoraUtc)
    {
        if (string.IsNullOrWhiteSpace(cp.OrigenCodigoPostal) || string.IsNullOrWhiteSpace(cp.OrigenEstado)
            || string.IsNullOrWhiteSpace(cp.DestinoCodigoPostal) || string.IsNullOrWhiteSpace(cp.DestinoEstado)
            || vehiculo.PesoBrutoVehicular is not > 0)
        {
            throw new BusinessRuleException(
                "CARTA_PORTE_DATOS_SAT_INCOMPLETOS",
                "La Carta Porte 3.1 exige código postal y estado (c_Estado) del origen y del destino, " +
                "y el peso bruto vehicular (toneladas) del vehículo. Captura los datos faltantes y vuelve a emitir.");
        }

        var complemento = new ComplementoCartaPorteCfdi(
            Origen: new UbicacionCartaPorteCfdi(cp.Origen, cp.OrigenCodigoPostal!, cp.OrigenEstado!, "MEX"),
            Destino: new UbicacionCartaPorteCfdi(cp.Destino, cp.DestinoCodigoPostal!, cp.DestinoEstado!, "MEX"),
            RfcRemitente: cp.RfcEmisor,
            NombreRemitente: cp.NombreEmisor,
            RfcDestinatario: cp.ReceptorRfc,
            NombreDestinatario: cp.ReceptorNombre,
            DistanciaKm: cp.DistanciaKm,
            FechaSalida: cp.FechaSalida,
            FechaLlegadaEstimada: cp.FechaLlegadaEstimada,
            Vehiculo: new VehiculoCartaPorteCfdi(
                vehiculo.Placa, vehiculo.ConfigVehicular, vehiculo.AnioModelo, vehiculo.PesoBrutoVehicular,
                vehiculo.TipoPermisoSct, vehiculo.NumPermisoSct, vehiculo.Aseguradora, vehiculo.PolizaSeguro),
            Operador: new OperadorCartaPorteCfdi(operador.Rfc, operador.Nombre, operador.NumLicencia),
            IdCcpRelacionado: cp.CartaPortePreviaId?.ToString("D"),
            Mercancias: cp.Mercancias
                .Select(m => new MercanciaCartaPorteCfdi(
                    m.BienesTransp, m.Descripcion, m.ClaveUnidad, m.Cantidad, m.PesoEnKg, m.MaterialPeligroso))
                .ToList());

        IReadOnlyList<ConceptoCfdi> conceptos = cp.Tipo == TipoComprobante.Ingreso
            // Tipo I: se factura el servicio de transporte (un concepto).
            ? [ConceptoUnico("78101800", "E48", "Servicio de transporte de carga", cp.Subtotal, cp.ImpuestosTrasladados)]
            // Tipo T: los conceptos espejan las mercancías con valor 0 (no objeto de impuesto).
            : cp.Mercancias
                .Select(m => new ConceptoCfdi(
                    ClaveProdServ: m.BienesTransp,
                    ClaveUnidad: m.ClaveUnidad,
                    Cantidad: m.Cantidad,
                    Descripcion: m.Descripcion,
                    ValorUnitario: 0m,
                    Importe: 0m,
                    Descuento: 0m,
                    ObjetoImp: "01",
                    Impuestos: []))
                .ToList();

        return new CfdiEmision(
            EmpresaId: cp.EmpresaId,
            Tipo: cp.Tipo == TipoComprobante.Ingreso ? TipoCfdi.Ingreso : TipoCfdi.Traslado,
            Emisor: EmisorDe(cp),
            Receptor: ReceptorDe(cp),
            Serie: SerieDe(cp),
            Folio: FolioDe(cp),
            FechaLocal: FechaFiscal(ahoraUtc),
            Moneda: cp.Moneda,
            TipoCambio: cp.TipoCambio,
            FormaPago: cp.FormaPago,
            MetodoPago: cp.MetodoPago,
            Exportacion: "01",
            Conceptos: conceptos,
            Relaciones: RelacionesDe(cp),
            ComplementoCartaPorte: complemento,
            ReferenciaInterna: cp.Id);
    }

    // ---- Helpers ----

    /// <summary>Fecha de emisión en la zona horaria fiscal (America/Mexico_City).</summary>
    public static DateTimeOffset FechaFiscal(DateTimeOffset ahoraUtc) =>
        TimeZoneInfo.ConvertTime(ahoraUtc, ZonaFiscal);

    /// <summary>
    /// Serie del CFDI = folio interno formateado sin el consecutivo final
    /// (formato Compartido.Series: <c>{prefijo}[{sufijo}|-{periodo}]-{D6}</c>).
    /// </summary>
    public static string SerieDe(Comprobante c)
    {
        var idx = c.Folio.LastIndexOf('-');
        return idx > 0 ? c.Folio[..idx] : c.Folio;
    }

    /// <summary>Folio numérico del CFDI (consecutivo de la serie).</summary>
    public static string FolioDe(Comprobante c) =>
        c.FolioNumero.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static EmisorCfdi EmisorDe(Comprobante c)
    {
        var emisor = c.SnapshotEmisor().Validar();
        return new EmisorCfdi(emisor.Rfc, emisor.Nombre, emisor.RegimenFiscal, emisor.LugarExpedicion);
    }

    private static ReceptorCfdi ReceptorDe(Comprobante c, string? numRegIdTrib = null) =>
        new(
            Rfc: c.ReceptorRfc,
            Nombre: c.ReceptorNombre,
            RegimenFiscal: c.ReceptorRegimenFiscal,
            CodigoPostal: c.ReceptorCodigoPostal,
            UsoCfdi: c.ReceptorUsoCfdi,
            Pais: c.ReceptorPais,
            EsGenerico: c.ReceptorEsGenerico,
            NumRegIdTrib: numRegIdTrib);

    private static List<RelacionCfdiEmision> RelacionesDe(Comprobante c) =>
        c.Relaciones
            .Select(r => new RelacionCfdiEmision(r.TipoRelacion, r.UuidRelacionado))
            .ToList();

    /// <summary>
    /// Impuestos de una línea de factura de venta: traslado IVA (002) con tasa
    /// a 6 decimales, o Exento cuando la línea es objeto de impuesto sin tasa;
    /// retenciones IVA (002) e ISR (001) si aplican.
    /// </summary>
    private static List<ImpuestoConceptoCfdi> ImpuestosDeLinea(FacturaVentaLinea l)
    {
        if (l.ObjetoImp != ObjetoImpGravado)
            return [];

        var baseGravable = l.Importe - l.Descuento;
        var impuestos = new List<ImpuestoConceptoCfdi>();

        if (l.TasaIvaTraslado is { } tasaIva)
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: baseGravable,
                Impuesto: "002",
                TipoFactor: "Tasa",
                TasaOCuota: Math.Round(tasaIva, 6),
                Importe: l.ImpuestoTrasladadoImporte,
                EsRetencion: false));
        }
        else
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: baseGravable,
                Impuesto: "002",
                TipoFactor: "Exento",
                TasaOCuota: 0m,
                Importe: 0m,
                EsRetencion: false));
        }

        if (l.TasaRetencionIva is decimal retIva && retIva > 0)
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: baseGravable,
                Impuesto: "002",
                TipoFactor: "Tasa",
                TasaOCuota: Math.Round(retIva, 6),
                Importe: Math.Round(baseGravable * retIva, 2, MidpointRounding.AwayFromZero),
                EsRetencion: true));
        }

        if (l.TasaRetencionIsr is decimal retIsr && retIsr > 0)
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: baseGravable,
                Impuesto: "001",
                TipoFactor: "Tasa",
                TasaOCuota: Math.Round(retIsr, 6),
                Importe: Math.Round(baseGravable * retIsr, 2, MidpointRounding.AwayFromZero),
                EsRetencion: true));
        }

        return impuestos;
    }

    /// <summary>
    /// Concepto único de comprobantes sin líneas propias (NC, anticipo, Carta
    /// Porte tipo I). La tasa de IVA se reconstruye desde los totales
    /// (subtotal/impuesto) porque el agregado no la persiste.
    /// </summary>
    private static ConceptoCfdi ConceptoUnico(
        string claveProdServ, string claveUnidad, string descripcion, decimal subtotal, decimal importeIva)
    {
        var impuestos = new List<ImpuestoConceptoCfdi>();
        if (importeIva > 0 && subtotal > 0)
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: subtotal,
                Impuesto: "002",
                TipoFactor: "Tasa",
                TasaOCuota: Math.Round(importeIva / subtotal, 6),
                Importe: importeIva,
                EsRetencion: false));
        }
        else
        {
            impuestos.Add(new ImpuestoConceptoCfdi(
                Base: subtotal,
                Impuesto: "002",
                TipoFactor: "Exento",
                TasaOCuota: 0m,
                Importe: 0m,
                EsRetencion: false));
        }

        return new ConceptoCfdi(
            ClaveProdServ: claveProdServ,
            ClaveUnidad: claveUnidad,
            Cantidad: 1m,
            Descripcion: descripcion,
            ValorUnitario: subtotal,
            Importe: subtotal,
            Descuento: 0m,
            ObjetoImp: ObjetoImpGravado,
            Impuestos: impuestos);
    }

    /// <summary>
    /// SKU estable y único por línea de exportación. El complemento CCE exige
    /// que cada <c>Mercancia.NoIdentificacion</c> coincida con el <c>ItemSku</c>
    /// del concepto correlacionado (contrato FiscalAPI); se deriva de la clave
    /// SAT + posición para que sea único dentro del comprobante y no un valor
    /// trivial ("1") que el PAC intente registrar como producto nuevo y
    /// colisione con "A record with the same unique values already exists"
    /// (P10-H2, sandbox FiscalAPI).
    /// </summary>
    private static string NoIdentificacionCce(FacturaVentaLinea l) =>
        $"{l.ClaveProdServSat}-{l.Posicion}";

    /// <summary>
    /// Construye el complemento CCE si la factura lo adjuntó (F7-PR1). Valida
    /// pre-vuelo el domicilio del receptor extranjero (Estado + CP, exigidos
    /// por el CCE 2.0) — falla ANTES de quemar el timbre (F12-PR3).
    /// <paramref name="lineasOrdenadas"/> son las líneas de la factura (mismas
    /// que los conceptos, ordenadas por posición) para correlacionar cada
    /// mercancía con su concepto por <c>NoIdentificacion</c>.
    /// </summary>
    private static ComplementoCceCfdi? ConstruirCce(
        FacturaVenta factura, List<FacturaVentaLinea> lineasOrdenadas)
    {
        if (factura.ComplementoCce is not { } cce)
            return null;

        if (string.IsNullOrWhiteSpace(cce.ReceptorDomicilioEstado)
            || string.IsNullOrWhiteSpace(cce.ReceptorDomicilioCodigoPostal))
        {
            throw new BusinessRuleException(
                "CCE_DOMICILIO_RECEPTOR_INCOMPLETO",
                "El Complemento de Comercio Exterior 2.0 exige el domicilio del receptor extranjero " +
                "(estado y código postal). Captura los datos faltantes y vuelve a emitir.");
        }

        return new ComplementoCceCfdi(
            TipoOperacion: cce.TipoOperacion,
            Incoterm: cce.Incoterm,
            TipoCambioUsd: cce.TcDof,
            ReceptorNumRegIdTrib: cce.ReceptorNumRegIdTrib,
            ReceptorPaisResidencia: cce.ReceptorPaisResidencia,
            ClaveDePedimento: cce.ClaveDePedimento,
            CertificadoOrigen: cce.CertificadoOrigen,
            Domicilio: new DomicilioCceCfdi(
                cce.ReceptorDomicilioCalle,
                cce.ReceptorDomicilioEstado!,
                cce.ReceptorPaisResidencia,
                cce.ReceptorDomicilioCodigoPostal!),
            Mercancias: cce.Lineas
                .Select((l, i) => new MercanciaCceCfdi(
                    // NoIdentificacion == ItemSku del concepto correlacionado por
                    // posición (contrato FiscalAPI para ligar la mercancía CCE a
                    // su concepto; P10-H2). Fallback a la posición 1-based si el
                    // número de líneas CCE no coincide con las de la factura.
                    i < lineasOrdenadas.Count
                        ? NoIdentificacionCce(lineasOrdenadas[i])
                        : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    l.FraccionArancelaria, l.UnidadAduana, l.CantidadAduana,
                    l.ValorUnitarioAduana, l.ValorDolares))
                .ToList());
    }
}
