using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Alertas;

// PT5.7 / CA5.7: la contra entrega que no se liquidó el mismo día llega a CxC como alerta de cartera.
public sealed class RegistrarAlertaContraEntregaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid(), ClienteId = Guid.NewGuid();

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 11, 5, 20, 0, 0, TimeSpan.Zero); }

    private sealed class EmpresaFija : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private static (RegistrarAlertaContraEntregaHandler Handler, CuentasPorCobrarDbContext Db) Crear()
    {
        var db = new CuentasPorCobrarDbContext(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_contra_{Guid.NewGuid()}").Options, new EmpresaFija());
        return (new RegistrarAlertaContraEntregaHandler(db, new Reloj()), db);
    }

    private static RegistrarAlertaContraEntregaCommand Cmd(Guid? cliente = null, string pedido = "P-8500") =>
        new(EmpresaId, cliente ?? ClienteId, "MXN", 8500m, pedido, "HS-20261105-A1B2C", new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Crea_la_alerta_de_cartera_con_pedido_hoja_e_importe()
    {
        var (h, db) = Crear();
        (await h.Handle(Cmd(), default)).Should().BeTrue();

        var a = await db.AlertasCartera.SingleAsync();
        a.Tipo.Should().Be(TipoAlertaCartera.ContraEntregaNoLiquidada);
        a.ClienteId.Should().Be(ClienteId);
        a.Atendida.Should().BeFalse();
        a.Detalle.Should().Contain("P-8500").And.Contain("HS-20261105-A1B2C").And.Contain("8,500.00");
    }

    [Fact]
    public async Task El_mismo_aviso_repetido_no_duplica_la_alerta_y_otro_pedido_si_crea_la_suya()
    {
        var (h, db) = Crear();
        await h.Handle(Cmd(), default);
        (await h.Handle(Cmd(), default)).Should().BeFalse();
        (await h.Handle(Cmd(pedido: "P-9100"), default)).Should().BeTrue();

        (await db.AlertasCartera.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Sin_cliente_no_hay_alerta_ni_reintento_infinito()
    {
        var (h, db) = Crear();
        (await h.Handle(Cmd(Guid.Empty), default)).Should().BeFalse();
        (await db.AlertasCartera.CountAsync()).Should().Be(0);
    }
}
