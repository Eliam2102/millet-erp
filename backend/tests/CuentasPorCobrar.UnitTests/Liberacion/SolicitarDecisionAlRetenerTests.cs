using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

public sealed class SolicitarDecisionAlRetenerTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private sealed class Publicador : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero); }

    private sealed class EmpresaFija : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private static (SolicitarDecisionAlRetenerHandler Handler, CuentasPorCobrarDbContext Db, Publicador Pub) Crear()
    {
        var db = new CuentasPorCobrarDbContext(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_retener_{Guid.NewGuid()}").Options, new EmpresaFija());
        db.ReglasLiberacionSerie.AddRange(ReglaLiberacionSerieConfiguration.SeriesMillet.Select(r => new ReglaLiberacionSerie(r.Id, r.Prefijo, r.Comportamiento)));
        db.SaveChanges();
        var pub = new Publicador();
        return (new SolicitarDecisionAlRetenerHandler(db, pub, new Reloj()), db, pub);
    }

    private static SolicitarDecisionAlRetenerCommand Cmd(string pedido, decimal monto = 1000m) => new(EmpresaId, pedido, ClienteId, "MXN", monto);

    // CA4: serie 5000 se libera sola, firmada por el usuario de sistema, con su evento hacia PT.
    [Fact]
    public async Task Serie_5000_decide_sola_como_sistema_y_publica_Liberado()
    {
        var (h, db, pub) = Crear();
        (await h.Handle(Cmd("5000123"), default)).Should().BeTrue();

        var d = await db.DecisionesLiberacion.SingleAsync();
        (d.Resultado, d.ReglaAplicada, d.DecididoPor).Should().Be((ResultadoLiberacion.Liberado, ReglaAplicadaLiberacion.Serie, SolicitarDecisionAlRetenerHandler.UsuarioSistemaId));
        pub.Publicados.OfType<DecisionLiberacionEmitidaEvent>().Should().ContainSingle(e => e.PedidoRef == "5000123" && e.Resultado == "Liberado");
    }

    // CA5: el mismo Retenido dos veces no repite la decisión automática.
    [Fact]
    public async Task El_mismo_pedido_retenido_dos_veces_decide_una_sola_vez()
    {
        var (h, db, pub) = Crear();
        (await h.Handle(Cmd("5000123"), default)).Should().BeTrue();
        (await h.Handle(Cmd("5000123"), default)).Should().BeFalse();

        (await db.DecisionesLiberacion.CountAsync()).Should().Be(1);
        pub.Publicados.Should().HaveCount(1);
    }

    [Fact]
    public async Task Serie_7800_decide_Retenido_y_sin_datos_de_cliente_o_monto_no_decide()
    {
        var (h, db, _) = Crear();
        (await h.Handle(Cmd("7800456"), default)).Should().BeTrue();
        (await db.DecisionesLiberacion.SingleAsync()).Resultado.Should().Be(ResultadoLiberacion.Retenido);

        (await h.Handle(Cmd("5000999", monto: 0m), default)).Should().BeFalse();
        (await h.Handle(Cmd("5000998") with { ClienteId = Guid.Empty }, default)).Should().BeFalse();
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(1);
    }
}
