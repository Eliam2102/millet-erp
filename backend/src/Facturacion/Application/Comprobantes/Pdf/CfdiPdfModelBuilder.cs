using Millet.Documentos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;

namespace Millet.Facturacion.Application.Comprobantes.Pdf;

/// <summary>
/// Arma el <see cref="CfdiPdfModel"/> con el formato de Millet (FMT-4, 86e3ukucq)
/// desde la entidad timbrada y los <see cref="DatosImpresionCfdi"/>. Función pura:
/// sin base de datos, para probar el mapeo campo por campo.
/// </summary>
public static class CfdiPdfModelBuilder
{
    private static readonly IReadOnlyDictionary<string, string> MetodosPago = new Dictionary<string, string>
    {
        ["PUE"] = "Pago en una sola exhibición",
        ["PPD"] = "Pago en parcialidades o diferido",
    };

    private static readonly IReadOnlyDictionary<string, string> TiposRelacion = new Dictionary<string, string>
    {
        ["01"] = "Nota de crédito de los documentos relacionados",
        ["02"] = "Nota de débito de los documentos relacionados",
        ["03"] = "Devolución de mercancía sobre facturas o traslados previos",
        ["04"] = "Sustitución de los CFDI previos",
        ["07"] = "CFDI por aplicación de anticipo",
    };

    public static CfdiPdfModel DesdeFactura(
        FacturaVenta f, DatosImpresionCfdi? datos, IReadOnlyDictionary<Guid, string>? codigosArticulo = null)
    {
        ArgumentNullException.ThrowIfNull(f);
        datos ??= DatosImpresionCfdi.Vacio;
        var lineas = f.Lineas.OrderBy(l => l.Posicion).ToList();

        var conceptos = lineas.Select(l => new ConceptoPdf(
            l.ProductoId is { } p && codigosArticulo?.TryGetValue(p, out var codigo) == true ? codigo : null,
            l.ClaveProdServSat, l.Descripcion,
            string.IsNullOrWhiteSpace(l.Referencia) ? null : $"ITEM.{l.Posicion} REF: {l.Referencia}",
            l.Cantidad, l.ClaveUnidadSat, l.ValorUnitario, l.Descuento, l.Importe)).ToList();

        var impuestos = new List<ImpuestoResumenPdf>();
        foreach (var g in lineas.Where(l => l.TasaIvaTraslado is not null).GroupBy(l => l.TasaIvaTraslado!.Value).OrderByDescending(g => g.Key))
            impuestos.Add(new("002 IVA", "Tasa", g.Key, g.Sum(l => l.Importe - l.Descuento), g.Sum(l => l.ImpuestoTrasladadoImporte), false));
        foreach (var g in lineas.Where(l => l.TasaRetencionIva is > 0).GroupBy(l => l.TasaRetencionIva!.Value))
            impuestos.Add(Retencion("002 IVA", g.Key, g.Sum(l => l.Importe - l.Descuento)));
        foreach (var g in lineas.Where(l => l.TasaRetencionIsr is > 0).GroupBy(l => l.TasaRetencionIsr!.Value))
            impuestos.Add(Retencion("001 ISR", g.Key, g.Sum(l => l.Importe - l.Descuento)));

        var fisicos = new TotalesFisicosPdf(
            lineas.Any(l => l.PesoKg is not null) ? lineas.Sum(l => l.PesoKg ?? 0) : null,
            lineas.Any(l => l.AreaPie2 is not null) ? lineas.Sum(l => l.AreaPie2 ?? 0) : null,
            lineas.Sum(l => l.Cantidad));

        return Armar(f, datos, TipoDocumentoPdf.Factura, "FACTURA", "INVOICE", "factura",
            conceptos, impuestos, fisicos, ComentariosFactura(f), f.OrdenCompraCliente, f.ReferenciaCliente,
            f.CondicionesPago, Relacionados(f)) with { ComercioExterior = ComercioExterior(f.ComplementoCce) };
    }

    public static CfdiPdfModel DesdeAnticipo(FacturaAnticipo a, DatosImpresionCfdi? datos)
    {
        ArgumentNullException.ThrowIfNull(a);
        return Armar(a, datos ?? DatosImpresionCfdi.Vacio, TipoDocumentoPdf.Anticipo,
            "FACTURA", "INVOICE", "factura-anticipo",
            [ConceptoUnico(a, a.ClaveProdServSat, a.Descripcion, a.ClaveUnidadSat)], [IvaUnico(a)], SinMedidas,
            comentarios: null, ordenCompra: null, referencia: null, condiciones: null, relaciones: Relacionados(a));
    }

    public static CfdiPdfModel DesdeNotaCredito(NotaCredito nc, DatosImpresionCfdi? datos)
    {
        ArgumentNullException.ThrowIfNull(nc);
        var motivo = nc.Motivo switch
        {
            MotivoNotaCredito.Amortizacion => "Aplicación de anticipo / Advance payment application",
            MotivoNotaCredito.Bonificacion => "Bonificación / Allowance",
            MotivoNotaCredito.Devolucion => "Devolución / Return",
            _ => nc.Motivo.ToString(),
        };
        return Armar(nc, datos ?? DatosImpresionCfdi.Vacio, TipoDocumentoPdf.NotaCredito,
            "NOTA DE CRÉDITO", "CREDIT MEMO", "nota-credito",
            [ConceptoUnico(nc, nc.ClaveProdServSat, nc.Descripcion, nc.ClaveUnidadSat)], [IvaUnico(nc)], MedidasEnCero,
            comentarios: $"Motivo / Reason: {motivo}", ordenCompra: null, referencia: null, condiciones: null,
            relaciones: Relacionados(nc));
    }

    /// <summary>
    /// Complemento de pago (CFDI tipo P, 86e3tfdx0) con el diseño de la factura de
    /// Millet: el concepto único «Pago» del SAT, los datos del pago y las facturas
    /// cubiertas. <paramref name="foliosFacturas"/> da la serie-folio de cada factura.
    /// </summary>
    public static CfdiPdfModel DesdeReciboPago(
        ReciboPago r, DatosImpresionCfdi? datos, IReadOnlyDictionary<Guid, string>? foliosFacturas = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        datos ??= DatosImpresionCfdi.Vacio;
        var facturas = r.FacturasPagadas.OrderBy(f => f.NumParcialidad).ThenBy(f => f.FacturaUuid).ToList();
        var primera = facturas.FirstOrDefault();
        var pago = new ComplementoPagoPdf(
            r.FechaPago,
            primera is null ? "" : ConDescripcion(primera.FormaPagoReal, datos.FormasPago),
            r.MonedaPago,
            facturas.Select(f => f.TcPago).FirstOrDefault(tc => tc is not null),
            r.ImporteTotalPago,
            Primero(facturas, f => f.ReferenciaPago),
            Primero(facturas, f => f.CuentaOrdenante),
            Primero(facturas, f => f.CuentaBeneficiaria),
            facturas.Select(f => new DocumentoPagadoPdf(
                f.FacturaUuid,
                foliosFacturas?.GetValueOrDefault(f.FacturaVentaId),
                f.MonedaFactura, f.NumParcialidad, f.SaldoAnterior, f.ImportePagado, f.SaldoInsoluto)).ToList());

        var modelo = Armar(r, datos, TipoDocumentoPdf.ComplementoPago, "COMPLEMENTO DE PAGO", "PAYMENT RECEIPT",
            "complemento-pago", [ConceptoPago], [], SinMedidas,
            comentarios: null, ordenCompra: null, referencia: null, condiciones: null, relaciones: Relacionados(r));
        return modelo with
        {
            TipoComprobante = "Pago",
            MontoEnLetra = MontoEnLetra(r.ImporteTotalPago, r.MonedaPago),
            ComplementoPago = pago,
        };
    }

    /// <summary>Concepto único que el SAT fija para el CFDI de pago (84111506 · Pago · ACT, importe 0).</summary>
    private static readonly ConceptoPdf ConceptoPago = new(null, "84111506", "Pago", null, 1, "ACT", 0, 0, 0);

    private static string? Primero(IEnumerable<ReciboPagoFactura> facturas, Func<ReciboPagoFactura, string?> campo) =>
        facturas.Select(campo).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static ComercioExteriorPdf? ComercioExterior(Domain.Cce.ComplementoCce? cce) =>
        cce is null
            ? null
            : new ComercioExteriorPdf(cce.TipoOperacion, cce.Incoterm, cce.ClaveDePedimento, cce.CertificadoOrigen,
                cce.TcDof, cce.Lineas.Sum(l => l.ValorDolares), cce.ReceptorNumRegIdTrib, cce.ReceptorPaisResidencia);

    private static CfdiPdfModel Armar(
        Comprobante c, DatosImpresionCfdi datos, TipoDocumentoPdf tipo, string tituloEs, string tituloEn, string prefijoArchivo,
        IReadOnlyList<ConceptoPdf> conceptos, IReadOnlyList<ImpuestoResumenPdf> impuestos, TotalesFisicosPdf fisicos,
        string? comentarios, string? ordenCompra, string? referencia, string? condiciones,
        IReadOnlyList<CfdiRelacionadoPdf> relaciones)
    {
        var (serie, folio) = SepararFolio(c.Folio);
        return new CfdiPdfModel(
            tipo, tituloEs, tituloEn, serie, folio,
            c.FechaTimbrado ?? c.CreatedAt,
            c.Tipo == TipoComprobante.Egreso ? "Egreso" : "Ingreso",
            new EmisorPdf(c.NombreEmisor, datos.NombreComercial, c.RfcEmisor,
                ConDescripcion(c.RegimenFiscalEmisor, datos.Regimenes), datos.DomicilioLineas, datos.Logo),
            new ReceptorPdf(c.ReceptorNombre, c.ReceptorRfc, c.ReceptorCodigoPostal,
                ConDescripcion(c.ReceptorRegimenFiscal, datos.Regimenes), c.ReceptorPais, condiciones),
            new CfdiCabeceraPdf(c.LugarExpedicion, c.Moneda, c.TipoCambio, ordenCompra, referencia,
                c.NoCertificadoEmisor, c.NoCertificadoSat, c.FechaTimbrado, c.Uuid),
            conceptos, impuestos, fisicos, comentarios,
            new TotalesPdf(c.Moneda, c.Subtotal, c.Descuento, c.ImpuestosTrasladados, c.Retenciones, c.Total),
            MontoEnLetra(c.Total, c.Moneda),
            new PagoPdf(ConDescripcion(c.ReceptorUsoCfdi, datos.UsosCfdi),
                ConDescripcion(c.MetodoPago, MetodosPago), ConDescripcion(c.FormaPago, datos.FormasPago)),
            relaciones,
            Timbre(c),
            tipo == TipoDocumentoPdf.Factura ? $"{prefijoArchivo}-{c.Folio}-bilingue.pdf" : $"{prefijoArchivo}-{c.Folio}.pdf");
    }

    private static List<CfdiRelacionadoPdf> Relacionados(Comprobante c) =>
        c.Relaciones.Select(r => new CfdiRelacionadoPdf(r.UuidRelacionado, ConDescripcion(r.TipoRelacion, TiposRelacion))).ToList();

    /// <summary>Concepto único sin peso ni área: el original FANT deja Kg y Pie² vacíos; el NCRED los imprime en 0.00.</summary>
    private static readonly TotalesFisicosPdf SinMedidas = new(null, null, 1);
    private static readonly TotalesFisicosPdf MedidasEnCero = new(0, 0, 1);

    private static ConceptoPdf ConceptoUnico(Comprobante c, string claveProdServ, string descripcion, string claveUnidad) =>
        new(null, claveProdServ, descripcion, null, 1, claveUnidad, c.Subtotal, c.Descuento, c.Subtotal);

    private static ImpuestoResumenPdf IvaUnico(Comprobante c)
    {
        var baseIva = c.Subtotal - c.Descuento;
        var tasa = baseIva > 0 ? Math.Round(c.ImpuestosTrasladados / baseIva, 2, MidpointRounding.AwayFromZero) : 0;
        return new("002 IVA", "Tasa", tasa, baseIva, c.ImpuestosTrasladados, false);
    }

    private static ImpuestoResumenPdf Retencion(string nombre, decimal tasa, decimal baseImpuesto) =>
        new(nombre, "Tasa", tasa, baseImpuesto, Math.Round(baseImpuesto * tasa, 2, MidpointRounding.AwayFromZero), true);

    private static string? ComentariosFactura(FacturaVenta f)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Proforma)) partes.Add($"PROFORMA: {f.Proforma}");
        if (f.DatosEmbarqueCe is { } ce)
        {
            partes.Add($"EXPEDIENTE: {ce.FolioExpediente} · INCOTERM: {ce.Incoterm}");
            partes.Add($"PEDIMENTO: {ce.NumeroPedimento} ({ce.ClavePedimento}) {ce.FechaPedimento:dd/MM/yyyy}" +
                (ce.Aduana is null ? "" : $" · ADUANA {ce.Aduana} {ce.AduanaNombre}".TrimEnd()));
            foreach (var cont in ce.Contenedores)
                partes.Add($"CONTENEDOR: {cont.Numero} · BL: {cont.BlGuia} · EMBARQUE: {cont.FechaEmbarqueReal:dd/MM/yyyy}");
        }
        if (!string.IsNullOrWhiteSpace(f.Observaciones)) partes.Add(f.Observaciones.Trim());
        return partes.Count == 0 ? null : string.Join('\n', partes);
    }

    private static MontoEnLetraPdf MontoEnLetra(decimal total, string moneda)
    {
        try
        {
            return new(ImporteConLetra.Espanol(total, moneda), ImporteConLetra.Ingles(total, moneda));
        }
        catch (ArgumentException)
        {
            // El kit solo escribe MXN y USD; otra moneda imprime solo el importe en número.
            return new("", "");
        }
    }

    private static TimbrePdf? Timbre(Comprobante c)
    {
        if (c.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(c.Uuid)
            || string.IsNullOrWhiteSpace(c.SelloCfdi) || string.IsNullOrWhiteSpace(c.SelloSat))
            return null;
        var cadena = c.CadenaOriginalTimbre;
        if (string.IsNullOrWhiteSpace(cadena) && c.FechaTimbrado is { } fecha && !string.IsNullOrWhiteSpace(c.RfcProveedorCertificacion))
            cadena = $"||1.1|{c.Uuid}|{fecha:yyyy-MM-ddTHH:mm:ss}|{c.RfcProveedorCertificacion}|{c.SelloCfdi}|{c.NoCertificadoSat}||";
        // El QR usa los últimos 8 caracteres del sello; un sello más corto (stub) imprime los sellos sin QR.
        var urlQr = c.SelloCfdi.Length >= 8 && c.Total >= 0
            ? CfdiQr.Url(c.Uuid, c.RfcEmisor, c.ReceptorRfc, c.Total, c.SelloCfdi)
            : null;
        return new TimbrePdf(c.SelloCfdi, c.SelloSat, cadena ?? "", urlQr);
    }

    internal static (string Serie, string Folio) SepararFolio(string folio)
    {
        var i = folio.LastIndexOf('-');
        return i <= 0 ? ("", folio) : (folio[..i], folio[(i + 1)..]);
    }

    private static string ConDescripcion(string clave, IReadOnlyDictionary<string, string> catalogo) =>
        catalogo.TryGetValue(clave, out var descripcion) && !string.IsNullOrWhiteSpace(descripcion)
            ? $"{clave} - {descripcion}"
            : clave;
}
