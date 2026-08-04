using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using SdkModels = Fiscalapi.Models;
// Fiscalapi.Models.ForeignTrade.Mercancia y Fiscalapi.Models.BillOfLading.Mercancia
// colisionan — siempre vía alias de namespace.
using SdkBol = Fiscalapi.Models.BillOfLading;
using SdkFt = Fiscalapi.Models.ForeignTrade;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Adapter REAL de <see cref="ICfdiTimbradoPort"/> sobre el SDK NuGet oficial
/// de FiscalAPI (F12-PR2; desde F12-PR3 es el ÚNICO registro del puerto — los
/// stubs de Facturación se eliminaron). Mapea el contrato PAC-neutral
/// <see cref="CfdiEmision"/> al modelo <c>Invoice</c> del SDK
/// (<c>POST /api/v4/invoices</c> — FiscalAPI construye, sella y timbra en
/// una operación, D11). Complementos soportados vía <c>Invoice.Complement</c>:
/// Pago 2.0 (<c>Complement.Payment</c>), CCE 2.0 y Carta Porte 3.1 (F12-PR3).
///
/// <para>Semántica de resultados (diseño F12, resiliencia G):</para>
/// <list type="bullet">
///   <item><b>Timbrado</b> — el envelope regresó <c>succeeded</c> con UUID.</item>
///   <item><b>Fallido</b> — el PAC/SAT rechazó (envelope no-succeeded, p.ej.
///   CFDI40xxx). Corregible: el comprobante vuelve a Borrador.</item>
///   <item><b>EnProceso</b> — resultado AMBIGUO: excepción de red/timeout
///   después de enviar, o succeeded sin UUID. NO se reintenta automático
///   (podría duplicar el timbre); lo resuelve el <c>TimbradoPendienteWorker</c>
///   + runbook.</item>
/// </list>
/// </summary>
public sealed partial class FiscalApiTimbradoAdapter : ICfdiTimbradoPort
{
    private readonly IFiscalApiSdkClientFactory _factory;
    private readonly IConfiguracionPacResolver _configResolver;
    private readonly ILogger<FiscalApiTimbradoAdapter> _logger;

    public FiscalApiTimbradoAdapter(
        IFiscalApiSdkClientFactory factory,
        IConfiguracionPacResolver configResolver,
        ILogger<FiscalApiTimbradoAdapter> logger)
    {
        _factory = factory;
        _configResolver = configResolver;
        _logger = logger;
    }

    public async Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(emision.EmpresaId, cancellationToken);
        var config = await _configResolver.ResolverAsync(
            emision.EmpresaId, ProveedorPac.FiscalApi, cancellationToken);

        // Fail-fast ANTES de llamar al PAC: la emisión por valores exige el
        // CSD del emisor en cada request (Issuer.TaxCredentials) — sin él,
        // FiscalAPI rechaza siempre. Rechazo limpio (no ambiguo): corregible
        // capturando el CSD en la configuración del PAC y reintentando.
        if (config?.Csd is null)
        {
            _logger.LogWarning(
                "[FiscalApiTimbradoAdapter] Timbrado {Serie}-{Folio} (empresa {EmpresaId}) sin CSD configurado — rechazado pre-vuelo.",
                emision.Serie, emision.Folio, emision.EmpresaId);
            return Fallido(
                "CSD_NO_CONFIGURADO",
                "La configuración del PAC no tiene el CSD del emisor (.cer + .key + password). " +
                "Captúralo en Integraciones Fiscal → Configuración del PAC y reintenta el timbrado.");
        }

        emision = SustituirIdentidadesSandbox(emision, config);
        var invoice = MapInvoice(emision);
        invoice.Issuer.TaxCredentials =
        [
            new SdkModels.TaxCredential
            {
                Base64File = config.Csd.CertificadoBase64,
                FileType = SdkModels.FileType.CertificateCsd,
                Password = config.Csd.Password,
            },
            new SdkModels.TaxCredential
            {
                Base64File = config.Csd.LlavePrivadaBase64,
                FileType = SdkModels.FileType.PrivateKeyCsd,
                Password = config.Csd.Password,
            },
        ];

        Fiscalapi.Common.ApiResponse<SdkModels.Invoice> resp;
        try
        {
            resp = await sdk.Invoices.CreateAsync(invoice);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ambiguo: la solicitud pudo haber llegado. NUNCA reintentar aquí.
            _logger.LogError(ex,
                "[FiscalApiTimbradoAdapter] Excepción al timbrar {Serie}-{Folio} (empresa {EmpresaId}) — resultado ambiguo → EnProceso.",
                emision.Serie, emision.Folio, emision.EmpresaId);
            return new TimbradoResultado(
                TimbradoEstado.EnProceso, null, null, null, null, null, null, null,
                ErrorCodigo: "PAC_SIN_RESPUESTA", ErrorMensaje: ex.Message);
        }

        if (!resp.Succeeded)
        {
            var codigo = ExtraerCodigoSat(resp.Message) ?? resp.HttpStatusCode.ToString();
            var mensaje = string.IsNullOrWhiteSpace(resp.Details)
                ? resp.Message
                : $"{resp.Message} — {resp.Details}";
            _logger.LogWarning(
                "[FiscalApiTimbradoAdapter] Timbre rechazado {Serie}-{Folio}: {Codigo} {Mensaje}",
                emision.Serie, emision.Folio, codigo, mensaje);
            return Fallido(codigo, mensaje);
        }

        var timbre = resp.Data?.Responses?.FirstOrDefault();
        if (timbre is null || string.IsNullOrWhiteSpace(timbre.InvoiceUuid))
        {
            // Succeeded sin timbre en la respuesta: ambiguo (no re-emitir).
            _logger.LogError(
                "[FiscalApiTimbradoAdapter] CreateAsync succeeded SIN UUID para {Serie}-{Folio} (invoiceId={InvoiceId}) → EnProceso.",
                emision.Serie, emision.Folio, resp.Data?.Id);
            return new TimbradoResultado(
                TimbradoEstado.EnProceso, null, null, null, null, null, null, null,
                ErrorCodigo: "PAC_RESPUESTA_INCOMPLETA",
                ErrorMensaje: "El PAC aceptó la solicitud pero no devolvió el timbre.");
        }

        var xml = await ResolverXmlTimbradoAsync(sdk, resp.Data!.Id, timbre.InvoiceBase64);

        return new TimbradoResultado(
            Estado: TimbradoEstado.Timbrado,
            Uuid: timbre.InvoiceUuid,
            SelloCfdi: timbre.InvoiceBase64Sello,
            SelloSat: timbre.SatBase64Sello,
            NoCertificadoSat: timbre.SatCertificateNumber,
            FechaTimbrado: new DateTimeOffset(timbre.InvoiceSignatureDate, TimeSpan.Zero),
            RfcProveedorCertificacion: null, // no viene en el envelope; el XML lo trae (TFD)
            XmlTimbrado: xml,
            ErrorCodigo: null,
            ErrorMensaje: null,
            // Folio que FiscalAPI asignó al CFDI (consecutivo por RFC emisor;
            // el atributo Folio del XML). El folio interno de Millet no viaja.
            FolioPac: resp.Data.Number);
    }

    public async Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(solicitud.EmpresaId, cancellationToken);

        // El CFDI se timbró con el emisor sandbox (si está configurado); la
        // cancelación debe viajar con ese mismo RFC o el SAT no lo encuentra.
        var config = await _configResolver.ResolverAsync(
            solicitud.EmpresaId, ProveedorPac.FiscalApi, cancellationToken);
        if (config?.EmisorSandbox is { } emisorSbx)
        {
            _logger.LogWarning(
                "[SANDBOX] Cancelación de {Uuid} con RFC emisor de prueba {RfcSandbox} (real: {RfcReal}).",
                solicitud.Uuid, emisorSbx.Rfc, solicitud.RfcEmisor);
            solicitud = solicitud with { RfcEmisor = emisorSbx.Rfc };
        }

        Fiscalapi.Common.ApiResponse<SdkModels.CancelInvoiceResponse> resp;
        try
        {
            resp = await sdk.Invoices.CancelAsync(new SdkModels.CancelInvoiceRequest
            {
                InvoiceUuid = solicitud.Uuid,
                Rfc = solicitud.RfcEmisor,
                CancellationReasonCode = solicitud.MotivoSat,
                ReplacementUuid = solicitud.UuidSustituto,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[FiscalApiTimbradoAdapter] Excepción al cancelar {Uuid}.", solicitud.Uuid);
            return new CancelacionResultado(CancelacionEstado.Error, null, ex.Message);
        }

        if (!resp.Succeeded)
            return new CancelacionResultado(
                CancelacionEstado.Error, null,
                string.IsNullOrWhiteSpace(resp.Details) ? resp.Message : $"{resp.Message} — {resp.Details}");

        // El SAT resuelve la cancelación de forma asíncrona salvo los casos
        // "sin aceptación" (código 202 = ya cancelado). Cualquier otro estado
        // queda EnProceso y el CancelacionSatPollerWorker lo confirma
        // consultando el estatus del CFDI.
        var estatus = resp.Data?.InvoiceUuids is { } uuids
            && uuids.TryGetValue(solicitud.Uuid, out var v) ? v : null;
        var aceptadaDeInmediato = estatus is not null
            && (estatus.Contains("202", StringComparison.Ordinal)
                || estatus.Contains("Cancelado", StringComparison.OrdinalIgnoreCase));

        return new CancelacionResultado(
            aceptadaDeInmediato ? CancelacionEstado.Aceptada : CancelacionEstado.EnProceso,
            estatus,
            null);
    }

    public async Task<EstatusCfdiResultado> ConsultarEstatusAsync(
        EstatusCfdiSolicitud solicitud, CancellationToken cancellationToken)
    {
        var sdk = await _factory.GetClientAsync(solicitud.EmpresaId, cancellationToken);

        // La consulta compara contra lo TIMBRADO: si el CFDI salió con
        // identidades sandbox, los RFC de la consulta deben ser los mismos.
        var config = await _configResolver.ResolverAsync(
            solicitud.EmpresaId, ProveedorPac.FiscalApi, cancellationToken);
        var rfcEmisor = config?.EmisorSandbox?.Rfc ?? solicitud.RfcEmisor;
        var rfcReceptor = config?.ReceptorSandbox is { } receptorSbx && !EsRfcGenerico(solicitud.RfcReceptor)
            ? receptorSbx.Rfc
            : solicitud.RfcReceptor;

        var resp = await sdk.Invoices.GetStatusAsync(new SdkModels.InvoiceStatusRequest
        {
            InvoiceUuid = solicitud.Uuid,
            IssuerTin = rfcEmisor,
            RecipientTin = rfcReceptor,
            InvoiceTotal = solicitud.Total,
            Last8DigitsIssuerSignature = solicitud.Sello8,
        });

        if (!resp.Succeeded)
            throw new InvalidOperationException(
                $"FiscalAPI ConsultarEstatus falló para {solicitud.Uuid}: {resp.Message} {resp.Details}".Trim());

        var data = resp.Data!;
        return new EstatusCfdiResultado(
            EsVigente: string.Equals(data.Status, "Vigente", StringComparison.OrdinalIgnoreCase),
            EstatusSat: data.Status ?? "Desconocido",
            EsCancelable: data.CancelableStatus is null
                ? null
                : !data.CancelableStatus.Contains("No cancelable", StringComparison.OrdinalIgnoreCase),
            EstadoCancelacion: data.CancellationStatus);
    }

    // ───────────────────────── Mapeo CfdiEmision → Invoice ─────────────────────────

    internal static SdkModels.Invoice MapInvoice(CfdiEmision e)
    {
        var esConImporte = e.Tipo is TipoCfdi.Ingreso or TipoCfdi.Egreso;

        return new SdkModels.Invoice
        {
            VersionCode = "4.0",
            Series = e.Serie,
            // Number (Folio del CFDI) DEBE ir null: FiscalAPI lo calcula
            // internamente (consecutivo por RFC emisor) y rechaza folios del
            // cliente. El folio interno de Millet (e.Folio) queda como
            // control en el comprobante; el del PAC regresa en la respuesta
            // y se persiste como FolioPac.
            Number = null,
            Date = e.FechaLocal.DateTime, // ya convertida a America/Mexico_City por el builder
            TypeCode = e.Tipo switch
            {
                TipoCfdi.Ingreso => "I",
                TipoCfdi.Egreso => "E",
                TipoCfdi.Pago => "P",
                _ => "T",
            },
            ExpeditionZipCode = e.Emisor.LugarExpedicion,
            ExportCode = e.Exportacion,
            CurrencyCode = e.Moneda,
            ExchangeRate = e.TipoCambio ?? 1m,
            // Tipo P/T: forma y método de pago no aplican (regla SAT).
            PaymentFormCode = esConImporte ? e.FormaPago : null,
            PaymentMethodCode = esConImporte ? e.MetodoPago : null,
            Issuer = new SdkModels.InvoiceIssuer
            {
                Tin = e.Emisor.Rfc,
                LegalName = e.Emisor.Nombre,
                TaxRegimeCode = e.Emisor.RegimenFiscal,
            },
            Recipient = MapRecipient(e.Receptor),
            // Tipo P (Pago 2.0): sin conceptos. El complemento de pago viaja en
            // Complement.Payment (ver MapComplement) y FiscalAPI genera el
            // concepto fijo del pago (84111506/ACT) internamente; mandar Items
            // en tipo P — como intentó #662 — deja el pago sin su mapper y el
            // PAC responde 500 "No mapper found for invoice request" (P9-H2,
            // incidente 2026-07-17). El resto de tipos sí llevan sus conceptos
            // (emisión por valores con TaxCredentials).
            Items = e.Tipo == TipoCfdi.Pago ? null : e.Conceptos.Select(MapItem).ToList(),
            RelatedInvoices = e.Relaciones.Count == 0
                ? null
                : e.Relaciones
                    .Select(r => new SdkModels.RelatedInvoice { RelationshipTypeCode = r.TipoRelacion, Uuid = r.Uuid })
                    .ToList(),
            // El complemento Pago 2.0 viaja en Complement.Payment (singular) —
            // ver MapComplement. Invoice.Payments (plural) es del lado respuesta
            // y el endpoint /api/v4/invoices NO lo lee al construir el CFDI.
            Complement = MapComplement(e),
        };
    }

    /// <summary>
    /// Nodo <c>Complement</c> del Invoice: complemento Pago 2.0 (tipo P), CCE 2.0
    /// y Carta Porte 3.1. El endpoint unificado <c>/api/v4/invoices</c> detecta
    /// el complemento de pago por <c>Complement.Payment</c> (singular); ponerlo
    /// en <c>Invoice.Payments</c> hace que el PAC responda 500 "No mapper found
    /// for invoice request" (P9-H2). Si un CFDI trajera varios complementos, se
    /// componen en el mismo nodo.
    /// </summary>
    private static SdkModels.Complement? MapComplement(CfdiEmision e)
    {
        if (e.ComplementoPago is null && e.ComplementoCce is null && e.ComplementoCartaPorte is null)
            return null;

        return new SdkModels.Complement
        {
            Payment = e.ComplementoPago is null ? null : MapPayment(e.ComplementoPago),
            ComercioExterior = e.ComplementoCce is null ? null : MapComercioExterior(e.ComplementoCce),
            CartaPorte = e.ComplementoCartaPorte is null ? null : MapCartaPorte(e.ComplementoCartaPorte),
        };
    }

    /// <summary>CCE 2.0 → <c>ForeignTrade.ComercioExterior</c>. <c>MotivoTrasladoId</c> no se setea (solo CFDI de traslado).</summary>
    private static SdkFt.ComercioExterior MapComercioExterior(ComplementoCceCfdi c) => new()
    {
        ClaveDePedimentoId = c.ClaveDePedimento,
        CertificadoOrigen = c.CertificadoOrigen ? 1 : 0,
        IncotermId = c.Incoterm,
        TipoCambioUSD = c.TipoCambioUsd,
        Receptor = new SdkFt.Receptor
        {
            NumRegIdTrib = c.ReceptorNumRegIdTrib,
            Domicilio = c.Domicilio is null ? null : new SdkFt.ReceptorDomicilio
            {
                Calle = c.Domicilio.Calle,
                Estado = c.Domicilio.Estado,
                PaisId = c.Domicilio.Pais,
                CodigoPostal = c.Domicilio.CodigoPostal,
            },
        },
        Mercancias = c.Mercancias.Select(m => new SdkFt.Mercancia
        {
            NoIdentificacion = m.NoIdentificacion,
            FraccionArancelariaId = m.FraccionArancelaria,
            CantidadAduana = m.CantidadAduana,
            UnidadAduanaId = m.UnidadAduana,
            ValorUnitarioAduana = m.ValorUnitarioAduana,
            // ValorDolares a EXACTAMENTE 2 decimales (P10-H4): FiscalAPI calcula
            // ComercioExterior:TotalUSD = Σ ValorDolares y propaga la escala
            // decimal al XML (mismo gotcha que TasaCatalogoSat/CFDI40179). Con
            // escala 0 ("5000") el TotalUSD queda sin 2 decimales y el SAT
            // rechaza con CCE122. Sumar 0.00m fuerza escala 2 sin alterar el valor.
            ValorDolares = Importe2Decimales(m.ValorDolares),
        }).ToList(),
    };

    /// <summary>
    /// Fuerza un importe a EXACTAMENTE 2 decimales de escala. <c>Math.Round</c>
    /// no basta (un <c>decimal</c> conserva su escala original y el serializador
    /// JSON la respeta); sumar <c>0.00m</c> toma la escala mayor de los operandos
    /// (2) sin cambiar el valor. Mismo patrón que <see cref="TasaCatalogoSat"/>.
    /// </summary>
    internal static decimal Importe2Decimales(decimal importe) =>
        Math.Round(importe, 2, MidpointRounding.AwayFromZero) + 0.00m;

    /// <summary>
    /// Carta Porte 3.1 → <c>BillOfLading.CartaPorte</c>. Solo autotransporte
    /// federal nacional (TranspInternac = No); ubicaciones con formato SAT
    /// <c>ORxxxxxx</c>/<c>DExxxxxx</c> y fechas en hora local fiscal (el
    /// builder ya convirtió; el SDK serializa con <c>SatDateFormat</c>).
    /// </summary>
    private static SdkBol.CartaPorte MapCartaPorte(ComplementoCartaPorteCfdi c) => new()
    {
        TranspInternacId = "No",
        TotalDistRec = c.DistanciaKm,
        Ubicaciones =
        [
            new SdkBol.Ubicacion
            {
                TipoUbicacion = "Origen",
                IDUbicacion = "OR000001",
                RFCRemitenteDestinatario = c.RfcRemitente,
                NombreRemitenteDestinatario = c.NombreRemitente,
                FechaHoraSalidaLlegada = c.FechaSalida.DateTime,
                Domicilio = MapDomicilio(c.Origen),
            },
            new SdkBol.Ubicacion
            {
                TipoUbicacion = "Destino",
                IDUbicacion = "DE000001",
                RFCRemitenteDestinatario = c.RfcDestinatario,
                NombreRemitenteDestinatario = c.NombreDestinatario,
                FechaHoraSalidaLlegada = c.FechaLlegadaEstimada.DateTime,
                DistanciaRecorrida = c.DistanciaKm,
                Domicilio = MapDomicilio(c.Destino),
            },
        ],
        Mercancias = c.Mercancias.Select(m => new SdkBol.Mercancia
        {
            BienesTranspId = m.BienesTransp,
            Descripcion = m.Descripcion,
            Cantidad = m.Cantidad,
            ClaveUnidadId = m.ClaveUnidad,
            PesoEnKg = m.PesoEnKg,
            // MaterialPeligroso es CONDICIONAL: solo se registra cuando el bien
            // es peligroso. Para claves catalogadas como no-peligrosas (columna
            // "Material peligroso" = "0" en c_ClaveProdServCP), enviar "No"
            // dispara CP107/CP155; hay que OMITIR el atributo (null → el SDK no
            // lo serializa). P10-H5.
            MaterialPeligrosoId = m.MaterialPeligroso ? "Sí" : null,
        }).ToList(),
        PesoNetoTotal = c.Mercancias.Sum(m => m.PesoEnKg),
        UnidadPesoId = "KGM",
        Autotransporte = new SdkBol.Autotransporte
        {
            PermSCTId = c.Vehiculo.TipoPermisoSct,
            NumPermisoSCT = c.Vehiculo.NumPermisoSct,
            ConfigVehicularId = c.Vehiculo.ConfigVehicular,
            // Obligatorio SAT (toneladas). CfdiEmisionBuilder ya validó > 0
            // (CARTA_PORTE_DATOS_SAT_INCOMPLETOS) antes de quemar folio.
            PesoBrutoVehicular = c.Vehiculo.PesoBrutoVehicular ?? 0m,
            PlacaVM = c.Vehiculo.Placa,
            AnioModeloVM = c.Vehiculo.AnioModelo,
            AseguraRespCivil = c.Vehiculo.Aseguradora,
            PolizaRespCivil = c.Vehiculo.PolizaSeguro,
        },
        TiposFigura =
        [
            new SdkBol.TipoFigura
            {
                TipoFiguraId = "01", // 01 = Operador (c_FiguraTransporte)
                RFCFigura = c.Operador.Rfc,
                NumLicencia = c.Operador.NumLicencia,
                NombreFigura = c.Operador.Nombre,
            },
        ],
    };

    private static SdkBol.UbicacionDomicilio MapDomicilio(UbicacionCartaPorteCfdi u) => new()
    {
        EstadoId = u.Estado,
        PaisId = u.Pais,
        CodigoPostalId = u.CodigoPostal,
    };

    private static SdkModels.InvoiceRecipient MapRecipient(ReceptorCfdi r)
    {
        var esExtranjero = !string.Equals(r.Pais, "MEX", StringComparison.OrdinalIgnoreCase);
        return new SdkModels.InvoiceRecipient
        {
            Tin = r.Rfc,
            LegalName = r.Nombre,
            ZipCode = r.CodigoPostal,
            TaxRegimeCode = r.RegimenFiscal,
            CfdiUseCode = r.UsoCfdi,
            // ResidenciaFiscal/NumRegIdTrib solo aplican a receptores extranjeros.
            CountryId = esExtranjero ? r.Pais : null,
            ForeignTin = esExtranjero ? r.NumRegIdTrib : null,
        };
    }

    private static SdkModels.InvoiceItem MapItem(ConceptoCfdi c) => new()
    {
        ItemCode = c.ClaveProdServ,
        // FiscalAPI exige itemSku no-vacío (NoIdentificacion del CFDI);
        // cuando el agregado no trae SKU propio, la ClaveProdServ es el
        // identificador estable disponible.
        ItemSku = string.IsNullOrWhiteSpace(c.NoIdentificacion) ? c.ClaveProdServ : c.NoIdentificacion,
        UnitOfMeasurementCode = c.ClaveUnidad,
        Quantity = c.Cantidad,
        Description = c.Descripcion,
        UnitPrice = c.ValorUnitario,
        Discount = c.Descuento,
        TaxObjectCode = c.ObjetoImp,
        CustomsInfo = c.Pedimento is null
            ? null
            : [new SdkModels.CustomsInfo { CustomsNumber = c.Pedimento.Numero }],
        ItemTaxes = c.Impuestos.Count == 0
            ? null
            : c.Impuestos.Select(i => new SdkModels.InvoiceItemTax
            {
                TaxCode = i.Impuesto,
                TaxTypeCode = i.TipoFactor,
                TaxRate = TasaCatalogoSat(i.TasaOCuota),
                // c_TipoImpuesto del modelo FiscalAPI: T = traslado, R = retención.
                TaxFlagCode = i.EsRetencion ? "R" : "T",
            }).ToList(),
    };

    /// <summary>
    /// Re-escala la tasa a EXACTAMENTE 6 decimales. El catálogo
    /// <c>c_TasaOCuota</c> del SAT exige la forma textual completa
    /// ("0.160000") y rechaza "0.16" con CFDI40179 (incidente 2026-07-12).
    /// <c>Math.Round(t, 6)</c> NO basta: redondear no agrega ceros — un
    /// <c>decimal</c> conserva su escala original (0.16m es escala 2) y el
    /// serializador JSON respeta esa escala en el payload al PAC. Sumar
    /// 0.000000m fuerza escala 6 (la suma decimal toma la escala mayor de
    /// los operandos) sin alterar el valor.
    /// </summary>
    internal static decimal TasaCatalogoSat(decimal tasa) =>
        Math.Round(tasa, 6) + 0.000000m;

    private static SdkModels.InvoicePayment MapPayment(ComplementoPagoCfdi p) => new()
    {
        PaymentDate = p.FechaPago.DateTime,
        PaymentFormCode = p.FormaPago,
        CurrencyCode = p.Moneda,
        ExchangeRate = p.TipoCambio ?? 1m,
        Amount = p.Monto,
        OperationNumber = p.ReferenciaPago,
        SourceBankAccount = p.CuentaOrdenante,
        TargetBankAccount = p.CuentaBeneficiaria,
        PaidInvoices = p.Documentos.Select(d => new SdkModels.PaidInvoice
        {
            Uuid = d.FacturaUuid,
            Series = d.Serie,
            Number = d.Folio,
            CurrencyCode = d.Moneda,
            PartialityNumber = d.NumParcialidad,
            PreviousBalance = d.SaldoAnterior,
            PaymentAmount = d.ImportePagado,
            RemainingBalance = d.SaldoInsoluto,
            // ObjetoImpDR de la factura pagada. "02" (sí objeto) exige el
            // desglose ImpuestosDR (base/tasa) prorrateado al pago; sin él
            // FiscalAPI rechaza. Base y tasa vienen ya calculados por Facturación.
            TaxObjectCode = d.ObjetoImpDR,
            Equivalence = d.Equivalencia,
            Subtotal = d.Subtotal,
            PaidInvoiceTaxes = d.Impuestos is null or { Count: 0 }
                ? null
                : d.Impuestos.Select(i => new SdkModels.PaidInvoiceTax
                {
                    Base = i.BaseDR,
                    TaxCode = i.Impuesto,
                    TaxTypeCode = i.TipoFactor,
                    TaxRate = TasaCatalogoSat(i.TasaOCuota),
                    // c_TipoImpuesto: T = traslado, R = retención.
                    TaxFlagCode = i.EsRetencion ? "R" : "T",
                }).ToList(),
        }).ToList(),
    };

    // ───────────────────────── Identidades sandbox ─────────────────────────

    /// <summary>
    /// Sustituye emisor/receptor por las identidades de prueba de la
    /// <c>ConfiguracionPac</c> cuando existen (solo posibles con BaseUrl de
    /// sandbox — invariante del agregado). Los datos reales de la empresa y
    /// del cliente NO se tocan: el snapshot del comprobante en el ERP
    /// conserva los valores reales; solo el payload al PAC viaja sustituido.
    /// </summary>
    private CfdiEmision SustituirIdentidadesSandbox(
        CfdiEmision emision, ConfiguracionPacResuelta config)
    {
        if (config.EmisorSandbox is null && config.ReceptorSandbox is null)
            return emision;

        var sustituida = AplicarIdentidadesSandbox(emision, config.EmisorSandbox, config.ReceptorSandbox);

        _logger.LogWarning(
            "[SANDBOX] Timbrado {Serie}-{Folio} con identidades de prueba — emisor {EmisorReal}→{EmisorSandbox}, receptor {ReceptorReal}→{ReceptorSandbox}.",
            emision.Serie, emision.Folio,
            emision.Emisor.Rfc, sustituida.Emisor.Rfc,
            emision.Receptor.Rfc, sustituida.Receptor.Rfc);

        return sustituida;
    }

    /// <summary>
    /// Aplica la sustitución en el contrato PAC-neutral. El receptor solo se
    /// sustituye cuando es nacional y no genérico: los RFC genéricos
    /// (XAXX/XEXX) son válidos también en la LCO sintética, y sustituir al
    /// receptor extranjero de un CFDI de exportación rompería el CCE.
    ///
    /// <para>
    /// <b>Excepción Traslado (tipo T):</b> un CFDI de traslado de mercancía
    /// propia exige <c>Receptor.Rfc == Emisor.Rfc</c> (CP107). Al sustituir el
    /// emisor por la identidad sandbox, el receptor DEBE seguirlo (si se
    /// sustituyera con el receptor de prueba quedaría distinto y el PAC rechaza
    /// con CP107 — P10-H1). Es la excepción análoga a la del receptor
    /// extranjero de CCE, pero en sentido inverso: aquí sí forzamos la
    /// sustitución del receptor, igualándolo al emisor. Además se propaga a las
    /// ubicaciones del complemento Carta Porte, cuyo remitente/destinatario son
    /// el emisor/receptor del traslado.
    /// </para>
    /// </summary>
    internal static CfdiEmision AplicarIdentidadesSandbox(
        CfdiEmision e, IdentidadSandboxResuelta? emisor, IdentidadSandboxResuelta? receptor)
    {
        if (emisor is not null)
        {
            e = e with
            {
                Emisor = new EmisorCfdi(
                    Rfc: emisor.Rfc,
                    Nombre: emisor.RazonSocial,
                    RegimenFiscal: emisor.RegimenFiscal,
                    LugarExpedicion: emisor.CodigoPostal),
            };
        }

        // Traslado (tipo T): receptor == emisor sustituido (CP107).
        if (e.Tipo == TipoCfdi.Traslado)
        {
            if (emisor is not null)
            {
                e = e with
                {
                    Receptor = e.Receptor with
                    {
                        Rfc = e.Emisor.Rfc,
                        Nombre = e.Emisor.Nombre,
                        RegimenFiscal = e.Emisor.RegimenFiscal,
                        CodigoPostal = e.Emisor.LugarExpedicion,
                    },
                };
            }

            return PropagarSustitucionACartaPorte(e);
        }

        var receptorSustituible = receptor is not null
            && !e.Receptor.EsGenerico
            && !EsRfcGenerico(e.Receptor.Rfc)
            && string.Equals(e.Receptor.Pais, "MEX", StringComparison.OrdinalIgnoreCase);

        if (receptorSustituible)
        {
            e = e with
            {
                Receptor = e.Receptor with
                {
                    Rfc = receptor!.Rfc,
                    Nombre = receptor.RazonSocial,
                    RegimenFiscal = receptor.RegimenFiscal,
                    CodigoPostal = receptor.CodigoPostal,
                },
            };
        }

        return e;
    }

    /// <summary>
    /// Mantiene el complemento Carta Porte consistente con las identidades ya
    /// sustituidas del comprobante de traslado: el remitente es el emisor y el
    /// destinatario el receptor (mercancía propia), así que sus RFC/nombre en
    /// las ubicaciones deben seguir la sustitución sandbox. Si no, la ubicación
    /// conservaría el RFC real y divergería del Emisor/Receptor del CFDI.
    /// </summary>
    private static CfdiEmision PropagarSustitucionACartaPorte(CfdiEmision e)
    {
        if (e.ComplementoCartaPorte is null)
            return e;

        return e with
        {
            ComplementoCartaPorte = e.ComplementoCartaPorte with
            {
                RfcRemitente = e.Emisor.Rfc,
                NombreRemitente = e.Emisor.Nombre,
                RfcDestinatario = e.Receptor.Rfc,
                NombreDestinatario = e.Receptor.Nombre,
            },
        };
    }

    /// <summary>RFC genéricos del SAT: nacional (público en general) y extranjero.</summary>
    private static bool EsRfcGenerico(string rfc) =>
        string.Equals(rfc, "XAXX010101000", StringComparison.OrdinalIgnoreCase)
        || string.Equals(rfc, "XEXX010101000", StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<string?> ResolverXmlTimbradoAsync(
        Fiscalapi.Abstractions.IFiscalApiClient sdk, string invoiceId, string? invoiceBase64)
    {
        if (!string.IsNullOrWhiteSpace(invoiceBase64))
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(invoiceBase64));
            }
            catch (FormatException)
            {
                // No era base64 — podría venir ya como XML plano.
                return invoiceBase64;
            }
        }

        try
        {
            var file = await sdk.Invoices.GetXmlAsync(invoiceId);
            if (file.Succeeded && !string.IsNullOrWhiteSpace(file.Data?.Base64File))
                return Encoding.UTF8.GetString(Convert.FromBase64String(file.Data.Base64File));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[FiscalApiTimbradoAdapter] No se pudo recuperar el XML timbrado de {InvoiceId}; se persiste sin XML.",
                invoiceId);
        }

        return null;
    }

    private static TimbradoResultado Fallido(string codigo, string? mensaje) => new(
        TimbradoEstado.Fallido, null, null, null, null, null, null, null, codigo, mensaje);

    /// <summary>Extrae el código de validación SAT (p.ej. CFDI40139) del mensaje del PAC.</summary>
    private static string? ExtraerCodigoSat(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return null;
        var match = CodigoSatRegex().Match(mensaje);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"CFDI\d{5}")]
    private static partial Regex CodigoSatRegex();
}
