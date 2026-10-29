using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Ingesta;

public sealed class PlantaPinturaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static ClienteFiscalLectura Cliente() =>
        new(Guid.NewGuid(), "AAA010101AAA", "Cliente PP", "601", "97000", "G03", "01", "PUE", "MXN", false);

    private static ProductoFiscalLectura Producto() =>
        new(Guid.NewGuid(), "Pintura", "01010101", "H87", "02", 0.16m, null, null, "PlantaPintura",
            EsServicioRecubrimiento: true);

    private static PedidoPlantaPintura Pedido(long version = 1) => new(
        "PP-1", Guid.NewGuid(), "CLI-1", "Cliente PP", CanalesVentaConocidos.PlantaPintura,
        ComportamientoFiscal.MostradorInmediato, "MXN", null, null, "comentario", version, "ABIERTO",
        [new LineaPedidoPlantaPintura("PRD-1", "Pintura", "01010101", "H87", 2m, 100m, 0m, false)],
        "{\"pp\":\"PP-1\"}");

    private static ImportarPedidoPlantaPinturaHandler Handler(
        FacturacionDbContext db, ClienteFiscalLectura? cliente, ProductoFiscalLectura? producto) =>
        new(db, new FakeClientesReadPort(cliente), new FakeProductosReadPort(producto), new FakeClock(Ahora));

    [Fact]
    public void ImportarDesdePlantaPintura_tiene_origen_PlantaPintura()
    {
        var p = PedidoFacturable.ImportarDesdePlantaPintura(Guid.NewGuid(), "PP-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", CanalesVentaConocidos.PlantaPintura, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "ABIERTO");
        p.Origen.Should().Be(OrigenPedido.PlantaPintura);
        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    [Fact]
    public async Task Importar_con_master_existente_crea_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, Cliente(), Producto())
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        var pedido = await db.PedidosFacturables.SingleAsync();
        pedido.Origen.Should().Be(OrigenPedido.PlantaPintura);
        await db.IngestaControles.SingleAsync();
        await db.PedidosFacturablesSnapshot.SingleAsync();
    }

    [Fact]
    public async Task Importar_sin_cliente_genera_excepcion_sin_autoprovision()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, cliente: null, producto: Producto())
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await db.PedidosFacturables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Importar_sin_articulo_genera_excepcion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, cliente: Cliente(), producto: null)
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ArticuloNoExiste);
    }

    [Fact]
    public async Task Importar_version_repetida_es_idempotente()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 1)), CancellationToken.None);

        var resp = await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 1)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reenvio_con_version_mayor_tras_exito_no_crea_segundo_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var primera = await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 1)), CancellationToken.None);

        var resp = await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 2)), CancellationToken.None);

        resp.PedidoFacturableId.Should().Be(primera.PedidoFacturableId);
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Servicio_sin_marca_de_recubrimiento_se_rechaza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, Cliente(), Producto() with { EsServicioRecubrimiento = false })
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ServicioNoMarcado);
        (await db.PedidosFacturables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Iva_que_no_cuadra_con_el_producto_se_rechaza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var p = Pedido() with { Lineas = [Pedido().Lineas[0] with { TasaIva = 0.08m }] };

        var resp = await Handler(db, Cliente(), Producto())
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, p), CancellationToken.None);

        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.IvaNoCuadra);
        resp.Detalle.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Exito_resuelve_la_excepcion_previa_del_folio()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Handler(db, Cliente(), Producto() with { EsServicioRecubrimiento = false })
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        (await db.ExcepcionesImportacion.SingleAsync()).Resuelto.Should().BeTrue();
    }

    [Fact]
    public void Worker_intervalo_por_defecto_es_10_segundos() =>
        new PlantaPinturaImportOptions().IntervalSeconds.Should().Be(10);

    private sealed class ReaderAcuses(PedidoPlantaPintura pedido) : IPlantaPinturaPedidosReader
    {
        public readonly List<(string Folio, ResultadoAcusePlantaPintura Res, Guid? PedidoId, string? Error)> Acuses = [];
        public Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PedidoPlantaPintura>>([pedido]);
        public Task MarcarResultadoAsync(string numeroPedido, ResultadoAcusePlantaPintura resultado, Guid? pedidoId, string? detalleError,
            DateTimeOffset fecha, CancellationToken ct)
        {
            Acuses.Add((numeroPedido, resultado, pedidoId, detalleError));
            return Task.CompletedTask;
        }
    }

    private sealed class SenderFijo(Func<ImportarPedidoPlantaPinturaResponse> f) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) =>
            Task.FromResult((TResponse)(object)f());
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class MonitorFijo(PlantaPinturaImportOptions v) : IOptionsMonitor<PlantaPinturaImportOptions>
    {
        public PlantaPinturaImportOptions CurrentValue => v;
        public PlantaPinturaImportOptions Get(string? name) => v;
        public IDisposable? OnChange(Action<PlantaPinturaImportOptions, string?> listener) => null;
    }

    private static async Task<ReaderAcuses> TickConAsync(Func<ImportarPedidoPlantaPinturaResponse> respuesta)
    {
        var reader = new ReaderAcuses(Pedido());
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(null));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IPlantaPinturaPedidosReader>(reader);
        services.AddSingleton<ISender>(new SenderFijo(respuesta));
        var worker = new PlantaPinturaImportWorker(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new MonitorFijo(new PlantaPinturaImportOptions { EmpresaId = Guid.NewGuid() }),
            NullLogger<PlantaPinturaImportWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);
        return reader;
    }

    [Fact]
    public async Task Worker_acusa_creado_con_el_pedido_ERP_en_exito()
    {
        var id = Guid.NewGuid();
        var reader = await TickConAsync(() => new(ResultadoSolicitudAw.Aplicada, id, null));

        reader.Acuses.Should().ContainSingle().Which.Should().Be(("PP-1", ResultadoAcusePlantaPintura.Creado, (Guid?)id, (string?)null));
    }

    [Fact]
    public async Task Worker_acusa_error_con_el_motivo_si_la_regla_rechaza()
    {
        var reader = await TickConAsync(() => new(ResultadoSolicitudAw.Rechazada, null, MotivoExcepcion.IvaNoCuadra, "IVA no cuadra"));

        reader.Acuses.Should().ContainSingle().Which.Should().Be(("PP-1", ResultadoAcusePlantaPintura.Error, (Guid?)null, (string?)"IVA no cuadra"));
    }

    [Fact]
    public async Task Worker_acusa_error_si_el_envio_lanza_BusinessRuleException()
    {
        var reader = await TickConAsync(() => throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("X", "regla rota"));

        reader.Acuses.Should().ContainSingle().Which.Res.Should().Be(ResultadoAcusePlantaPintura.Error);
        reader.Acuses[0].Error.Should().Contain("regla rota");
    }
}
