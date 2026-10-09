using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests;

/// <summary>PostgreSQL desechable del script aislado; no crea registros en catálogos compartidos.</summary>
public sealed class P3PagoYRevaluacionTests
{
    private static string Conexion => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("Ejecuta tools/validate-integration-isolated.sh.");

    [Fact]
    public async Task Pago_manual_limita_a_ocho_y_libera_dos_con_outbox_atomico()
    {
        var contexto = new Contexto();
        var buffer = new InMemoryIntegrationEventBuffer();
        await using var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseNpgsql(Conexion).UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxSaveChangesInterceptor(buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance))
            .Options, contexto);
        var cuenta = new CuentaBancaria(contexto.Current!.Value, "Banco ficticio P3", "0000000001", null, "MXN");
        var pasivo = new PasivoPendientePago(contexto.Current.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            200, 200, "MXN", null, new(2026, 10, 12), null, "P3-FICTICIA", contexto.UtcNow, "PUE");
        db.CuentasBancarias.Add(cuenta);
        db.PasivosPendientesPago.Add(pasivo);
        await db.SaveChangesAsync();
        try
        {
            var limite = new Limite();
            var handler = new RegistrarPagoProveedorHandler(db, contexto, contexto, new Periodo(), new Proveedor(),
                limite, new Publicador(buffer), contexto);
            var excede = () => handler.Handle(new(cuenta.Id, new(2026, 10, 9), [new(pasivo.FacturaProveedorId, 200)]), default);
            await excede.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_EXCEDE_ELEGIBLE");
            (await db.MovimientosBancarios.CountAsync()).Should().Be(0);
            await handler.Handle(new(cuenta.Id, new(2026, 10, 9), [new(pasivo.FacturaProveedorId, 160)]), default);
            var otro = () => handler.Handle(new(cuenta.Id, new(2026, 10, 9), [new(pasivo.FacturaProveedorId, 1)]), default);
            await otro.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAGO_EXCEDE_ELEGIBLE");
            limite.Valor = 200;
            await handler.Handle(new(cuenta.Id, new(2026, 10, 9), [new(pasivo.FacturaProveedorId, 40)]), default);
            (await db.AplicacionesPagoProveedor.Where(a => a.FacturaProveedorId == pasivo.FacturaProveedorId)
                .SumAsync(a => a.ImporteAplicado)).Should().Be(200);
            (await db.Set<IntegrationEventOutboxEntry>().CountAsync(e => e.IntegrationEmpresaId == contexto.Current)).Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await db.AplicacionesPagoProveedor.Where(a => a.FacturaProveedorId == pasivo.FacturaProveedorId).ExecuteDeleteAsync();
            await db.MovimientosBancarios.ExecuteDeleteAsync();
            await db.PasivosPendientesPago.ExecuteDeleteAsync();
            await db.CuentasBancarias.ExecuteDeleteAsync();
            await db.Set<IntegrationEventOutboxEntry>().Where(e => e.IntegrationEmpresaId == contexto.Current).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Evento_antiguo_de_precio_se_descarta_dos_veces_sin_movimientos_ni_valoracion()
    {
        var contexto = new Contexto();
        await using var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseNpgsql(Conexion).UseSnakeCaseNamingConvention().Options, contexto);
        var eventoId = Guid.NewGuid();
        var handler = new DiferenciaPrecioFacturaDetectadaHandler(db, NullLogger<DiferenciaPrecioFacturaDetectadaHandler>.Instance);
        var command = new DiferenciaPrecioFacturaDetectadaCommand(eventoId,
            new(contexto.Current!.Value, contexto.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, 50, 20, 30, 300));
        try
        {
            await handler.Handle(command, default);
            await handler.Handle(command, default);
            (await db.Movimientos.CountAsync()).Should().Be(0);
            (await db.Set<IntegrationEventOutboxEntry>().CountAsync(e => e.IntegrationEmpresaId == contexto.Current)).Should().Be(0);
            (await db.Set<Millet.Almacen.Domain.Idempotencia.EventoProcesado>().CountAsync(e => e.EventoId == eventoId)).Should().Be(1);
        }
        finally
        {
            await db.Set<Millet.Almacen.Domain.Idempotencia.EventoProcesado>().Where(e => e.EventoId == eventoId).ExecuteDeleteAsync();
        }
    }

    private sealed class Contexto : ICurrentEmpresaContext, ICurrentUserContext, IClock
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "P3 ficticio";
        public DateTimeOffset UtcNow => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Limite : IElegibleFacturaReadPort
    {
        public decimal Valor { get; set; } = 160;
        public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(Valor);
    }
    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Proveedor : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(null);
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }
    private sealed class Publicador(InMemoryIntegrationEventBuffer buffer) : IIntegrationEventPublisher
    {
        public Task PublishAsync(object e, CancellationToken ct) { buffer.Enqueue((IntegrationEvent)e); return Task.CompletedTask; }
    }
}
