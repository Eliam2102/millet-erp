using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.UnitTests.Ingesta;

/// <summary>
/// I13.6 · el resultado de los pedidos de vuelta a A+W en el tablero de integraciones: con A+W caída el aviso se
/// reintenta, al agotar intentos queda en error y se reintenta a mano; el que ya se entregó cuenta como procesado.
/// </summary>
public sealed class AwWriteBackFuenteTableroTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Clasifica_el_write_back_y_el_reintento_manual_lo_regresa_a_la_cola()
    {
        using var db = NewDb();
        var pendiente = Control("10432201", fallas: 0);
        var reintentando = Control("10432202", fallas: 2);
        var enError = Control("10432203", fallas: 3);
        var entregado = Control("10432204", fallas: 1);
        entregado.ConfirmarWriteBack(Ahora.AddMinutes(-10));
        var sinWriteBack = IngestaControl.Crear(Empresa, OrigenPedido.Aw, "10432205", "h", 1, EstadoIngesta.Importado, null, Ahora);
        db.IngestaControles.AddRange(pendiente, reintentando, enError, entregado, sinWriteBack);
        await db.SaveChangesAsync();
        var fuente = new AwWriteBackFuenteTablero(db, new Monitor(new AwWriteBackOptions { MaxIntentos = 3 }));

        var r = await fuente.LeerAsync(Ahora, 10, default);

        (r.Pendientes, r.Reintentando, r.EnError, r.OkUltimas24h).Should().Be((1, 1, 1, 1));
        r.Mensajes.Should().HaveCount(4, "el pedido sin write-back no es de esta integración");
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Referencia == "Pedido 10432203" && m.Estado == EstadoMensajeIntegracion.Error && m.UltimoError == "HC caída");

        (await fuente.ReintentarAsync(enError.Id.ToString(), default)).Should().BeTrue();
        (await fuente.ReintentarAsync(entregado.Id.ToString(), default)).Should().BeFalse();
        (await db.IngestaControles.AsNoTracking().SingleAsync(c => c.Id == enError.Id)).WriteBackIntentos.Should().Be(0);
        (await fuente.LeerAsync(Ahora, 10, default)).EnError.Should().Be(0);
    }

    private static IngestaControl Control(string pedido, int fallas)
    {
        var c = IngestaControl.Crear(Empresa, OrigenPedido.Aw, pedido, "h", 1, EstadoIngesta.Importado, Guid.NewGuid(), Ahora.AddHours(-1));
        c.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora.AddHours(-1));
        for (var i = 0; i < fallas; i++) c.RegistrarFalloWriteBack("HC caída", Ahora.AddMinutes(-30 + i));
        return c;
    }

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private sealed class Monitor(AwWriteBackOptions valor) : IOptionsMonitor<AwWriteBackOptions>
    {
        public AwWriteBackOptions CurrentValue => valor;
        public AwWriteBackOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<AwWriteBackOptions, string?> listener) => null;
    }
}
