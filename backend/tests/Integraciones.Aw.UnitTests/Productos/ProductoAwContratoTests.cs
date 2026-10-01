using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>ADM-07 D1/D2: adaptador del contrato V1 y reintento por referencia con versión esperada (EF InMemory).</summary>
public class ProductoAwContratoTests
{
    private static CompartidoDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"producto-contrato-{Guid.NewGuid():N}").Options;
        var db = new CompartidoDbContext(options, new SinEmpresa());
        db.UnidadesMedida.Add(new UnidadMedida(Guid.NewGuid(), "M2", "Metro cuadrado", DimensionUnidad.Area, 1m, 2, true));
        db.SaveChanges();
        return db;
    }

    private static async Task<ProductoAw> SembrarAsync(CompartidoDbContext db, string referencia, bool baja = false)
    {
        var p = new ProductoAw(Guid.CreateVersion7(), referencia, "DEMO", "M2");
        p.AplicarVariantes([new("B", null, null, 8m, null), new("A", 1000m, 500m, 6m, "6mm")]);
        if (baja) p.DarDeBaja(DateTime.UtcNow);
        db.ProductosAw.Add(p);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return p;
    }

    [Fact]
    public async Task Lee_por_referencia_y_por_id_con_variantes_ordenadas_y_version_de_contrato()
    {
        using var db = NewDb();
        var p = await SembrarAsync(db, "DEMO-C1");
        var port = new ProductoAwContratoReadAdapter(db);

        var porRef = await port.ObtenerPorReferenciaAsync(" DEMO-C1 ", default);
        var porId = await port.ObtenerPorIdAsync(p.Id, default);

        porRef.Should().NotBeNull();
        porRef!.VersionContrato.Should().Be("1");
        porRef.Estatus.Should().Be("Activo");
        porRef.Variantes.Select(v => v.ClaveVariante).Should().Equal("A", "B");
        porRef.Variantes[1].AltoMm.Should().BeNull("nulo no es 0");
        porId.Should().BeEquivalentTo(porRef);
    }

    [Fact]
    public async Task Devuelve_inactivos_con_estatus_y_null_si_no_existe()
    {
        using var db = NewDb();
        await SembrarAsync(db, "DEMO-BAJA", baja: true);
        var port = new ProductoAwContratoReadAdapter(db);

        var c = await port.ObtenerPorReferenciaAsync("DEMO-BAJA", default);

        c!.Estatus.Should().Be("Inactivo");
        c.FechaBaja.Should().NotBeNull();
        (await port.ObtenerPorReferenciaAsync("NO-EXISTE", default)).Should().BeNull();
        (await port.ObtenerPorReferenciaAsync("  ", default)).Should().BeNull();
        (await port.ObtenerPorIdAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task Reintento_con_version_vieja_da_conflicto_y_no_escribe()
    {
        using var db = NewDb();
        var json = """
            {"origen":{"vw_erp_articulo":[{"producto_ref":"DEMO-C2","descripcion":"NUEVA","unidad_medida":"M2","baja":false,
             "variantes":[],"TRANSACTION_TIME":"2026-09-01T10:00:00"}]}}
            """;
        var p = await SembrarAsync(db, "DEMO-C2");
        var sp = new ServiceCollection().AddSingleton<IAwProductosOrigen>(AwProductosOrigenSimulado.DesdeJson(json)).BuildServiceProvider();
        var sync = new AwProductosSincronizador(db, new AplicarProductoAwService(db), sp,
            Options.Create(new AwProductosOptions { OrigenHabilitado = true }), TimeProvider.System,
            NullLogger<AwProductosSincronizador>.Instance);

        var viejo = await sync.SincronizarReferenciaAsync("DEMO-C2", default, versionEsperada: p.Version + 5);
        viejo.Conflictos.Should().Be(1);
        (await db.ProductosAw.SingleAsync()).Descripcion.Should().Be("DEMO");

        var vigente = await sync.SincronizarReferenciaAsync("DEMO-C2", default, versionEsperada: p.Version);
        vigente.Actualizados.Should().Be(1);
        (await db.ProductosAw.SingleAsync()).Descripcion.Should().Be("NUEVA");
    }

    [Fact]
    public async Task Mapper_entrega_la_hora_de_origen_en_UTC_para_Npgsql()
    {
        var json = """{"origen":{"vw_erp_articulo":[{"producto_ref":"DEMO-T","descripcion":"D","unidad_medida":"M2","baja":false,"variantes":[],"TRANSACTION_TIME":"2026-09-01T10:00:00"}]}}""";
        var fila = (await AwProductosOrigenSimulado.DesdeJson(json).LeerPaginaAsync(null, 1, default)).Filas[0];

        var snap = AwProductoSnapshotMapper.Mapear(fila, DateTime.UtcNow).Snapshot!;

        snap.TransaccionOrigenUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        snap.TransaccionOrigenUtc.Value.Hour.Should().Be(10);
    }

    private sealed class SinEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
