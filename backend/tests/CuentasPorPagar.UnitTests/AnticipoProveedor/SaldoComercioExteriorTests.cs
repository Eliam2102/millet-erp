using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.AnticipoProveedor;
public sealed class SaldoComercioExteriorTests
{
    [Fact]
    public async Task Evento_repetido_crea_un_solo_anticipo()
    {
        var reloj = new P8.PeriodoAbiertoStub();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa(), reloj, reloj);
        var handler = new AnticipoAgenteSaldoTransferidoHandler(db, reloj);
        var p = new SaldoAnticipoAgentePayload(Guid.NewGuid(), Guid.NewGuid(), "CE-P14", Guid.NewGuid(), 0,
            Guid.NewGuid(), 7500, new(2026, 10, 3), "Saldo agente");
        await handler.Handle(new(p), default);
        await handler.Handle(new(p), default);
        var a = Assert.Single(await db.AnticiposProveedor.ToListAsync());
        Assert.Equal(7500, a.SaldoAmortizable); Assert.Null(a.UuidCfdi);
        Assert.Equal($"{p.AnticipoAgenteId}:0", a.ReferenciaOrigen);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
