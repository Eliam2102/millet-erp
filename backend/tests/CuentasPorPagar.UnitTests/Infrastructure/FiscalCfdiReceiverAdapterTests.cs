using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Infrastructure;

/// <summary>
/// Tests del adapter PR-14 que recibe CFDIs cosechados del
/// <c>DescargaPollerWorker</c> (metadata + XML) y los persiste como
/// <see cref="CfdiRecibido"/> en estado <see cref="EstadoCfdiRecibido.PorProcesar"/>.
/// </summary>
public sealed class FiscalCfdiReceiverAdapterTests
{
    private const string Uuid = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B";
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SolicitudId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] FakeXml = "fake-xml-bytes"u8.ToArray();

    [Fact]
    public async Task IngresarCfdi_uuid_nuevo_parsea_XML_persiste_PorProcesar_y_guarda_blob()
    {
        var (adapter, db, blob, ctx) = Build();
        try
        {
            var payload = NewPayload();

            await adapter.IngresarCfdiAsync(payload, CancellationToken.None);

            db.ChangeTracker.Clear();
            var cfdi = await db.CfdisRecibidos.SingleAsync();
            cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
            cfdi.UuidCfdi.Valor.Should().Be(Uuid);
            cfdi.Total.Should().Be(1160m);                       // del XML parseado
            cfdi.Subtotal.Should().Be(1000m);                    // del XML parseado
            cfdi.ImpuestosTrasladados.Should().Be(160m);
            cfdi.Folio.Should().Be("F1");                         // solo viene del XML, no del meta
            cfdi.Serie.Should().Be("A");
            cfdi.SolicitudDescargaId.Should().Be(SolicitudId);
            cfdi.RequestIdExternoFiscalApi.Should().Be("req-ext-99");
            cfdi.XmlBlobRef.Should().Be(blob.UltimoRefDevuelto);
            cfdi.XmlHashSha256.Should().NotBeNullOrEmpty();
            cfdi.CanalOrigen.Should().Be(CanalOrigenCfdi.DescargaSat);
            blob.Subidas.Should().HaveCount(1);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task IngresarCfdi_uuid_ya_existente_es_idempotente_no_duplica()
    {
        var (adapter, db, blob, ctx) = Build();
        try
        {
            var payload = NewPayload();
            await adapter.IngresarCfdiAsync(payload, CancellationToken.None);
            await adapter.IngresarCfdiAsync(payload, CancellationToken.None);

            db.ChangeTracker.Clear();
            (await db.CfdisRecibidos.CountAsync()).Should().Be(1);
            // El segundo no debería re-subir blob: el dedupe es ANTES del blob.
            blob.Subidas.Should().HaveCount(1);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task IngresarCfdi_xml_no_parseable_no_persiste_y_no_truena()
    {
        var (adapter, db, blob, ctx) = Build(parserFactory: () => new ThrowingParser());
        try
        {
            var payload = NewPayload();
            await adapter.IngresarCfdiAsync(payload, CancellationToken.None);

            db.ChangeTracker.Clear();
            (await db.CfdisRecibidos.CountAsync()).Should().Be(0);
            // El parser falla antes del blob upload → no se sube nada.
            blob.Subidas.Should().BeEmpty();
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task IngresarCfdi_persiste_audit_trail_solicitud_y_request()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);

            db.ChangeTracker.Clear();
            var cfdi = await db.CfdisRecibidos.SingleAsync();
            cfdi.SolicitudDescargaId.Should().Be(SolicitudId);
            cfdi.RequestIdExternoFiscalApi.Should().Be("req-ext-99");
        }
        finally { ctx.Dispose(); }
    }

    // ─────────────────────── Estatus SAT (86e3ke0z5) ────────────────────

    [Fact]
    public async Task Nuevo_Vigente_persiste_estatus_vigente_sin_fecha()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
            c.FechaCancelacionSat.Should().BeNull();
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Nuevo_Cancelado_persiste_estatus_y_fecha()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            c.FechaCancelacionSat.Should().Be(FechaCancel);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Existente_Vigente_luego_Cancelado_actualiza_solo_estatus()
    {
        var (adapter, db, blob, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            var antes = await CfdiAsync(db);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            (await db.CfdisRecibidos.CountAsync()).Should().Be(1);
            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            c.FechaCancelacionSat.Should().Be(FechaCancel);
            c.XmlBlobRef.Should().Be(antes.XmlBlobRef);
            c.XmlHashSha256.Should().Be(antes.XmlHashSha256);
            c.CanalOrigen.Should().Be(antes.CanalOrigen);
            c.FechaRecepcion.Should().Be(antes.FechaRecepcion);
            blob.Subidas.Should().HaveCount(1);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Pasivo_Capturada_y_CFDI_Cancelado_pasa_a_revision_sin_cancelar()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Capturada);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            var r = await ReloadAsync(db, f.Id);
            r.Estado.Should().Be(EstadoPasivo.EnRevision);
            r.MotivoRevisionId.Should().Be(Factura.MotivoCfdiCanceladoSatId);
            r.DependenciaRevisoraId.Should().Be(CancelacionPasivoSolicitadaHandler.DependenciaCxpId);
            r.Estado.Should().NotBe(EstadoPasivo.Cancelada);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Mismo_payload_Cancelado_dos_veces_no_truena_ni_cambia_la_revision()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Capturada);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            var primera = (await ReloadAsync(db, f.Id)).FechaEntradaRevision;

            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();

            var r = await ReloadAsync(db, f.Id);
            r.Estado.Should().Be(EstadoPasivo.EnRevision);
            r.FechaEntradaRevision.Should().Be(primera);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Reentrega_Cancelado_tras_liberar_revision_no_reenvia_a_revision()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Capturada);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            var enRev = await ReloadAsync(db, f.Id);
            enRev.Estado.Should().Be(EstadoPasivo.EnRevision);
            enRev.LiberarRevision("Aclarado con el proveedor", null, Ahora);
            await db.SaveChangesAsync();
            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.Capturada);

            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.Capturada);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Pasivo_Autorizada_y_CFDI_Cancelado_pasa_a_revision()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Autorizada);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.EnRevision);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Pasivo_Cancelada_no_truena_actualiza_CFDI_y_no_toca_la_factura()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Cancelada);
            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();

            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.Cancelada);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Pasivo_Pagada_no_truena_y_sigue_Pagada()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Pagada);
            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();

            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.Pagada);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Pasivo_EnRevision_por_falta_REPP_no_sobrescribe_el_motivo()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.EnRevisionRepp);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            var r = await ReloadAsync(db, f.Id);
            r.Estado.Should().Be(EstadoPasivo.EnRevision);
            r.MotivoRevisionId.Should().Be(Factura.MotivoFaltaReppId);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Cancelado_a_Vigente_limpia_fecha_y_no_toca_la_factura()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Capturada);
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await adapter.IngresarCfdiAsync(NewPayload("Vigente"), CancellationToken.None);

            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
            c.FechaCancelacionSat.Should().BeNull();
            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.EnRevision);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task DocumentoDestino_inexistente_no_truena_y_actualiza_estatus()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await SeedFacturaAsync(db, EstadoSeed.Capturada, destinoOverride: Guid.NewGuid());
            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();
            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task DocumentoDestino_anticipo_existente_no_truena_actualiza_estatus_y_no_lo_toca()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var a = Domain.AnticipoProveedor.AnticipoProveedor.Capturar(
                empresaId: EmpresaId, cfdiRecibidoId: Guid.NewGuid(), uuidCfdi: Uuid, proveedorId: Guid.NewGuid(),
                serie: "FANT", folioProveedor: "ANT-001", fechaCfdi: Ahora, moneda: "MXN", tipoCambio: null,
                montoEntregado: 1000m, ordenCompraId: Guid.NewGuid(), capturadoPor: Guid.NewGuid(), ahora: Ahora);
            db.AnticiposProveedor.Add(a);
            await db.SaveChangesAsync();
            await SeedFacturaAsync(db, EstadoSeed.Capturada, destinoOverride: a.Id);

            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();

            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            var despues = await db.AnticiposProveedor.SingleAsync(x => x.Id == a.Id);
            despues.Estado.Should().Be(Domain.AnticipoProveedor.EstadoAnticipo.Abierto);
            despues.SaldoAmortizable.Should().Be(1000m);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Misma_UUID_de_otra_empresa_no_toca_la_fila_existente()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            await adapter.IngresarCfdiAsync(
                NewPayload("Cancelado", FechaCancel, empresaId: Guid.NewGuid()), CancellationToken.None);

            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
            c.FechaCancelacionSat.Should().BeNull();
        }
        finally { ctx.Dispose(); }
    }

    [Theory]
    [InlineData("CANCELADO")]
    [InlineData("cancelado")]
    public async Task Estatus_cancelado_se_interpreta_sin_importar_mayusculas(string estatus)
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            await adapter.IngresarCfdiAsync(NewPayload(estatus, FechaCancel), CancellationToken.None);
            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        }
        finally { ctx.Dispose(); }
    }

    [Theory]
    [InlineData("Xyz")]
    [InlineData("")]
    public async Task Estatus_desconocido_o_vacio_no_cambia_el_estatus_ni_truena(string estatus)
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            var act = () => adapter.IngresarCfdiAsync(NewPayload(estatus, FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();

            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
            c.FechaCancelacionSat.Should().BeNull();
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Cancelado_sin_fecha_guarda_estatus_y_fecha_nula()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload("Cancelado", null), CancellationToken.None);
            var c = await CfdiAsync(db);
            c.EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
            c.FechaCancelacionSat.Should().BeNull();
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task UUID_con_espacios_y_minusculas_actualiza_la_fila_existente()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            await adapter.IngresarCfdiAsync(NewPayload(), CancellationToken.None);
            await adapter.IngresarCfdiAsync(
                NewPayload("Cancelado", FechaCancel, uuid: $"  {Uuid.ToLowerInvariant()}  "), CancellationToken.None);

            (await db.CfdisRecibidos.CountAsync()).Should().Be(1);
            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task XML_invalido_con_UUID_existente_igual_actualiza_el_estatus()
    {
        // El dedupe y la actualización de estatus ocurren antes de parsear el XML.
        var (adapter, db, _, ctx) = Build(parserFactory: () => new ThrowingParser());
        try
        {
            await SeedFacturaAsync(db, EstadoSeed.Capturada);
            var act = () => adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);
            await act.Should().NotThrowAsync();
            (await CfdiAsync(db)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        }
        finally { ctx.Dispose(); }
    }

    [Fact]
    public async Task Doscientas_reentregas_Cancelado_no_truenan()
    {
        var (adapter, db, _, ctx) = Build();
        try
        {
            var f = await SeedFacturaAsync(db, EstadoSeed.Capturada);
            for (var i = 0; i < 200; i++)
                await adapter.IngresarCfdiAsync(NewPayload("Cancelado", FechaCancel), CancellationToken.None);

            (await ReloadAsync(db, f.Id)).Estado.Should().Be(EstadoPasivo.EnRevision);
            (await db.CfdisRecibidos.CountAsync()).Should().Be(1);
        }
        finally { ctx.Dispose(); }
    }

    // ─────────────────────── Helpers ────────────────────────────────────

    private static CfdiCosechadoPayload NewPayload(
        string? estatus = "Vigente", DateTimeOffset? fechaCancelacion = null,
        string? uuid = null, Guid? empresaId = null) =>
        new(EmpresaId:             empresaId ?? EmpresaId,
            RfcReceptorMillet:     "MIL010101AAA",
            Uuid:                  uuid ?? Uuid,
            RfcEmisor:             "PROV010101AAA",
            NombreEmisor:          "Proveedor SA",
            RfcReceptor:           "MIL010101AAA",
            NombreReceptor:        "Millet",
            FechaCfdi:             Ahora,
            FechaCertificacionSat: Ahora,
            Total:                 1160m,
            TipoComprobante:       "I",
            EstatusSat:            estatus!,
            FechaCancelacion:      fechaCancelacion,
            SolicitudDescargaId:   SolicitudId,
            RequestIdExterno:      "req-ext-99",
            XmlBytes:              FakeXml);

    private static readonly DateTimeOffset FechaCancel = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private enum EstadoSeed { Capturada, Autorizada, Cancelada, Pagada, EnRevisionRepp }

    /// <summary>Siembra un CFDI convertido en pasivo, ligado a una factura en el estado pedido.</summary>
    private static async Task<Factura> SeedFacturaAsync(
        CuentasPorPagarDbContext db, EstadoSeed estado, Guid? destinoOverride = null)
    {
        var f = Factura.CapturarConOc(
            empresaId: EmpresaId, cfdiRecibidoId: null, uuidCfdi: null, proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-9A", serieProveedor: "A", fechaDocumento: Ahora, fechaContabilizacion: Ahora,
            fechaVencimiento: DateOnly.FromDateTime(Ahora.UtcDateTime.AddDays(30)), moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null, tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: Ahora);

        switch (estado)
        {
            case EstadoSeed.Autorizada: f.Autorizar(null, Ahora); break;
            case EstadoSeed.Cancelada: f.Cancelar(MotivoCancelacion.OtroConTexto, "prueba", null, Ahora); break;
            case EstadoSeed.Pagada:
                f.Autorizar(null, Ahora);
                f.RegistrarPago(1160m, Ahora, "pago prueba");
                break;
            case EstadoSeed.EnRevisionRepp:
                f.EnviarARevision(Factura.MotivoFaltaReppId, CancelacionPasivoSolicitadaHandler.DependenciaCxpId, null, Ahora);
                break;
        }

        var cfdi = CfdiRecibido.Ingresar(
            EmpresaId, UuidCfdi.Parse(Uuid), RfcMexicano.Parse("PROV010101AAA"), RfcMexicano.Parse("MIL010101AAA"),
            Millet.CuentasPorPagar.Domain.Cfdi.TipoCfdi.Ingreso, "F1", "A", Ahora, 1160m, 1000m, 160m, 0m, "MXN", null,
            CanalOrigenCfdi.DescargaSat, Ahora, "cxp/blob.xml", null, "hash",
            estatusSat: EstatusSatCfdi.Vigente);
        cfdi.MarcarConvertidoEnPasivo(destinoOverride ?? f.Id);
        db.FacturasProveedor.Add(f);
        db.CfdisRecibidos.Add(cfdi);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return f;
    }

    private static async Task<Factura> ReloadAsync(CuentasPorPagarDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        return await db.FacturasProveedor.SingleAsync(x => x.Id == id);
    }

    private static async Task<CfdiRecibido> CfdiAsync(CuentasPorPagarDbContext db)
    {
        db.ChangeTracker.Clear();
        return await db.CfdisRecibidos.SingleAsync();
    }

    private static (FiscalCfdiReceiverAdapter Adapter,
                    CuentasPorPagarDbContext Db,
                    StubCfdiBlobStorage Blob,
                    IDisposable Disposable)
        Build(Func<IXmlCfdiParser>? parserFactory = null)
    {
        var dbName = $"adapter-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new CuentasPorPagarDbContext(options, new BypassedEmpresaContext(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        var clock = new FakeClock(Ahora);
        var parser = parserFactory?.Invoke() ?? new CannedParser();
        var blob = new StubCfdiBlobStorage();
        var adapter = new FiscalCfdiReceiverAdapter(db, parser, blob, clock,
            NullLogger<FiscalCfdiReceiverAdapter>.Instance);
        return (adapter, db, blob, db);
    }

    /// <summary>
    /// Parser canónico que devuelve datos coherentes con
    /// <see cref="NewPayload"/> — ignora el contenido del XML y devuelve
    /// el mismo DTO siempre. Suficiente para tests del adapter (la
    /// validación real del parser vive en <c>XmlCfdiParserTests</c>).
    /// </summary>
    private sealed class CannedParser : IXmlCfdiParser
    {
        public DatosCfdiParseados Parsear(Stream xml) => Canned();
        public DatosCfdiParseados Parsear(string xml) => Canned();
        private static DatosCfdiParseados Canned() => new(
            UuidCfdi:            Uuid,
            RfcEmisor:            "PROV010101AAA",
            RazonSocialEmisor:    "Proveedor SA",
            RfcReceptor:          "MIL010101AAA",
            RazonSocialReceptor:  "Millet",
            Tipo:                 Millet.CuentasPorPagar.Domain.Cfdi.TipoCfdi.Ingreso,
            Folio:                "F1",
            Serie:                "A",
            FechaCfdi:            Ahora,
            Total:                1160m,
            Subtotal:             1000m,
            ImpuestosTrasladados: 160m,
            Retenciones:          0m,
            Moneda:               "MXN",
            TipoCambio:           null,
            Lineas:               Array.Empty<LineaCfdiParseada>());
    }

    private sealed class ThrowingParser : IXmlCfdiParser
    {
        public DatosCfdiParseados Parsear(Stream xml) =>
            throw new CfdiParseException("CFDI_XML_CORRUPTO", "fake corrupted xml");
        public DatosCfdiParseados Parsear(string xml) =>
            throw new CfdiParseException("CFDI_XML_CORRUPTO", "fake corrupted xml");
    }

    private sealed class StubCfdiBlobStorage : ICfdiBlobStorage
    {
        public List<string> Subidas { get; } = new();
        public string? UltimoRefDevuelto { get; private set; }

        public async Task<string> GuardarXmlAsync(
            string uuid, DateTimeOffset fechaCfdi, Stream contenido, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            await contenido.CopyToAsync(ms, cancellationToken);
            var path = $"cxp/{fechaCfdi:yyyy/MM}/cfdi/{uuid}.xml";
            Subidas.Add(path);
            UltimoRefDevuelto = path;
            return path;
        }

        public Task<string?> GuardarPdfAsync(
            string uuid, DateTimeOffset fechaCfdi, Stream? contenido, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);

        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow) { UtcNow = utcNow; }
        public DateTimeOffset UtcNow { get; }
    }
}
