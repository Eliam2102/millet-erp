using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
using Millet.CuentasPorPagar.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.CuentasPorPagar.UnitTests.TableroIntegraciones;

/// <summary>I13.6 · el buzón de CFDI en el tablero de integraciones, a partir de la bitácora de corridas. Datos ficticios.</summary>
public sealed class BuzonCfdiFuenteTableroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Las_corridas_fallidas_seguidas_salen_como_reintento_con_su_numero_de_fallas()
    {
        using var db = NewDb();
        db.BuzonEjecuciones.Add(Corrida(-75, null));
        for (var i = 4; i >= 0; i--) db.BuzonEjecuciones.Add(Corrida(-15 * i, "MAILBOX_LIST_FALLO"));
        await db.SaveChangesAsync();

        var r = await Fuente(db).LeerAsync(Ahora, 50, default);

        (r.Pendientes, r.Reintentando, r.EnError, r.OkUltimas24h).Should().Be((0, 1, 0, 1));
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Estado == EstadoMensajeIntegracion.Reintentando && m.Intentos == 5 && m.UltimoError == "MAILBOX_LIST_FALLO");
        r.PendienteMasAntiguo.Should().BeNull();
    }

    [Fact]
    public async Task Sin_corrida_en_tres_intervalos_el_buzon_activo_se_ve_detenido()
    {
        using var db = NewDb();
        db.BuzonEjecuciones.Add(Corrida(-60, null));
        await db.SaveChangesAsync();

        var r = await Fuente(db).LeerAsync(Ahora, 50, default);

        (r.Pendientes, r.Reintentando).Should().Be((1, 0));
        r.PendienteMasAntiguo.Should().Be(Ahora.AddMinutes(-60 + 15), "la siguiente corrida se esperaba 15 min después");
        r.UltimoExito.Should().Be(Ahora.AddMinutes(-59));
    }

    [Fact]
    public async Task Con_el_buzon_apagado_no_se_marca_detenido()
    {
        using var db = NewDb();
        db.BuzonEjecuciones.Add(Corrida(-600, null));
        await db.SaveChangesAsync();

        var r = await Fuente(db, activo: false).LeerAsync(Ahora, 50, default);

        r.PendienteMasAntiguo.Should().BeNull();
    }

    private static BuzonEjecucion Corrida(int minutos, string? error) =>
        BuzonEjecucion.Registrar(Ahora.AddMinutes(minutos), Ahora.AddMinutes(minutos + 1), 3, error is null ? 2 : 0, 1, 0, error);

    private static BuzonCfdiFuenteTablero Fuente(CuentasPorPagarDbContext db, bool activo = true) => new(db,
        new Monitor<WorkerSchedulingOptions>(new WorkerSchedulingOptions { MailboxIntervalSeconds = 900 }),
        new Monitor<MailboxOptions>(new MailboxOptions { SimuladorDirectorio = activo ? "/tmp/buzon-simulado" : string.Empty }));

    private static CuentasPorPagarDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Empresa(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
