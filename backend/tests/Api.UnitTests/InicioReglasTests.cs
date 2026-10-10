using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;
using ConsultaCompras = Millet.Compras.Application.Inicio.ResumenInicioQuery;

namespace Millet.Api.UnitTests;

public sealed class InicioReglasTests
{
    [Theory]
    [InlineData(PeriodoInicio.Hoy, 10)]
    [InlineData(PeriodoInicio.Semana, 5)]
    [InlineData(PeriodoInicio.Mes, 1)]
    public void Periodo_respeta_calendario_de_Merida(PeriodoInicio periodo, int dia)
    {
        var hoy = FechasInicio.Hoy(new Reloj());
        Assert.Equal(new DateOnly(2026, 10, 10), hoy);
        Assert.Equal(new DateOnly(2026, 10, dia), FechasInicio.Desde(periodo, hoy));
    }
    [Fact]
    public void Rechaza_periodo_desconocido_sucursal_vacia_y_pieza_desconocida()
    {
        var validator = new Millet.Compras.Application.Inicio.ResumenInicioValidator();
        Assert.False(validator.Validate(new ConsultaCompras("circuito") { Periodo = (PeriodoInicio)99 }).IsValid);
        Assert.False(validator.Validate(new ConsultaCompras("circuito", Guid.Empty)).IsValid);
        Assert.False(validator.Validate(new ConsultaCompras("bancos")).IsValid);
        Assert.True(validator.Validate(new ConsultaCompras("circuito", Guid.NewGuid()) { Periodo = PeriodoInicio.Semana }).IsValid);
    }
    [Fact]
    public async Task Requisiciones_propias_no_incluyen_otro_capturista_ni_sucursal_ajena()
    {
        var empresa = new Empresa(); var usuario = new Usuario();
        using var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var propia = Guid.NewGuid(); var ajena = Guid.NewGuid();
        Requisicion Crear(Guid sucursal, Guid capturista, int numero) => new(Guid.NewGuid(), empresa.Current!.Value,
            Folio.Parse($"INI2026-{numero:000000}"), 2026, Clasificacion.OrdenCompra, sucursal,
            Guid.NewGuid(), null, capturista, capturista, Prioridad.Normal, new Reloj().UtcNow);
        db.Requisiciones.AddRange(Crear(propia, usuario.UserId!.Value, 1), Crear(ajena, usuario.UserId.Value, 2),
            Crear(propia, Guid.NewGuid(), 3));
        await db.SaveChangesAsync();
        using var servicios = new ServiceCollection().BuildServiceProvider();
        var handler = new Millet.Compras.Application.Inicio.ResumenInicioHandler(db, usuario, new Permisos(), new Reloj(), new Mediator(servicios));
        Assert.Equal(1, (await handler.Handle(new("requisiciones") { SucursalesPermitidas = [propia] }, CancellationToken.None)).Total);
        Assert.Equal(2, (await handler.Handle(new("requisiciones"), CancellationToken.None)).Total);
        Assert.Equal(0, (await handler.Handle(new("requisiciones") { SucursalesPermitidas = [] }, CancellationToken.None)).Total);
    }
    [Fact]
    public async Task Recepciones_de_Almacen_transmiten_alcance_al_puerto_de_Compras()
    {
        var empresa = new Empresa(); var sucursal = Guid.NewGuid(); var puerto = new Recepciones();
        using var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var handler = new Millet.Almacen.Application.Inicio.ResumenInicioHandler(db, puerto, new Reloj());
        var propio = await handler.Handle(new("recepciones") { SucursalesPermitidas = [sucursal] }, CancellationToken.None);
        Assert.Equal(new[] { sucursal }, puerto.Alcance);
        Assert.Equal(1, propio.Total);
        Assert.Equal(2, (await handler.Handle(new("recepciones"), CancellationToken.None)).Total);
        Assert.Null(puerto.Alcance);
    }
    private sealed class Recepciones : IComprasRecepcionesReadPort
    {
        public IReadOnlyList<Guid>? Alcance { get; private set; }
        public Task<RecepcionesPendientesLectura> ResumirRecepcionesAsync(IReadOnlyList<Guid>? sucursales, CancellationToken cancellationToken)
        {
            Alcance = sucursales;
            return Task.FromResult(new RecepcionesPendientesLectura(sucursales is null ? 2 : sucursales.Count, 0));
        }
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 10, 11, 1, 0, 0, TimeSpan.Zero); }
    private sealed class Usuario : ICurrentUserContext { public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "Prueba INI-ROL"; }
    private sealed class Permisos : ICurrentUserPermissions
    { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(false); }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid(); public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
