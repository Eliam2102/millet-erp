using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Cotizaciones;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Application;

public sealed class RegistrarCotizacionHandlerTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);

    [Fact]
    public async Task Condiciones_de_pago_solo_admiten_catalogo_activo()
    {
        await using var f = Fixture.Crear();
        var pago = new Millet.Catalogos.Domain.CondicionesPago(Guid.NewGuid(), "DEMO", "DEMO ficticio", 30);
        f.Compartido.CondicionesPago.Add(pago);
        await f.Compartido.SaveChangesAsync();
        await PartidasCotizacion.ValidarPagoAsync(f.Compartido, null, default);
        await PartidasCotizacion.ValidarPagoAsync(f.Compartido, pago.Id, default);
        pago.CambiarEstatus(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo);
        await f.Compartido.SaveChangesAsync();
        foreach (var id in new[] { pago.Id, Guid.NewGuid(), Guid.Empty })
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PartidasCotizacion.ValidarPagoAsync(f.Compartido, id, default));
            Assert.Equal("COT_PAGO_INVALIDO", error.Code);
        }
    }

    [Fact]
    public async Task RqEnBorrador_NoSeCotiza()
    {
        await using var f = Fixture.Crear(autorizar: false);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Handler.Handle(f.Comando([f.Rq.Lineas.First().Id]), default));
        Assert.Equal("COT_RQ_NO_COTIZABLE", ex.Code);
    }

    [Fact]
    public async Task PartidaInventada_EsAjena()
    {
        await using var f = Fixture.Crear();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Handler.Handle(f.Comando([Guid.NewGuid()]), default));
        Assert.Equal("COT_PARTIDA_AJENA", ex.Code);
    }

    [Fact]
    public async Task CotizarSoloUnaDeDosPartidas_EsIncompleta()
    {
        await using var f = Fixture.Crear();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Handler.Handle(f.Comando([f.Rq.Lineas.First().Id]), default));
        Assert.Equal("COT_PARTIDAS_INCOMPLETAS", ex.Code);
    }


    [Fact]
    public async Task Eliminar_ConOtraElegida_ComparativoCerrado()
    {
        await using var f = Fixture.Crear();
        var elegida = f.NuevaCotizacion();
        var otra = f.NuevaCotizacion();
        elegida.Elegir(Guid.NewGuid(), "Menor precio y entrega", Guid.NewGuid(), DateTimeOffset.UtcNow, Hoy);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new EliminarCotizacionHandler(f.Db).Handle(new EliminarCotizacionCommand(otra.Id), default));
        Assert.Equal("COT_COMPARATIVO_CERRADO", ex.Code);
        Assert.True(await f.Db.Cotizaciones.AnyAsync(c => c.Id == otra.Id));
    }

    [Fact]
    public async Task Eliminar_SinEleccion_Borra()
    {
        await using var f = Fixture.Crear();
        var cot = f.NuevaCotizacion();
        await f.Db.SaveChangesAsync();

        await new EliminarCotizacionHandler(f.Db).Handle(new EliminarCotizacionCommand(cot.Id), default);

        Assert.False(await f.Db.Cotizaciones.AnyAsync(c => c.Id == cot.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ComprasDbContext Db { get; init; }
        public required CompartidoDbContext Compartido { get; init; }
        public required Requisicion Rq { get; init; }
        public required RegistrarCotizacionHandler Handler { get; init; }

        public static Fixture Crear(bool autorizar = true)
        {
            var empresa = Guid.NewGuid();
            var ctx = new Contexto(empresa);
            var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
                .UseInMemoryDatabase($"COT-{Guid.NewGuid()}").Options, ctx);
            var compartido = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
                .UseInMemoryDatabase($"COT-C-{Guid.NewGuid()}").Options, ctx);
            var rq = new Requisicion(Guid.NewGuid(), empresa, Folio.Parse("MID2026-000950"), 2026,
                Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Prioridad.Normal, DateTimeOffset.UtcNow, null);
            var l1 = rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", Money.Mxn(100), centroCostoId: Guid.NewGuid());
            var l2 = rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 5, "PZA", Money.Mxn(100), centroCostoId: Guid.NewGuid());
            if (autorizar)
            {
                rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
                rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
                rq.RegistrarCubrimiento([new CubrimientoLinea(l1.Id, 0, 10), new CubrimientoLinea(l2.Id, 0, 5)], DateTimeOffset.UtcNow);
            }
            db.Add(rq);
            db.SaveChanges();
            return new Fixture
            {
                Db = db, Compartido = compartido, Rq = rq,
                Handler = new RegistrarCotizacionHandler(db, compartido, ctx, ctx, ctx),
            };
        }

        public Millet.Compras.Domain.Cotizaciones.Cotizacion NuevaCotizacion()
        {
            var c = Millet.Compras.Domain.Cotizaciones.Cotizacion.Registrar(Guid.NewGuid(), Rq.EmpresaId, Rq.Id, Rq.SucursalId,
                Guid.NewGuid(), "F-1", "MXN", Hoy, Hoy.AddDays(5), null,
                Rq.Lineas.Select(l => new Millet.Compras.Domain.Cotizaciones.LineaCotizacionDatos(l.Id, "PZA", "PZA", 1m, 10m, 3)).ToList(),
                Guid.NewGuid(), DateTimeOffset.UtcNow, Hoy);
            Db.Cotizaciones.Add(c);
            return c;
        }

        public RegistrarCotizacionCommand Comando(Guid[] lineas) => new(Rq.Id, Guid.NewGuid(), "F-1", "MXN", Hoy, Hoy.AddDays(5), null,
            lineas.Select(id => new LineaCotizacionRequest(id, "PZA", 1m, 10m, 3)).ToList());

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Compartido.DisposeAsync();
        }
    }

    private sealed class Contexto(Guid empresa) : ICurrentEmpresaContext, ICurrentUserContext, IClock
    {
        public Guid? Current => empresa;
        public bool IsBypassed => false;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "COT";
        public DateTimeOffset UtcNow => new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
