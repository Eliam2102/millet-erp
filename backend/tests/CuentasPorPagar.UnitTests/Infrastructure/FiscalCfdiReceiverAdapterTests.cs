using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Domain.Cfdi;
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

    // ─────────────────────── Helpers ────────────────────────────────────

    private static CfdiCosechadoPayload NewPayload() =>
        new(EmpresaId:             EmpresaId,
            RfcReceptorMillet:     "MIL010101AAA",
            Uuid:                  Uuid,
            RfcEmisor:             "PROV010101AAA",
            NombreEmisor:          "Proveedor SA",
            RfcReceptor:           "MIL010101AAA",
            NombreReceptor:        "Millet",
            FechaCfdi:             Ahora,
            FechaCertificacionSat: Ahora,
            Total:                 1160m,
            TipoComprobante:       "I",
            EstatusSat:            "Vigente",
            FechaCancelacion:      null,
            SolicitudDescargaId:   SolicitudId,
            RequestIdExterno:      "req-ext-99",
            XmlBytes:              FakeXml);

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
        var db = new CuentasPorPagarDbContext(options, new BypassedEmpresaContext());
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
