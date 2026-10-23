using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Cotizaciones;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Cotizaciones;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compras.UnitTests.Application;

/// <summary>C-05: la marca «Más bajo» solo se calcula entre cotizaciones elegibles (vigentes, con PDF, proveedor activo).</summary>
public sealed class ComparativoMasBajoElegiblesTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);

    [Fact]
    public async Task MasBarataSinPdf_NoLlevaMarca_LaLlevaLaMasBarataElegible()
    {
        await using var f = Fixture.Crear();
        var sinPdf = f.Cotizar(100m, conPdf: false);
        var elegibleBarata = f.Cotizar(200m);
        var elegibleCara = f.Cotizar(300m);
        var r = await f.ConsultarAsync();
        Assert.False(Marca(r, sinPdf));
        Assert.True(Marca(r, elegibleBarata));
        Assert.False(Marca(r, elegibleCara));
    }

    [Fact]
    public async Task MasBarataVencida_NoLlevaMarca()
    {
        await using var f = Fixture.Crear();
        var vencida = f.Cotizar(100m, vigenteHasta: Hoy.AddDays(-1));
        var vigente = f.Cotizar(200m);
        var r = await f.ConsultarAsync();
        Assert.False(Marca(r, vencida));
        Assert.True(Marca(r, vigente));
    }

    [Fact]
    public async Task MasBarataDeProveedorInactivo_NoLlevaMarca()
    {
        await using var f = Fixture.Crear();
        var inactivo = f.Cotizar(100m, proveedorActivo: false);
        var activo = f.Cotizar(200m);
        var r = await f.ConsultarAsync();
        Assert.False(Marca(r, inactivo));
        Assert.True(Marca(r, activo));
    }

    [Fact]
    public async Task NingunaElegible_NingunaLlevaMarca()
    {
        await using var f = Fixture.Crear();
        f.Cotizar(100m, conPdf: false);
        f.Cotizar(200m, vigenteHasta: Hoy.AddDays(-1));
        var r = await f.ConsultarAsync();
        Assert.All(r.Cotizaciones, c => Assert.All(c.Lineas, l => Assert.False(l.EsMenorPrecio)));
    }

    private static bool Marca(ComparativoCotizacionesResponse r, Guid cotizacionId) =>
        r.Cotizaciones.Single(c => c.Id == cotizacionId).Lineas.Single().EsMenorPrecio;

    private sealed class Fixture : IAsyncDisposable
    {
        public required ComprasDbContext Db { get; init; }
        public required CompartidoDbContext Compartido { get; init; }
        public required Requisicion Rq { get; init; }
        private readonly Contexto _ctx = null!;
        private Fixture(Contexto ctx) => _ctx = ctx;

        public static Fixture Crear()
        {
            var empresa = Guid.NewGuid();
            var ctx = new Contexto(empresa);
            var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
                .UseInMemoryDatabase($"CMP-{Guid.NewGuid()}").Options, ctx);
            var compartido = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
                .UseInMemoryDatabase($"CMP-C-{Guid.NewGuid()}").Options, ctx);
            var rq = new Requisicion(Guid.NewGuid(), empresa, Folio.Parse("MID2026-000951"), 2026,
                Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Prioridad.Normal, DateTimeOffset.UtcNow, null);
            var l1 = rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", Money.Mxn(100), centroCostoId: Guid.NewGuid());
            rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
            rq.RegistrarCubrimiento([new CubrimientoLinea(l1.Id, 0, 10)], DateTimeOffset.UtcNow);
            db.Add(rq);
            db.SaveChanges();
            return new Fixture(ctx) { Db = db, Compartido = compartido, Rq = rq };
        }

        /// <summary>Crea una cotización MXN con un proveedor y, opcionalmente, su PDF; devuelve su id.</summary>
        public Guid Cotizar(decimal precio, bool conPdf = true, DateOnly? vigenteHasta = null, bool proveedorActivo = true)
        {
            var prov = new Proveedor(Guid.NewGuid(), "P" + Guid.NewGuid().ToString("N")[..8], "Proveedor", "AAA010101AAA",
                TipoPersonaProveedor.Moral, proveedorActivo ? EstatusCatalogo.Activo : EstatusCatalogo.Inactivo);
            Compartido.Proveedores.Add(prov);
            var desde = vigenteHasta is null ? Hoy : Hoy.AddDays(-10);
            var c = Cotizacion.Registrar(Guid.NewGuid(), Rq.EmpresaId, Rq.Id, Rq.SucursalId, prov.Id, "F-1", "MXN",
                desde, vigenteHasta ?? Hoy.AddDays(5), null,
                Rq.Lineas.Select(l => new LineaCotizacionDatos(l.Id, "PZA", "PZA", 1m, precio, 3)).ToList(),
                Guid.NewGuid(), DateTimeOffset.UtcNow, desde);
            Db.Cotizaciones.Add(c);
            if (conPdf)
                Compartido.Adjuntos.Add(new Adjunto(Guid.NewGuid(), "cotizacion_compra", c.Id, Rq.EmpresaId, Guid.NewGuid(),
                    "cot.pdf", "application/pdf", 10, new string('a', 64), "blob/cot", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
            Db.SaveChanges();
            Compartido.SaveChanges();
            return c.Id;
        }

        public Task<ComparativoCotizacionesResponse> ConsultarAsync() =>
            new ComparativoCotizacionesHandler(Db, Compartido, new ArticulosVacios(), _ctx)
                .Handle(new ComparativoCotizacionesQuery(Rq.Id), default);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Compartido.DisposeAsync();
        }
    }

    private sealed class ArticulosVacios : IArticuloReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class Contexto(Guid empresa) : ICurrentEmpresaContext, ICurrentUserContext, IClock
    {
        public Guid? Current => empresa;
        public bool IsBypassed => false;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "CMP";
        public DateTimeOffset UtcNow => new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
