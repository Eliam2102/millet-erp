using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
namespace Millet.CuentasPorPagar.UnitTests.AnticipoProveedor;
public sealed class P19SolicitudAgenteTests
{
    [Fact]
    public async Task Solicitud_repetida_crea_un_anticipo_sin_saldo_amortizable_y_un_pasivo_ce()
    {
        var reloj = new P8.PeriodoAbiertoStub(); var eventos = new Eventos();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa(), reloj, reloj);
        var p = new SolicitudAnticipoAgentePayload(Guid.NewGuid(), Guid.NewGuid(), "CE-P19", Guid.NewGuid(), null, 1,
            Guid.NewGuid(), Guid.NewGuid(), 50000, new(2026, 10, 1), Guid.NewGuid());
        var h = new AnticipoAgenteSolicitadoHandler(db, eventos, reloj);
        await h.Handle(new(p), default); await h.Handle(new(p), default);
        var a = Assert.Single(await db.AnticiposProveedor.ToListAsync());
        Assert.Equal(p.AnticipoAgenteId, a.Id); Assert.Equal(EstadoAnticipo.Solicitado, a.Estado); Assert.Equal(0, a.SaldoAmortizable);
        var pasivo = Assert.IsType<Millet.CuentasPorPagar.Application.Integration.PasivoAutorizadoParaPagoIntegrationEvent>(Assert.Single(eventos.Publicados));
        Assert.Equal(1, pasivo.TipoOperacionCe); Assert.Equal(p.SucursalId, pasivo.SucursalId); Assert.Equal("AnticipoAgente", pasivo.OrigenTipo);
        a.ConfirmarPagoComercioExterior(50000); Assert.Equal(0, a.SaldoAmortizable);
        Assert.Equal(EstadoAnticipo.PagadoComercioExterior, a.Estado);
        Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => a.Cancelar("Cancelar", reloj.UtcNow));
        Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => a.Amortizar(1, reloj.UtcNow));
    }
    private sealed class Eventos : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null; public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada(); private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
