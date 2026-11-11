using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pedimentos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Facturas;

/// <summary>M-216: el pedimento de la factura retenida sale del expediente de Comercio exterior.</summary>
public sealed class PedimentoDesdeExpedienteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Pedimento = "25  16  3807  5000123";

    private sealed class Expedientes(Func<Guid, EmbarqueExportacionLectura> lectura) : IEmbarqueExportacionReadPort
    {
        public Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, Guid? contenedorId, CancellationToken ct) =>
            Task.FromResult(lectura(pedidoFacturableId));
    }

    private static EmbarqueExportacionLectura ConPedimento(string? numero = Pedimento) =>
        new(true, Guid.NewGuid(), FolioExpediente: "EXP-25-0007", NumeroPedimento: numero, ClavePedimento: "A1", FechaPedimento: new(2026, 11, 3));

    private static DbContextOptions<FacturacionDbContext> Opciones(string nombre) =>
        new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(nombre).Options;

    private static FacturaVenta Factura(Guid empresa, Guid? pedido, bool retener = true)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Vidrios del Sureste", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresa, "F-" + Guid.NewGuid().ToString("N")[..6], 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 11,
            (short)8, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "30171500", "Vidrio claro 6 mm importado", "H87", 1m, 100m, 0m, "02", 0.16m, null, null, requierePedimento: true);
        fv.RecalcularTotales();
        if (pedido is Guid p) fv.HeredarOrigenDeSustituida(p, null);
        if (retener) fv.MarcarPendientePedimento();
        return fv;
    }

    private static async Task<FacturacionDbContext> SembrarAsync(Guid empresa, string nombre, params FacturaVenta[] facturas)
    {
        var db = new FacturacionDbContext(Opciones(nombre), new FakeEmpresaContext(empresa));
        db.FacturasVenta.AddRange(facturas);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Lector_entrega_el_pedimento_del_expediente_de_la_factura_retenida()
    {
        var empresa = Guid.NewGuid(); var pedido = Guid.NewGuid(); var fv = Factura(empresa, pedido);
        using var db = await SembrarAsync(empresa, Guid.NewGuid().ToString(), fv);

        var leidos = await new ExpedientePedimentosReader(db, new Expedientes(_ => ConPedimento())).LeerPendientesAsync(50, CancellationToken.None);

        var leido = Assert.Single(leidos);
        Assert.Equal(fv.Id, leido.FacturaVentaId);
        Assert.Equal(Pedimento, leido.Pedimento);
        Assert.Equal(new DateOnly(2026, 11, 3), leido.FechaDocAduanero);
    }

    [Fact]
    public async Task Lector_omite_sin_expediente_sin_pedimento_sin_pedido_y_las_no_retenidas()
    {
        var empresa = Guid.NewGuid();
        var sinExpediente = Guid.NewGuid(); var sinPedimento = Guid.NewGuid(); var largo = Guid.NewGuid(); var noRetenida = Guid.NewGuid();
        using var db = await SembrarAsync(empresa, Guid.NewGuid().ToString(),
            Factura(empresa, sinExpediente), Factura(empresa, sinPedimento), Factura(empresa, largo),
            Factura(empresa, null), Factura(empresa, noRetenida, retener: false));
        var puerto = new Expedientes(p =>
            p == sinExpediente ? new(false)
            : p == sinPedimento ? ConPedimento(null)
            : p == largo ? ConPedimento("PEDIMENTO-CON-MAS-DE-21-CARACTERES")
            : ConPedimento());

        Assert.Empty(await new ExpedientePedimentosReader(db, puerto).LeerPendientesAsync(50, CancellationToken.None));
    }

    [Fact]
    public async Task Worker_con_el_lector_real_libera_y_timbra_la_factura()
    {
        var empresa = Guid.NewGuid(); var pedido = Guid.NewGuid(); var nombre = Guid.NewGuid().ToString();
        var fv = Factura(empresa, pedido);
        using (await SembrarAsync(empresa, nombre, fv)) { }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresa));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(nombre));
        services.AddScoped(sp => ReceptorFiscalTestFactory.Crear(sp.GetRequiredService<FacturacionDbContext>()));
        services.AddSingleton<IPeriodoContablePort>(new FakePeriodoContablePort());
        services.AddSingleton<ICfdiTimbradoPort>(new FakeFiscalApiClient());
        services.AddSingleton<ICfdiRepositorioPort>(new FakeCfdiRepositorioPort());
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<IEmbarqueExportacionReadPort>(new Expedientes(_ => ConPedimento()));
        services.AddScoped<ISalidasPedimentosReader, ExpedientePedimentosReader>();
        services.AddSingleton<IIntegrationEventPublisher>(new FakeIntegrationEventPublisher());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Millet.Facturacion.Application.AssemblyMarker).Assembly));
        services.AddOptions<PedimentoSalidasOptions>();
        var sp = services.BuildServiceProvider();
        var worker = new PedimentoSalidasWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<PedimentoSalidasOptions>>(), NullLogger<PedimentoSalidasWorker>.Instance);

        Assert.Equal(1, await worker.TickAsync(CancellationToken.None));

        using var scope = sp.CreateScope();
        var guardada = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().FacturasVenta
            .Include(f => f.Lineas).SingleAsync(f => f.Id == fv.Id);
        Assert.Equal(EstadoTimbrado.Timbrado, guardada.Estado);
        Assert.Equal(Pedimento, guardada.Lineas.Single().Pedimento);
        Assert.Equal(0, await worker.TickAsync(CancellationToken.None));
    }

    [Fact]
    public void Completar_desde_expediente_solo_en_borrador()
    {
        var fv = Factura(Guid.NewGuid(), Guid.NewGuid());
        var ex = Assert.Throws<BusinessRuleException>(() => fv.CompletarPedimentoDesdeExpediente(Pedimento, new(2026, 11, 3)));
        Assert.Equal("FACTURA_NO_BORRADOR", ex.Code);
    }

    [Theory]
    [InlineData("sin-expediente", "Sin expediente de Comercio exterior ligado: captura el pedimento de importación.")]
    [InlineData("sin-pedimento", "El expediente EXP-25-0007 aún no tiene pedimento capturado.")]
    [InlineData("con-pedimento", "El pedimento 25  16  3807  5000123 del expediente EXP-25-0007 se aplicará en la siguiente revisión automática.")]
    public async Task Detalle_dice_que_falta_para_liberar_la_factura(string caso, string motivo)
    {
        var empresa = Guid.NewGuid(); var fv = Factura(empresa, Guid.NewGuid());
        using var db = await SembrarAsync(empresa, Guid.NewGuid().ToString(), fv);
        var puerto = new Expedientes(_ => caso switch
        {
            "sin-expediente" => new(false),
            "sin-pedimento" => ConPedimento(null),
            _ => ConPedimento(),
        });

        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator(), puerto)
            .Handle(new ComprobanteDetalleQuery(fv.Id), CancellationToken.None);

        Assert.Equal(motivo, detalle.MotivoPedimento);
    }

    [Fact]
    public async Task Detalle_sin_retencion_no_trae_motivo()
    {
        var empresa = Guid.NewGuid(); var fv = Factura(empresa, Guid.NewGuid(), retener: false);
        using var db = await SembrarAsync(empresa, Guid.NewGuid().ToString(), fv);
        var detalle = await new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator(), new Expedientes(_ => ConPedimento()))
            .Handle(new ComprobanteDetalleQuery(fv.Id), CancellationToken.None);
        Assert.Null(detalle.MotivoPedimento);
    }
}
