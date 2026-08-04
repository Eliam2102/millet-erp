using System.Text;
using Fiscalapi.Abstractions;
using Fiscalapi.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using SdkModels = Fiscalapi.Models;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

/// <summary>
/// Tests del <see cref="FiscalApiTimbradoAdapter"/> (F12-PR2): mapeo
/// CfdiEmision→Invoice, traducción del envelope a la semántica
/// Timbrado/Fallido/EnProceso, cancelación y estatus.
/// </summary>
public sealed class FiscalApiTimbradoAdapterTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    // ───────────────────────── Fakes del SDK ─────────────────────────

    private sealed class FakeInvoiceService : IInvoiceService
    {
        public SdkModels.Invoice? UltimoCreado { get; private set; }
        public SdkModels.CancelInvoiceRequest? UltimaCancelacion { get; private set; }
        public SdkModels.InvoiceStatusRequest? UltimoEstatus { get; private set; }

        public Func<SdkModels.Invoice, ApiResponse<SdkModels.Invoice>>? OnCreate { get; init; }
        public ApiResponse<SdkModels.CancelInvoiceResponse>? CancelResponse { get; init; }
        public ApiResponse<SdkModels.InvoiceStatusResponse>? StatusResponse { get; init; }
        public ApiResponse<FileResponse>? XmlResponse { get; init; }

        public Task<ApiResponse<SdkModels.Invoice>> CreateAsync(SdkModels.Invoice model)
        {
            UltimoCreado = model;
            if (OnCreate is null) throw new NotImplementedException();
            var result = OnCreate(model);
            if (result is null) throw new HttpRequestException("simulated network failure");
            return Task.FromResult(result);
        }

        public Task<ApiResponse<SdkModels.CancelInvoiceResponse>> CancelAsync(SdkModels.CancelInvoiceRequest requestModel)
        {
            UltimaCancelacion = requestModel;
            return Task.FromResult(CancelResponse ?? throw new NotImplementedException());
        }

        public Task<ApiResponse<SdkModels.InvoiceStatusResponse>> GetStatusAsync(SdkModels.InvoiceStatusRequest requestModel)
        {
            UltimoEstatus = requestModel;
            return Task.FromResult(StatusResponse ?? throw new NotImplementedException());
        }

        public Task<ApiResponse<FileResponse>> GetXmlAsync(string id) =>
            Task.FromResult(XmlResponse ?? new ApiResponse<FileResponse> { Succeeded = false, Message = "sin xml" });

        public Task<ApiResponse<FileResponse>> GetPdfAsync(SdkModels.CreatePdfRequest requestModel) => throw new NotImplementedException();
        public Task<ApiResponse<bool>> SendAsync(SdkModels.SendInvoiceRequest requestModel) => throw new NotImplementedException();
        public Task<ApiResponse<PagedList<SdkModels.Invoice>>> GetListAsync(int pageNumber, int pageSize) => throw new NotImplementedException();
        public Task<ApiResponse<SdkModels.Invoice>> GetByIdAsync(string id, bool details = false) => throw new NotImplementedException();
        public Task<ApiResponse<SdkModels.Invoice>> UpdateAsync(string id, SdkModels.Invoice model) => throw new NotImplementedException();
        public Task<ApiResponse<bool>> DeleteAsync(string id) => throw new NotImplementedException();
    }

    private sealed class FakeSdkClient(FakeInvoiceService invoices) : IFiscalApiClient
    {
        public IInvoiceService Invoices { get; } = invoices;
        public IPersonService Persons => throw new NotImplementedException();
        public IProductService Products => throw new NotImplementedException();
        public IApiKeyService ApiKeys => throw new NotImplementedException();
        public ITaxFileService TaxFiles => throw new NotImplementedException();
        public ICatalogService Catalogs => throw new NotImplementedException();
        public IDownloadCatalogService DownloadCatalogs => throw new NotImplementedException();
        public IDownloadRuleService DownloadRules => throw new NotImplementedException();
        public IDownloadRequestService DownloadRequests => throw new NotImplementedException();
        public IStampService Stamps => throw new NotImplementedException();
        public IManifestService Manifests => throw new NotImplementedException();
    }

    private sealed class FakeFactory(FakeInvoiceService invoices) : IFiscalApiSdkClientFactory
    {
        public Task<IFiscalApiClient> GetClientAsync(Guid empresaId, CancellationToken cancellationToken) =>
            Task.FromResult<IFiscalApiClient>(new FakeSdkClient(invoices));
    }

    private sealed class FakeConfigResolver(ConfiguracionPacResuelta? config) : IConfiguracionPacResolver
    {
        public Task<ConfiguracionPacResuelta?> ResolverAsync(
            Guid empresaId, ProveedorPac proveedor, CancellationToken cancellationToken) =>
            Task.FromResult(config);

        public void Invalidar(Guid empresaId, ProveedorPac proveedor) { }
    }

    private static FiscalApiTimbradoAdapter Adapter(
        FakeInvoiceService invoices, ConfiguracionPacResuelta? config = null) =>
        new(new FakeFactory(invoices), new FakeConfigResolver(config),
            NullLogger<FiscalApiTimbradoAdapter>.Instance);

    /// <summary>CSD de prueba resuelto — TimbrarAsync lo exige (emisión por valores).</summary>
    private static readonly CsdResuelto CsdPrueba = new("CER_B64", "KEY_B64", "12345678a");

    private static ConfiguracionPacResuelta ConfigSandbox(
        IdentidadSandboxResuelta? emisor = null, IdentidadSandboxResuelta? receptor = null) => new(
        ConfiguracionId: Guid.NewGuid(),
        EmpresaId: EmpresaId,
        Proveedor: ProveedorPac.FiscalApi,
        BaseUrl: "https://test.fiscalapi.com",
        ApiKey: "sk_test_x",
        Activo: true,
        EmisorSandbox: emisor,
        ReceptorSandbox: receptor,
        Csd: CsdPrueba);

    private static readonly IdentidadSandboxResuelta EmisorEku =
        new("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "42501");

    private static readonly IdentidadSandboxResuelta ReceptorCacx =
        new("CACX7605101P8", "XOCHILT CASAS CHAVEZ", "612", "36257");

    // ───────────────────────── Emisiones de prueba ─────────────────────────

    private static CfdiEmision EmisionIngreso() => new(
        EmpresaId: EmpresaId,
        Tipo: TipoCfdi.Ingreso,
        Emisor: new EmisorCfdi("AAA010101AAA", "Millet", "601", "76120"),
        Receptor: new ReceptorCfdi("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false),
        Serie: "FA",
        Folio: "123",
        FechaLocal: new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.FromHours(-6)),
        Moneda: "MXN",
        TipoCambio: null,
        FormaPago: "01",
        MetodoPago: "PUE",
        Exportacion: "01",
        Conceptos:
        [
            new ConceptoCfdi("01010101", "H87", 2m, "Vidrio", 100m, 200m, 0m, "02",
                [new ImpuestoConceptoCfdi(200m, "002", "Tasa", 0.16m, 32m, EsRetencion: false)],
                new PedimentoCfdi("24 47 3807 4002345")),
        ],
        Relaciones: [new RelacionCfdiEmision("07", "11111111-1111-1111-1111-111111111111")],
        ReferenciaInterna: Guid.NewGuid());

    private static CfdiEmision EmisionPago() => EmisionIngreso() with
    {
        Tipo = TipoCfdi.Pago,
        Moneda = "XXX",
        Conceptos =
        [
            new ConceptoCfdi("84111506", "ACT", 1m, "Pago", 0m, 0m, 0m, "01", []),
        ],
        Relaciones = [],
        ComplementoPago = new ComplementoPagoCfdi(
            FechaPago: new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.FromHours(-6)),
            FormaPago: "03",
            Moneda: "USD",
            TipoCambio: 18m,
            Monto: 400m,
            CuentaOrdenante: "012345",
            CuentaBeneficiaria: "678901",
            ReferenciaPago: "REF-1",
            Documentos:
            [
                new DocumentoPagoCfdi("22222222-2222-2222-2222-222222222222", null, null, "USD", 2, 1000m, 400m, 600m),
            ]),
    };

    private static ApiResponse<SdkModels.Invoice> RespuestaTimbrada(string uuid = "AAAAAAAA-1111-2222-3333-444444444444") => new()
    {
        Succeeded = true,
        HttpStatusCode = 200,
        Data = new SdkModels.Invoice
        {
            Id = "inv-1",
            Responses =
            [
                new SdkModels.InvoiceResponse
                {
                    InvoiceId = "inv-1",
                    InvoiceUuid = uuid,
                    InvoiceBase64Sello = "SELLO_CFDI_B64",
                    SatBase64Sello = "SELLO_SAT_B64",
                    SatCertificateNumber = "00001000000504465028",
                    InvoiceSignatureDate = new DateTime(2026, 7, 9, 12, 0, 5),
                    InvoiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<cfdi>timbrado</cfdi>")),
                },
            ],
        },
    };

    // ───────────────────────── Timbrado ─────────────────────────

    [Fact]
    public async Task Timbrar_mapea_invoice_y_devuelve_timbrado()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado);
        r.Uuid.Should().Be("AAAAAAAA-1111-2222-3333-444444444444");
        r.SelloCfdi.Should().Be("SELLO_CFDI_B64");
        r.SelloSat.Should().Be("SELLO_SAT_B64");
        r.NoCertificadoSat.Should().Be("00001000000504465028");
        r.XmlTimbrado.Should().Be("<cfdi>timbrado</cfdi>");

        var inv = invoices.UltimoCreado!;
        inv.TypeCode.Should().Be("I");
        inv.Series.Should().Be("FA");
        // El folio interno de Millet NO viaja: FiscalAPI lo calcula
        // internamente y rechaza folios del cliente.
        inv.Number.Should().BeNull();
        inv.ExpeditionZipCode.Should().Be("76120");
        inv.ExportCode.Should().Be("01");
        inv.PaymentFormCode.Should().Be("01");
        inv.PaymentMethodCode.Should().Be("PUE");
        inv.Issuer.Tin.Should().Be("AAA010101AAA");
        inv.Recipient.CfdiUseCode.Should().Be("G03");
        inv.Recipient.CountryId.Should().BeNull(); // nacional: sin residencia fiscal
        var item = inv.Items.Single();
        item.ItemCode.Should().Be("01010101");
        item.ItemSku.Should().Be("01010101"); // sin SKU propio → ClaveProdServ
        item.TaxObjectCode.Should().Be("02");
        item.CustomsInfo!.Single().CustomsNumber.Should().Be("24 47 3807 4002345");
        var tax = item.ItemTaxes!.Single();
        tax.TaxCode.Should().Be("002");
        tax.TaxFlagCode.Should().Be("T");
        tax.TaxRate.Should().Be(0.16m);
        inv.RelatedInvoices!.Single().RelationshipTypeCode.Should().Be("07");

        // Emisión por valores: el CSD viaja como exactamente 2 TaxCredentials.
        inv.Issuer.TaxCredentials.Should().HaveCount(2);
        var cer = inv.Issuer.TaxCredentials[0];
        cer.FileType.Should().Be(SdkModels.FileType.CertificateCsd);
        cer.Base64File.Should().Be("CER_B64");
        cer.Password.Should().Be("12345678a");
        var key = inv.Issuer.TaxCredentials[1];
        key.FileType.Should().Be(SdkModels.FileType.PrivateKeyCsd);
        key.Base64File.Should().Be("KEY_B64");
        key.Password.Should().Be("12345678a");
    }

    /// <summary>
    /// c_TasaOCuota exige la forma textual con 6 decimales ("0.160000") —
    /// CFDI40179 rechazó "0.16" (incidente 2026-07-12). El valor decimal es
    /// idéntico (0.16m == 0.160000m); lo que valida el SAT es la ESCALA, que
    /// el serializador JSON respeta. Por eso se asierta el ToString.
    /// </summary>
    [Theory]
    [InlineData("0.16", "0.160000")]
    [InlineData("0.160000", "0.160000")]
    [InlineData("0", "0.000000")]
    [InlineData("0.106667", "0.106667")]
    [InlineData("0.1066666666", "0.106667")] // redondea a 6 (retención IVA 2/3)
    public void TasaCatalogoSat_fuerza_escala_de_seis_decimales(string entrada, string esperado)
    {
        var tasa = decimal.Parse(entrada, System.Globalization.CultureInfo.InvariantCulture);

        FiscalApiTimbradoAdapter.TasaCatalogoSat(tasa)
            .ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(esperado);
    }

    [Fact]
    public async Task Timbrar_serializa_la_tasa_con_seis_decimales()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };

        await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        var tax = invoices.UltimoCreado!.Items.Single().ItemTaxes!.Single();
        tax.TaxRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("0.160000");
    }

    [Fact]
    public async Task Timbrar_persiste_folio_asignado_por_el_pac()
    {
        var respuesta = RespuestaTimbrada();
        respuesta.Data!.Number = "EKU9003173C9-51";
        var invoices = new FakeInvoiceService { OnCreate = _ => respuesta };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado);
        r.FolioPac.Should().Be("EKU9003173C9-51");
    }

    [Fact]
    public async Task Timbrar_concepto_con_sku_propio_lo_usa_como_item_sku()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var emision = EmisionIngreso() with
        {
            Conceptos =
            [
                new ConceptoCfdi("01010101", "H87", 2m, "Vidrio", 100m, 200m, 0m, "02", [],
                    NoIdentificacion: "VID-LAM-6MM"),
            ],
        };

        await Adapter(invoices, ConfigSandbox()).TimbrarAsync(emision, CancellationToken.None);

        invoices.UltimoCreado!.Items.Single().ItemSku.Should().Be("VID-LAM-6MM");
    }

    [Fact]
    public async Task Timbrar_sin_csd_configurado_es_fallido_sin_llamar_al_pac()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };

        // Config sin CSD y config inexistente: mismo rechazo pre-vuelo.
        var sinCsd = await Adapter(invoices, ConfigSandbox() with { Csd = null })
            .TimbrarAsync(EmisionIngreso(), CancellationToken.None);
        var sinConfig = await Adapter(invoices, config: null)
            .TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        sinCsd.Estado.Should().Be(TimbradoEstado.Fallido);
        sinCsd.ErrorCodigo.Should().Be("CSD_NO_CONFIGURADO");
        sinConfig.Estado.Should().Be(TimbradoEstado.Fallido);
        sinConfig.ErrorCodigo.Should().Be("CSD_NO_CONFIGURADO");
        invoices.UltimoCreado.Should().BeNull(); // nunca llegó al PAC
    }

    [Fact]
    public async Task Timbrar_tipo_pago_sin_forma_metodo_y_con_complemento()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };

        await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionPago(), CancellationToken.None);

        var inv = invoices.UltimoCreado!;
        inv.TypeCode.Should().Be("P");
        inv.PaymentFormCode.Should().BeNull();
        inv.PaymentMethodCode.Should().BeNull();
        // El complemento Pago 2.0 viaja en Complement.Payment (singular), NO en
        // Invoice.Payments — de lo contrario el PAC responde "No mapper found
        // for invoice request" (P9-H2). En tipo P tampoco se mandan Items.
        inv.Payments.Should().BeNull();
        inv.Items.Should().BeNull();
        var pago = inv.Complement!.Payment!;
        pago.PaymentFormCode.Should().Be("03");
        pago.Amount.Should().Be(400m);
        pago.ExchangeRate.Should().Be(18m);
        var doc = pago.PaidInvoices!.Single();
        doc.PartialityNumber.Should().Be(2);
        doc.RemainingBalance.Should().Be(600m);
        doc.TaxObjectCode.Should().Be("01");
    }

    [Fact]
    public async Task Timbrar_tipo_pago_emite_impuestos_dr_en_complement_payment()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var baseEmision = EmisionPago();
        var emision = baseEmision with
        {
            ComplementoPago = baseEmision.ComplementoPago! with
            {
                Documentos =
                [
                    new DocumentoPagoCfdi(
                        "22222222-2222-2222-2222-222222222222", null, null, "MXN", 1, 1160m, 580m, 580m,
                        ObjetoImpDR: "02", Equivalencia: 1m, Subtotal: 500m,
                        Impuestos: [new ImpuestoDocumentoPagoCfdi("002", "Tasa", 0.16m, EsRetencion: false, BaseDR: 500m)]),
                ],
            },
        };

        await Adapter(invoices, ConfigSandbox()).TimbrarAsync(emision, CancellationToken.None);

        var doc = invoices.UltimoCreado!.Complement!.Payment!.PaidInvoices!.Single();
        doc.TaxObjectCode.Should().Be("02");
        doc.Equivalence.Should().Be(1m);
        doc.Subtotal.Should().Be(500m);
        var tax = doc.PaidInvoiceTaxes!.Single();
        tax.Base.Should().Be(500m);
        tax.TaxCode.Should().Be("002");
        tax.TaxTypeCode.Should().Be("Tasa");
        tax.TaxRate.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be("0.160000");
        tax.TaxFlagCode.Should().Be("T");
    }

    [Fact]
    public async Task Timbrar_rechazo_del_pac_es_fallido_con_codigo_sat()
    {
        var invoices = new FakeInvoiceService
        {
            OnCreate = _ => new ApiResponse<SdkModels.Invoice>
            {
                Succeeded = false,
                HttpStatusCode = 400,
                Message = "CFDI40139 - El nombre del emisor no coincide.",
                Details = "Emisor@Nombre",
            },
        };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Fallido);
        r.ErrorCodigo.Should().Be("CFDI40139");
        r.ErrorMensaje.Should().Contain("Emisor@Nombre");
    }

    [Fact]
    public async Task Timbrar_excepcion_de_red_es_enproceso_ambiguo()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => null! };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.EnProceso);
        r.ErrorCodigo.Should().Be("PAC_SIN_RESPUESTA");
    }

    [Fact]
    public async Task Timbrar_succeeded_sin_uuid_es_enproceso()
    {
        var invoices = new FakeInvoiceService
        {
            OnCreate = _ => new ApiResponse<SdkModels.Invoice>
            {
                Succeeded = true,
                Data = new SdkModels.Invoice { Id = "inv-2", Responses = [] },
            },
        };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.EnProceso);
        r.ErrorCodigo.Should().Be("PAC_RESPUESTA_INCOMPLETA");
    }

    [Fact]
    public async Task Timbrar_con_cce_construye_complement_comercio_exterior()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var emision = EmisionIngreso() with
        {
            Exportacion = "02",
            ComplementoCce = new ComplementoCceCfdi(
                TipoOperacion: "2",
                Incoterm: "FOB",
                TipoCambioUsd: 18.5m,
                ReceptorNumRegIdTrib: "US123456789",
                ReceptorPaisResidencia: "USA",
                ClaveDePedimento: "A1",
                CertificadoOrigen: true,
                Domicilio: new DomicilioCceCfdi("123 Main St", "TX", "USA", "75001"),
                Mercancias:
                [
                    new MercanciaCceCfdi("1", "70109999", "06", 10m, 5.5m, 55m),
                ]),
        };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(emision, CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado); // ya NO devuelve COMPLEMENTO_NO_SOPORTADO
        var ce = invoices.UltimoCreado!.Complement!.ComercioExterior!;
        ce.IncotermId.Should().Be("FOB");
        ce.TipoCambioUSD.Should().Be(18.5m);
        ce.ClaveDePedimentoId.Should().Be("A1");
        ce.CertificadoOrigen.Should().Be(1);
        ce.MotivoTrasladoId.Should().BeNull(); // solo aplica a CFDI de traslado
        ce.Receptor!.NumRegIdTrib.Should().Be("US123456789");
        ce.Receptor.Domicilio!.Calle.Should().Be("123 Main St");
        ce.Receptor.Domicilio.Estado.Should().Be("TX");
        ce.Receptor.Domicilio.PaisId.Should().Be("USA");
        ce.Receptor.Domicilio.CodigoPostal.Should().Be("75001");
        var m = ce.Mercancias!.Single();
        m.NoIdentificacion.Should().Be("1");
        m.FraccionArancelariaId.Should().Be("70109999");
        m.CantidadAduana.Should().Be(10m);
        m.UnidadAduanaId.Should().Be("06");
        m.ValorUnitarioAduana.Should().Be(5.5m);
        // ValorDolares forzado a escala 2 (P10-H4): TotalUSD del SAT exige 2
        // decimales; FiscalAPI propaga la escala al XML (gotcha TasaCatalogoSat).
        m.ValorDolares.Should().Be(55m);
        decimal.GetBits(m.ValorDolares)[3].Should().Be(2 << 16); // escala decimal = 2
        invoices.UltimoCreado.Complement.CartaPorte.Should().BeNull();
    }

    [Fact]
    public async Task Timbrar_con_carta_porte_construye_complement_carta_porte()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var fechaSalida = new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.FromHours(-6));
        var fechaLlegada = fechaSalida.AddHours(4);
        var emision = EmisionIngreso() with
        {
            Tipo = TipoCfdi.Traslado,
            Conceptos =
            [
                new ConceptoCfdi("43211508", "H87", 10m, "Vidrio laminado", 0m, 0m, 0m, "01", []),
            ],
            Relaciones = [],
            ComplementoCartaPorte = new ComplementoCartaPorteCfdi(
                Origen: new UbicacionCartaPorteCfdi("Querétaro", "76120", "QUE", "MEX"),
                Destino: new UbicacionCartaPorteCfdi("CDMX", "06600", "CMX", "MEX"),
                RfcRemitente: "AAA010101AAA",
                NombreRemitente: "Millet",
                RfcDestinatario: "BBB010101BBB",
                NombreDestinatario: "Cliente SA",
                DistanciaKm: 220m,
                FechaSalida: fechaSalida,
                FechaLlegadaEstimada: fechaLlegada,
                Vehiculo: new VehiculoCartaPorteCfdi("ABC1234", "C2R2", 2022, 17.5m, "TPAF01", "PERM-1", "Seguros X", "POL-1"),
                Operador: new OperadorCartaPorteCfdi("CCC010101CC1", "Juan Chofer", "LIC-99"),
                IdCcpRelacionado: null,
                Mercancias:
                [
                    new MercanciaCartaPorteCfdi("43211508", "Vidrio laminado", "H87", 10m, 850m, MaterialPeligroso: false),
                    new MercanciaCartaPorteCfdi("12352128", "Silicón", "KGM", 2m, 150m, MaterialPeligroso: true),
                ]),
        };

        var r = await Adapter(invoices, ConfigSandbox()).TimbrarAsync(emision, CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado); // ya NO devuelve COMPLEMENTO_NO_SOPORTADO
        var cp = invoices.UltimoCreado!.Complement!.CartaPorte!;
        cp.TranspInternacId.Should().Be("No");
        cp.TotalDistRec.Should().Be(220m);

        cp.Ubicaciones.Should().HaveCount(2);
        var origen = cp.Ubicaciones![0];
        origen.TipoUbicacion.Should().Be("Origen");
        origen.IDUbicacion.Should().Be("OR000001");
        origen.RFCRemitenteDestinatario.Should().Be("AAA010101AAA");
        origen.NombreRemitenteDestinatario.Should().Be("Millet");
        origen.FechaHoraSalidaLlegada.Should().Be(fechaSalida.DateTime);
        origen.Domicilio!.EstadoId.Should().Be("QUE");
        origen.Domicilio.PaisId.Should().Be("MEX");
        origen.Domicilio.CodigoPostalId.Should().Be("76120");
        var destino = cp.Ubicaciones[1];
        destino.TipoUbicacion.Should().Be("Destino");
        destino.IDUbicacion.Should().Be("DE000001");
        destino.RFCRemitenteDestinatario.Should().Be("BBB010101BBB");
        destino.FechaHoraSalidaLlegada.Should().Be(fechaLlegada.DateTime);
        destino.DistanciaRecorrida.Should().Be(220m);
        destino.Domicilio!.CodigoPostalId.Should().Be("06600");

        cp.Mercancias.Should().HaveCount(2);
        cp.Mercancias![0].BienesTranspId.Should().Be("43211508");
        cp.Mercancias[0].PesoEnKg.Should().Be(850m);
        // No peligroso → se OMITE el atributo (null), no "No" (P10-H5, CP155).
        cp.Mercancias[0].MaterialPeligrosoId.Should().BeNull();
        cp.Mercancias[1].MaterialPeligrosoId.Should().Be("Sí");
        cp.PesoNetoTotal.Should().Be(1000m); // Σ PesoEnKg
        cp.UnidadPesoId.Should().Be("KGM");

        var auto = cp.Autotransporte!;
        auto.PermSCTId.Should().Be("TPAF01");
        auto.NumPermisoSCT.Should().Be("PERM-1");
        auto.ConfigVehicularId.Should().Be("C2R2");
        auto.PesoBrutoVehicular.Should().Be(17.5m);
        auto.PlacaVM.Should().Be("ABC1234");
        auto.AnioModeloVM.Should().Be(2022);
        auto.AseguraRespCivil.Should().Be("Seguros X");
        auto.PolizaRespCivil.Should().Be("POL-1");

        var figura = cp.TiposFigura!.Single();
        figura.TipoFiguraId.Should().Be("01");
        figura.RFCFigura.Should().Be("CCC010101CC1");
        figura.NumLicencia.Should().Be("LIC-99");
        figura.NombreFigura.Should().Be("Juan Chofer");

        invoices.UltimoCreado.Complement.ComercioExterior.Should().BeNull();
    }

    // ───────────────────────── Identidades sandbox ─────────────────────────

    [Fact]
    public async Task Timbrar_con_identidades_sandbox_sustituye_emisor_y_receptor()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var adapter = Adapter(invoices, ConfigSandbox(EmisorEku, ReceptorCacx));

        var r = await adapter.TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado);
        var inv = invoices.UltimoCreado!;
        inv.Issuer.Tin.Should().Be("EKU9003173C9");
        inv.Issuer.LegalName.Should().Be("ESCUELA KEMPER URGATE");
        inv.Issuer.TaxRegimeCode.Should().Be("601");
        inv.ExpeditionZipCode.Should().Be("42501");
        inv.Recipient.Tin.Should().Be("CACX7605101P8");
        inv.Recipient.LegalName.Should().Be("XOCHILT CASAS CHAVEZ");
        inv.Recipient.ZipCode.Should().Be("36257");
        inv.Recipient.CfdiUseCode.Should().Be("G03"); // el uso original se conserva
    }

    [Fact]
    public async Task Timbrar_sin_identidades_configuradas_no_sustituye()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var adapter = Adapter(invoices, ConfigSandbox()); // sandbox sin identidades

        await adapter.TimbrarAsync(EmisionIngreso(), CancellationToken.None);

        invoices.UltimoCreado!.Issuer.Tin.Should().Be("AAA010101AAA");
        invoices.UltimoCreado.Recipient.Tin.Should().Be("BBB010101BBB");
    }

    [Fact]
    public async Task Timbrar_no_sustituye_receptor_generico_ni_extranjero()
    {
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var adapter = Adapter(invoices, ConfigSandbox(EmisorEku, ReceptorCacx));
        var emision = EmisionIngreso() with
        {
            Receptor = new ReceptorCfdi(
                "XEXX010101000", "FOREIGN BUYER INC", "616", "00000", "S01", "USA",
                EsGenerico: true, NumRegIdTrib: "US123456789"),
        };

        await adapter.TimbrarAsync(emision, CancellationToken.None);

        var inv = invoices.UltimoCreado!;
        inv.Issuer.Tin.Should().Be("EKU9003173C9"); // el emisor sí
        inv.Recipient.Tin.Should().Be("XEXX010101000"); // el receptor genérico no
    }

    [Fact]
    public async Task Timbrar_traslado_sandbox_iguala_receptor_al_emisor_y_propaga_a_carta_porte()
    {
        // CFDI de Traslado (tipo T): el SAT exige Receptor.Rfc == Emisor.Rfc
        // (CP107). Al sustituir el emisor por la identidad sandbox, el receptor
        // debe seguirlo — NO sustituirse al receptor de prueba CACX (P10-H1).
        var invoices = new FakeInvoiceService { OnCreate = _ => RespuestaTimbrada() };
        var adapter = Adapter(invoices, ConfigSandbox(EmisorEku, ReceptorCacx));
        var fechaSalida = new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.FromHours(-6));
        var emision = EmisionIngreso() with
        {
            Tipo = TipoCfdi.Traslado,
            // Traslado de mercancía propia: receptor == emisor real.
            Receptor = new ReceptorCfdi("AAA010101AAA", "Millet", "601", "76120", "S01", "MEX", false),
            Conceptos = [new ConceptoCfdi("43211508", "H87", 10m, "Vidrio", 0m, 0m, 0m, "01", [])],
            Relaciones = [],
            ComplementoCartaPorte = new ComplementoCartaPorteCfdi(
                Origen: new UbicacionCartaPorteCfdi("Querétaro", "76120", "QUE", "MEX"),
                Destino: new UbicacionCartaPorteCfdi("CDMX", "06600", "CMX", "MEX"),
                RfcRemitente: "AAA010101AAA",
                NombreRemitente: "Millet",
                RfcDestinatario: "AAA010101AAA",
                NombreDestinatario: "Millet",
                DistanciaKm: 220m,
                FechaSalida: fechaSalida,
                FechaLlegadaEstimada: fechaSalida.AddHours(4),
                Vehiculo: new VehiculoCartaPorteCfdi("ABC1234", "C2R2", 2022, 17.5m, "TPAF01", "PERM-1", "Seguros X", "POL-1"),
                Operador: new OperadorCartaPorteCfdi("CCC010101CC1", "Juan Chofer", "LIC-99"),
                IdCcpRelacionado: null,
                Mercancias:
                [
                    new MercanciaCartaPorteCfdi("43211508", "Vidrio", "H87", 10m, 850m, MaterialPeligroso: false),
                ]),
        };

        var r = await adapter.TimbrarAsync(emision, CancellationToken.None);

        r.Estado.Should().Be(TimbradoEstado.Timbrado);
        var inv = invoices.UltimoCreado!;
        inv.Issuer.Tin.Should().Be("EKU9003173C9");
        // Receptor == emisor sustituido (NO el receptor sandbox CACX): evita CP107.
        inv.Recipient.Tin.Should().Be("EKU9003173C9");
        inv.Recipient.LegalName.Should().Be("ESCUELA KEMPER URGATE");
        // Las ubicaciones de la carta porte siguen la misma sustitución.
        var cp = inv.Complement!.CartaPorte!;
        cp.Ubicaciones![0].RFCRemitenteDestinatario.Should().Be("EKU9003173C9");
        cp.Ubicaciones[1].RFCRemitenteDestinatario.Should().Be("EKU9003173C9");
    }

    [Fact]
    public async Task Cancelar_con_emisor_sandbox_usa_rfc_de_prueba()
    {
        const string uuid = "AAAAAAAA-1111-2222-3333-444444444444";
        var invoices = new FakeInvoiceService
        {
            CancelResponse = new ApiResponse<SdkModels.CancelInvoiceResponse>
            {
                Succeeded = true,
                Data = new SdkModels.CancelInvoiceResponse { InvoiceUuids = new() { [uuid] = "202" } },
            },
        };
        var adapter = Adapter(invoices, ConfigSandbox(EmisorEku));

        await adapter.CancelarAsync(
            new CancelacionSolicitud(EmpresaId, uuid, "AAA010101AAA", "02"), CancellationToken.None);

        invoices.UltimaCancelacion!.Rfc.Should().Be("EKU9003173C9");
    }

    [Fact]
    public async Task ConsultarEstatus_con_identidades_sandbox_sustituye_rfcs()
    {
        var invoices = new FakeInvoiceService
        {
            StatusResponse = new ApiResponse<SdkModels.InvoiceStatusResponse>
            {
                Succeeded = true,
                Data = new SdkModels.InvoiceStatusResponse { Status = "Vigente" },
            },
        };
        var adapter = Adapter(invoices, ConfigSandbox(EmisorEku, ReceptorCacx));

        await adapter.ConsultarEstatusAsync(
            new EstatusCfdiSolicitud(EmpresaId, "uuid-1", "AAA010101AAA", "BBB010101BBB", 232m),
            CancellationToken.None);

        invoices.UltimoEstatus!.IssuerTin.Should().Be("EKU9003173C9");
        invoices.UltimoEstatus.RecipientTin.Should().Be("CACX7605101P8");
    }

    // ───────────────────────── Cancelación ─────────────────────────

    [Fact]
    public async Task Cancelar_succeeded_queda_enproceso_salvo_202()
    {
        const string uuid = "AAAAAAAA-1111-2222-3333-444444444444";
        var enProceso = new FakeInvoiceService
        {
            CancelResponse = new ApiResponse<SdkModels.CancelInvoiceResponse>
            {
                Succeeded = true,
                Data = new SdkModels.CancelInvoiceResponse { InvoiceUuids = new() { [uuid] = "201" } },
            },
        };
        var yaCancelado = new FakeInvoiceService
        {
            CancelResponse = new ApiResponse<SdkModels.CancelInvoiceResponse>
            {
                Succeeded = true,
                Data = new SdkModels.CancelInvoiceResponse { InvoiceUuids = new() { [uuid] = "202" } },
            },
        };
        var solicitud = new CancelacionSolicitud(EmpresaId, uuid, "AAA010101AAA", "02");

        (await Adapter(enProceso).CancelarAsync(solicitud, CancellationToken.None))
            .Estado.Should().Be(CancelacionEstado.EnProceso);
        (await Adapter(yaCancelado).CancelarAsync(solicitud, CancellationToken.None))
            .Estado.Should().Be(CancelacionEstado.Aceptada);

        var req = enProceso.UltimaCancelacion!;
        req.InvoiceUuid.Should().Be(uuid);
        req.Rfc.Should().Be("AAA010101AAA");
        req.CancellationReasonCode.Should().Be("02");
    }

    [Fact]
    public async Task Cancelar_envelope_fallido_es_error()
    {
        var invoices = new FakeInvoiceService
        {
            CancelResponse = new ApiResponse<SdkModels.CancelInvoiceResponse>
            {
                Succeeded = false,
                Message = "No cancelable",
            },
        };

        var r = await Adapter(invoices).CancelarAsync(
            new CancelacionSolicitud(EmpresaId, "uuid-x", "AAA010101AAA", "02"), CancellationToken.None);

        r.Estado.Should().Be(CancelacionEstado.Error);
        r.ErrorMensaje.Should().Contain("No cancelable");
    }

    // ───────────────────────── Estatus ─────────────────────────

    [Fact]
    public async Task ConsultarEstatus_mapea_request_y_response()
    {
        var invoices = new FakeInvoiceService
        {
            StatusResponse = new ApiResponse<SdkModels.InvoiceStatusResponse>
            {
                Succeeded = true,
                Data = new SdkModels.InvoiceStatusResponse
                {
                    Status = "Cancelado",
                    CancelableStatus = "No cancelable",
                    CancellationStatus = "Cancelado sin aceptación",
                },
            },
        };

        var r = await Adapter(invoices).ConsultarEstatusAsync(
            new EstatusCfdiSolicitud(EmpresaId, "uuid-1", "AAA010101AAA", "BBB010101BBB", 232m, "ABCD1234"),
            CancellationToken.None);

        r.EsVigente.Should().BeFalse();
        r.EstatusSat.Should().Be("Cancelado");
        r.EsCancelable.Should().BeFalse();
        r.EstadoCancelacion.Should().Be("Cancelado sin aceptación");

        var req = invoices.UltimoEstatus!;
        req.InvoiceUuid.Should().Be("uuid-1");
        req.IssuerTin.Should().Be("AAA010101AAA");
        req.RecipientTin.Should().Be("BBB010101BBB");
        req.InvoiceTotal.Should().Be(232m);
        req.Last8DigitsIssuerSignature.Should().Be("ABCD1234");
    }
}
