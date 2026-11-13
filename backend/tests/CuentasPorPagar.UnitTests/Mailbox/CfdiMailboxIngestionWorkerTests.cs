using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Workers;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.Compartido.Application.Ports;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Mailbox;

/// <summary>P8.1: worker del buzón contra el buzón simulado en disco (sin Graph). Casos: feliz, duplicado, falla, lento.</summary>
public sealed class CfdiMailboxIngestionWorkerTests : IDisposable
{
    private const string RfcMillet = "MIL010101AAA";
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "buzon-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, true); }

    [Fact]
    public async Task Correo_con_XML_y_PDF_entra_como_buzon_y_pasa_a_Processed()
    {
        using var h = Armar();
        Correo("m1", ("f.xml", Xml("11111111-1111-1111-1111-111111111111")), ("f.pdf", "%PDF"));

        await h.Worker.TickAsync(default);

        var cfdi = await h.Db.CfdisRecibidos.IgnoreQueryFilters().SingleAsync();
        cfdi.CanalOrigen.Should().Be(CanalOrigenCfdi.Mailbox);
        cfdi.EmpresaId.Should().Be(_empresa);
        Directory.Exists(Path.Combine(_raiz, "Processed", "m1")).Should().BeTrue();
        (await h.Db.BuzonRevisionCfdi.CountAsync()).Should().Be(0);
        var corrida = await h.Db.BuzonEjecuciones.SingleAsync();
        (corrida.Mensajes, corrida.Ingresados, corrida.Duplicados, corrida.ARevision).Should().Be((1, 1, 0, 0));
    }

    [Fact]
    public async Task Mismo_UUID_dos_veces_no_duplica_ni_va_a_revision()
    {
        using var h = Armar();
        Correo("a", ("f.xml", Xml("22222222-2222-2222-2222-222222222222")));
        Correo("b", ("f.xml", Xml("22222222-2222-2222-2222-222222222222")));

        await h.Worker.TickAsync(default);
        Correo("c", ("f.xml", Xml("22222222-2222-2222-2222-222222222222")));
        await h.Worker.TickAsync(default);

        (await h.Db.CfdisRecibidos.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await h.Db.BuzonRevisionCfdi.CountAsync()).Should().Be(0);
        (await h.Db.BuzonEjecuciones.SumAsync(e => e.Duplicados)).Should().Be(2);
        Directory.GetDirectories(Path.Combine(_raiz, "Inbox")).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_adjuntos_sin_XML_XML_roto_receptor_ajeno_y_XML_enorme_van_a_revision()
    {
        using var h = Armar(maxXml: 5000);
        Correo("sin-adjuntos");
        Correo("solo-pdf", ("f.pdf", "%PDF"));
        Correo("roto", ("f.xml", "<no-cierra"));
        Correo("ajeno", ("f.xml", Xml("33333333-3333-3333-3333-333333333333", receptor: "AJE010101AAA")));
        Correo("enorme", ("f.xml", new string('x', 6000)));

        await h.Worker.TickAsync(default);

        (await h.Db.CfdisRecibidos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        var motivos = await h.Db.BuzonRevisionCfdi.ToDictionaryAsync(b => b.MensajeId, b => b.Motivo);
        motivos.Should().Contain("sin-adjuntos", "SIN_XML").And.Contain("solo-pdf", "SIN_XML")
            .And.Contain("ajeno", "RFC_RECEPTOR_NO_MAPEADO").And.Contain("enorme", "XML_DEMASIADO_GRANDE");
        motivos["roto"].Should().StartWith("CFDI_");
        Directory.GetDirectories(Path.Combine(_raiz, "Failed")).Should().HaveCount(5);
        (await h.Db.BuzonEjecuciones.SingleAsync()).ARevision.Should().Be(5);
    }

    [Fact]
    public async Task Falla_del_buzon_se_registra_en_la_corrida_y_no_tumba_el_worker()
    {
        using var h = Armar(cliente: new BuzonQueFalla());

        await h.Worker.TickAsync(default);

        (await h.Db.BuzonEjecuciones.SingleAsync()).Error.Should().Be("MAILBOX_LIST_FALLO");
    }

    [Fact]
    public async Task Buzon_lento_respeta_la_cancelacion()
    {
        using var h = Armar(cliente: new BuzonLento());
        using var cts = new CancellationTokenSource(50);

        var act = () => h.Worker.TickAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Arnes Armar(long maxXml = 5 * 1024 * 1024, IMailboxClient? cliente = null)
    {
        var mailbox = new MailboxOptions { SimuladorDirectorio = _raiz, MaxXmlBytes = maxXml };
        var db = new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase($"buzon-{Guid.NewGuid():N}").Options;
        var instancia = cliente ?? new DirectorioMailboxClient(Options.Create(mailbox));
        return new Arnes(this, mailbox, db, instancia);
    }

    private void Correo(string nombre, params (string archivo, string contenido)[] adjuntos)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_raiz, "Inbox", nombre));
        foreach (var (archivo, contenido) in adjuntos) File.WriteAllText(Path.Combine(dir.FullName, archivo), contenido);
    }

    private static string Xml(string uuid, string receptor = RfcMillet) => $"""
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="I" Fecha="2026-10-09T12:00:00" Moneda="MXN" SubTotal="1000.00" Total="1160.00">
        <cfdi:Emisor Rfc="PROV010101AAA" Nombre="Proveedor ficticio"/><cfdi:Receptor Rfc="{receptor}" Nombre="Empresa ficticia"/>
        <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="30102400" Cantidad="1" ClaveUnidad="H87" Descripcion="Material" ValorUnitario="1000.00" Importe="1000.00"/></cfdi:Conceptos>
        <cfdi:Impuestos TotalImpuestosTrasladados="160.00"/>
        <cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>
        """;

    private sealed class Arnes : IDisposable
    {
        private readonly ServiceProvider _sp;
        public CfdiMailboxIngestionWorker Worker { get; }
        public CuentasPorPagarDbContext Db { get; }

        public Arnes(CfdiMailboxIngestionWorkerTests t, MailboxOptions mailbox, DbContextOptions<CuentasPorPagarDbContext> db, IMailboxClient cliente)
        {
            _sp = new ServiceCollection()
                .AddMediatR(c => c.RegisterServicesFromAssemblyContaining<IngresarCfdiHandler>())
                .AddSingleton(db)
                .AddScoped<ICurrentEmpresaContext, Contexto>()
                .AddScoped<Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbiertoStub>()
                .AddScoped<IClock, PeriodoAbiertoStub>()
                .AddScoped<CuentasPorPagarDbContext>()
                .AddSingleton<IXmlCfdiParser, XmlCfdiParser>()
                .AddSingleton<ICfdiBlobStorage, BlobStub>()
                .AddSingleton<IIntegrationEventPublisher, EventosStub>()
                .AddSingleton<IAuditOriginContext, OrigenStub>()
                .AddSingleton<IEmpresaResolverPort>(new EmpresaPorRfc(t._empresa))
                .AddSingleton(cliente)
                .BuildServiceProvider();
            var sched = new WorkerSchedulingOptions { RfcsReceptores = new Dictionary<string, Guid> { [RfcMillet] = t._empresa } };
            Worker = new CfdiMailboxIngestionWorker(
                _sp.GetRequiredService<IServiceScopeFactory>(), new Monitor<WorkerSchedulingOptions>(sched),
                new Monitor<MailboxOptions>(mailbox), NullLogger<CfdiMailboxIngestionWorker>.Instance);
            Db = new CuentasPorPagarDbContext(db, new Contexto(), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        }

        public void Dispose() { Db.Dispose(); _sp.Dispose(); }
    }

    // El buzón entra con el IngresarCfdiHandler real; la carga manual (M-257) consulta este puerto, el buzón no.
    private sealed class EmpresaPorRfc(Guid empresaId) : IEmpresaResolverPort
    {
        public Task<EmpresaResolution?> ResolveByRfcAsync(string rfcBuscado, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmpresaResolution?>(rfcBuscado.Equals(RfcMillet, StringComparison.OrdinalIgnoreCase) ? new(empresaId, RfcMillet, "Empresa ficticia") : null);
    }

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class Contexto : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed { get; private set; }
        public IDisposable Bypass() { IsBypassed = true; return new Fin(this); }
        private sealed class Fin(Contexto c) : IDisposable { public void Dispose() => c.IsBypassed = false; }
    }

    private sealed class OrigenStub : IAuditOriginContext
    {
        public string? Origin => null;
        public IDisposable SetOrigin(string origin) => new Fin();
        private sealed class Fin : IDisposable { public void Dispose() { } }
    }

    private sealed class BlobStub : ICfdiBlobStorage
    {
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fecha, Stream contenido, CancellationToken ct) => Task.FromResult($"test/{uuid}.xml");
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fecha, Stream? contenido, CancellationToken ct) => Task.FromResult<string?>(contenido is null ? null : $"test/{uuid}.pdf");
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class EventosStub : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BuzonQueFalla : IMailboxClient
    {
        public Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken ct) =>
            throw new MailboxException("MAILBOX_LIST_FALLO", "simulado");
        public Task<Stream> DescargarAttachmentAsync(string m, string a, CancellationToken ct) => throw new NotSupportedException();
        public Task MoverAProcesadoAsync(string m, CancellationToken ct) => Task.CompletedTask;
        public Task MoverAFallidoAsync(string m, string r, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BuzonLento : IMailboxClient
    {
        public async Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }
        public Task<Stream> DescargarAttachmentAsync(string m, string a, CancellationToken ct) => throw new NotSupportedException();
        public Task MoverAProcesadoAsync(string m, CancellationToken ct) => Task.CompletedTask;
        public Task MoverAFallidoAsync(string m, string r, CancellationToken ct) => Task.CompletedTask;
    }
}
