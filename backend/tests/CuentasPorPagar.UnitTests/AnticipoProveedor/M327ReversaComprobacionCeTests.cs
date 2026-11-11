using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.AnticipoProveedor;

/// <summary>
/// M-327: la reversa autorizada de la comprobación deja todo lo anticipado como saldo a favor del agente (anticipo amortizable), una
/// sola vez, y publica la póliza de reversa. El saldo compensa la factura siguiente y nunca queda negativo.
/// </summary>
public sealed class M327ReversaComprobacionCeTests
{
    private static ReversaComprobacionCePayload Payload() => new(Guid.NewGuid(), Guid.NewGuid(), "CE-IMP-2026-0142", Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50000, 42000, new(2026, 11, 10));

    [Fact]
    public async Task Reversa_repetida_crea_un_solo_saldo_a_favor_de_50000_y_una_sola_poliza()
    {
        var reloj = new P8.PeriodoAbiertoStub();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa(), reloj, reloj);
        var eventos = new Eventos();
        var handler = new AnticipoComprobacionRevertidaHandler(db, reloj, eventos);
        var p = Payload();

        await handler.Handle(new(p), default);
        await handler.Handle(new(p), default);

        var saldo = Assert.Single(await db.AnticiposProveedor.ToListAsync());
        Assert.Equal(50000, saldo.SaldoAmortizable); Assert.Equal(p.AgenteId, saldo.ProveedorId);
        Assert.Equal(AnticipoComprobacionRevertidaHandler.Referencia(p.AnticipoAgenteId, p.SolicitudId), saldo.ReferenciaOrigen);
        var poliza = Assert.IsType<AnticipoAgenteComprobacionRevertidaIntegrationEvent>(Assert.Single(eventos.Publicados));
        Assert.Equal((50000m, 42000m, p.SolicitudId, saldo.Id), (poliza.SaldoAFavorMxn, poliza.GastosLiberadosMxn, poliza.SolicitudId, poliza.AnticipoProveedorId));

        // CA3: compensa la factura siguiente y nunca queda negativo.
        saldo.Amortizar(42000, reloj.UtcNow);
        Assert.Equal(8000, saldo.SaldoAmortizable);
        Assert.Throws<BusinessRuleException>(() => saldo.Amortizar(8000.01m, reloj.UtcNow));
        Assert.Equal(8000, saldo.SaldoAmortizable);
    }

    [Fact]
    public async Task Reversa_sin_saldo_positivo_se_rechaza()
    {
        var reloj = new P8.PeriodoAbiertoStub();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa(), reloj, reloj);
        var handler = new AnticipoComprobacionRevertidaHandler(db, reloj);
        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(Payload() with { SaldoAFavorMxn = 0 }), default));
        Assert.Empty(await db.AnticiposProveedor.ToListAsync());
    }

    private sealed class Eventos : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
