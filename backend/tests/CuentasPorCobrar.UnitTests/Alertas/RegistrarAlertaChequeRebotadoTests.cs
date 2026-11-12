using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Alertas;

// M-372: el cheque rebotado que avisa Tesorería llega a CxC como alerta de cartera, una sola vez por evento.
public sealed class RegistrarAlertaChequeRebotadoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid(), ClienteId = Guid.NewGuid();

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero); }

    private sealed class EmpresaFija : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    [Fact]
    public async Task Crea_una_alerta_por_evento_con_cheque_banco_e_importe()
    {
        var db = new CuentasPorCobrarDbContext(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_cheque_{Guid.NewGuid()}").Options, new EmpresaFija());
        var h = new RegistrarAlertaChequeRebotadoHandler(db, new Reloj());
        var evento = Guid.NewGuid();
        var cmd = new RegistrarAlertaChequeRebotadoCommand(evento, new ChequeRebotadoPayload(EmpresaId, DateTimeOffset.UtcNow,
            Guid.NewGuid(), ClienteId, "Banamex", "001234", 18_500m, "MXN", "Fondos insuficientes", true));

        await h.Handle(cmd, default);
        await h.Handle(cmd, default);

        var a = await db.AlertasCartera.SingleAsync();
        (a.Tipo, a.ClienteId, a.Moneda).Should().Be((TipoAlertaCartera.ChequeRebotado, ClienteId, "MXN"));
        a.Detalle.Should().Contain("001234").And.Contain("Banamex").And.Contain("18,500.00").And.Contain("Fondos insuficientes")
            .And.Contain("revirtió el ingreso");
    }
}
