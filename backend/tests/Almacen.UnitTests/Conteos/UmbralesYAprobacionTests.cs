using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.UnitTests.Conteos;

public sealed class UmbralesYAprobacionTests
{
    [Fact]
    public void Modelo_relacional_coincide_con_la_migracion_de_la_foto()
    {
        using var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only").UseSnakeCaseNamingConvention().Options,
            new Empresa());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(800, 1, true)]
    [InlineData(1000, 1, true)]
    [InlineData(1000.01, 1, false)]
    [InlineData(5000, 1, false)]
    [InlineData(5000, 2, true)]
    [InlineData(10000, 2, true)]
    [InlineData(12000, 2, false)]
    [InlineData(12000, 3, true)]
    [InlineData(-12000, 1, false)]
    [InlineData(-12000, 3, true)]
    [InlineData(800, 3, true)]
    [InlineData(800, 0, false)]
    public async Task Aprobacion_por_monto_exige_el_nivel_y_publica_solo_nivel3(
        decimal monto, int permiso, bool autorizado)
    {
        await using var db = NuevaDb();
        var c = CrearConteo(monto, new(5, 1000, 1000, 10000));
        db.Conteos.Add(c);
        await db.SaveChangesAsync();
        var efectos = new Efectos(db, c.Id);
        var handler = new AprobarConteoHandler(db, new Usuario(), new Permisos(permiso), efectos, efectos);
        if (!autorizado)
        {
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(c.Id), default));
            Assert.Contains($"Nivel {c.ObtenerUmbrales().NivelRequerido(monto)}", ex.Message);
            Assert.Equal(EstadoConteo.EnConciliacion, c.Estado);
            Assert.Empty(efectos.Eventos);
            Assert.Empty(efectos.Avisos);
            return;
        }
        await handler.Handle(new(c.Id), default);
        Assert.Equal(EstadoConteo.Aprobado, c.Estado);
        if (Math.Abs(monto) > 10000)
        {
            var evento = Assert.IsType<ConteoAjusteNivel3AprobadoEvent>(Assert.Single(efectos.Eventos));
            Assert.Equal(monto, evento.MontoNetoMxn);
            Assert.Equal(c.Id, evento.ConteoId);
            Assert.Equal(c.AprobadorId, evento.AprobadorId);
            Assert.Equal(c.FechaAprobacion, evento.OcurridoEn);
            Assert.Single(efectos.Avisos);
        }
        else
        {
            Assert.Empty(efectos.Eventos);
            Assert.Empty(efectos.Avisos);
        }
    }

    [Fact]
    public async Task Nuevo_limite_permite_supervisor_y_el_conteo_anterior_conserva_la_foto()
    {
        await using var db = NuevaDb();
        var anterior = CrearConteo(12000, new(5, 1000, 1000, 10000));
        var nuevo = CrearConteo(12000, new(8, 2000, 1000, 15000));
        db.AddRange(anterior, nuevo);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var efectos = new Efectos(db, nuevo.Id);
        var handler = new AprobarConteoHandler(db, new Usuario(), new Permisos(2), efectos, efectos);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(anterior.Id), default));
        await handler.Handle(new(nuevo.Id), default);
        var foto = await db.Conteos.SingleAsync(c => c.Id == anterior.Id);
        Assert.Equal(new ConteoUmbrales(5, 1000, 1000, 10000), foto.ObtenerUmbrales());
        Assert.Empty(efectos.Eventos);
    }

    [Theory]
    [InlineData(-1, 1000, 1000, 10000)]
    [InlineData(5, -1, 1000, 10000)]
    [InlineData(5, 1000, -1, 10000)]
    [InlineData(5, 1000, 1000, -1)]
    [InlineData(5, 1000, 10000, 10000)]
    [InlineData(5, 1000, 15000, 10000)]
    public void Rechaza_politicas_invalidas(decimal pct, decimal valor, decimal n1, decimal n2) =>
        Assert.Throws<BusinessRuleException>(() => new ConteoUmbrales(pct, valor, n1, n2));

    [Fact]
    public async Task Recuento_usa_ambos_umbrales_de_la_foto_y_respeta_el_limite_exacto()
    {
        await using var db = NuevaDb();
        var c = new ConteoInventario(Guid.NewGuid(), Guid.NewGuid(), TipoConteo.Rotativo,
            new DateOnly(2026, 10, 8), Guid.NewGuid());
        foreach (var (real, costo) in new[] { (108m, 250m), (108.01m, 0m), (104m, 501m) })
        {
            var l = new LineaConteo(Guid.NewGuid(), c.Id, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), 100, costo);
            l.Capturar(real, Guid.NewGuid());
            c.AgregarLinea(l);
        }
        c.Iniciar(new(8, 2000, 1000, 15000));
        db.Conteos.Add(c);
        await db.SaveChangesAsync();
        Assert.Equal(2, await new EvaluarVariacionesConteoHandler(db).Handle(new(c.Id), default));
        Assert.False(c.Lineas.First().RequiereRecuento);
        Assert.Equal(0, await new EvaluarVariacionesConteoHandler(db).Handle(new(c.Id), default));
    }

    [Fact]
    public void Neto_compensa_signos_y_redondea_por_linea_como_aplicar()
    {
        var c = CrearConteo(0, new(5, 1000, 1000, 10000));
        // Una política no suma valores absolutos: 12000 - 11500 = 500.
        var mixto = new ConteoInventario(Guid.NewGuid(), Guid.NewGuid(), TipoConteo.Rotativo,
            new DateOnly(2026, 10, 8), Guid.NewGuid());
        foreach (var (real, costo) in new[] { (220m, 100m), (0m, 115m) })
        {
            var l = new LineaConteo(Guid.NewGuid(), mixto.Id, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), 100, costo);
            l.Capturar(real, Guid.NewGuid());
            mixto.AgregarLinea(l);
        }
        mixto.Iniciar(c.ObtenerUmbrales());
        Assert.Equal(500, mixto.CalcularMontoNeto());
        Assert.Equal(1, mixto.ObtenerUmbrales().NivelRequerido(mixto.CalcularMontoNeto()));
        Assert.Throws<BusinessRuleException>(() => mixto.Iniciar(new(1, 1, 1, 2)));
        Assert.Equal(10000, mixto.UmbralNivel2Maximo);
    }

    private static ConteoInventario CrearConteo(decimal monto, ConteoUmbrales umbrales)
    {
        var c = new ConteoInventario(Guid.NewGuid(), Guid.NewGuid(), TipoConteo.Rotativo,
            new DateOnly(2026, 10, 8), Guid.NewGuid());
        var l = new LineaConteo(Guid.NewGuid(), c.Id, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 20000, 1);
        c.AgregarLinea(l);
        c.Iniciar(umbrales);
        l.Capturar(20000 + monto, Guid.NewGuid());
        c.EnviarAConciliacion();
        return c;
    }

    private static AlmacenDbContext NuevaDb() => new(
        new DbContextOptionsBuilder<AlmacenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Empresa());

    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Aprobador de prueba";
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Permisos(int nivel) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permiso == $"almacen.inventarios.aprobar-nivel{nivel}");
    }
    private sealed class Efectos(AlmacenDbContext db, Guid conteoId) : IIntegrationEventPublisher, INotificacionService
    {
        public List<IntegrationEvent> Eventos { get; } = [];
        public List<AvisoAjusteNivel3> Avisos { get; } = [];
        public async Task PublishAsync(object evento, CancellationToken cancellationToken)
        {
            // Antes de SaveChanges, la base aún no debe contener la aprobación.
            Assert.Equal(EstadoConteo.EnConciliacion,
                await db.Conteos.AsNoTracking().Where(c => c.Id == conteoId).Select(c => c.Estado).SingleAsync(cancellationToken));
            Eventos.Add(Assert.IsAssignableFrom<IntegrationEvent>(evento));
        }
        public async Task NotificarAjusteNivel3AprobadoAsync(AvisoAjusteNivel3 aviso, CancellationToken cancellationToken)
        {
            Assert.Equal(EstadoConteo.Aprobado,
                await db.Conteos.AsNoTracking().Where(c => c.Id == conteoId).Select(c => c.Estado).SingleAsync(cancellationToken));
            Avisos.Add(aviso);
        }
        public Task NotificarValeSinRegularizarAsync(Guid id, string folio, Guid? destinatario,
            DateTimeOffset fecha, int dia, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
