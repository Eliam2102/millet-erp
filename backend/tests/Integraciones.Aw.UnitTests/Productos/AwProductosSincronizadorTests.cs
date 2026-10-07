using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>ADM-07 C2: los 11 fixtures a través de <see cref="AwProductosSincronizador"/> (EF InMemory).</summary>
public class AwProductosSincronizadorTests
{
    private static CompartidoDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"productos-sync-{Guid.NewGuid():N}").Options;
        var db = new CompartidoDbContext(options, new BypassedEmpresaContext());
        db.UnidadesMedida.Add(new UnidadMedida(Guid.NewGuid(), "M2", "Metro cuadrado", DimensionUnidad.Conteo, 1m, 2, true));
        db.SaveChanges();
        return db;
    }

    private static AwProductosSincronizador Sincronizador(CompartidoDbContext db, IAwProductosOrigen origen, int lote = 100)
    {
        var sp = new ServiceCollection().AddSingleton(origen).BuildServiceProvider();
        return new(db, new AplicarProductoAwService(db), sp,
            Options.Create(new AwProductosOptions { OrigenHabilitado = true, TamanoLote = lote }),
            TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance);
    }

    private static string Json(string escenario) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Productos", "Fixtures", escenario + ".json"));

    /// <summary>Siembra el estado previo del fixture (producto A+W existente con otra descripción).</summary>
    private static async Task SembrarAsync(CompartidoDbContext db, string escenario, bool igualAlOrigen = false)
    {
        var raiz = System.Text.Json.JsonDocument.Parse(Json(escenario)).RootElement;
        var previo = raiz.GetProperty("estadoErpPrevio");
        if (!previo.GetProperty("existe").GetBoolean()) return;
        var origen = AwProductosOrigenSimulado.DesdeJson(Json(escenario));
        var fila = (await origen.LeerPaginaAsync(null, 1, default)).Filas[0] with
        {
            ProductoRef = previo.GetProperty("referenciaExterna").GetString(),
        };
        if (!igualAlOrigen)
            fila = fila with { Descripcion = "PREVIO", Baja = false,
                Variantes = [new("V1", null, null, 6m, "6mm")] };
        var snap = AwProductoSnapshotMapper.Mapear(fila, DateTime.UtcNow.AddMinutes(-5)).Snapshot!;
        await new AplicarProductoAwService(db).AplicarAsync(snap, default);
        db.ChangeTracker.Clear();
    }

    private static async Task<AwProductosResumen> CorrerAsync(CompartidoDbContext db, string escenario)
        => await Sincronizador(db, AwProductosOrigenSimulado.DesdeJson(Json(escenario))).SincronizarBarridoAsync(default);

    [Fact]
    public async Task Nominal_crea_un_producto_con_su_variante()
    {
        using var db = NewDb();
        var r = await CorrerAsync(db, "nominal");

        (r.Leidos, r.Creados, r.Errores).Should().Be((1, 1, 0));
        (await db.ProductosAw.CountAsync()).Should().Be(1);
        (await db.ProductosAw.SingleAsync()).UnidadMedida.Should().Be("M2");
        (await db.ProductosAwVariantes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Dos_medidas_mismo_codigo_son_un_producto_y_dos_variantes()
    {
        using var db = NewDb();
        var r = await CorrerAsync(db, "dos-medidas-mismo-codigo");

        r.Creados.Should().Be(1);
        (await db.ProductosAw.CountAsync()).Should().Be(1);
        (await db.ProductosAwVariantes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Tratado_templado_es_producto_independiente_del_base()
    {
        using var db = NewDb();
        await SembrarAsync(db, "tratado-templado");
        var r = await CorrerAsync(db, "tratado-templado");

        r.Creados.Should().Be(1);
        (await db.ProductosAw.CountAsync()).Should().Be(2);
        (await db.ProductosAw.SingleAsync(p => p.ReferenciaExterna == "DEMO-P003")).Descripcion.Should().Be("PREVIO");
    }

    [Fact]
    public async Task Tratado_laminado_conserva_composicion()
    {
        using var db = NewDb();
        var r = await CorrerAsync(db, "tratado-laminado");

        r.Creados.Should().Be(1);
        (await db.ProductosAwVariantes.SingleAsync()).Composicion.Should().Be("6mm+PVB+6mm");
    }

    [Fact]
    public async Task Unidad_desconocida_no_persiste_nada()
    {
        using var db = NewDb();
        var r = await CorrerAsync(db, "unidad-desconocida");

        (r.Pendientes, r.Creados, r.Errores).Should().Be((1, 0, 0));
        r.ErroresPorReferencia.Should().ContainSingle(e => e.Codigo == "UNIDAD_SIN_EQUIVALENCIA");
        (await db.ProductosAw.CountAsync()).Should().Be(0);
        (await db.ProductosAwVariantes.CountAsync()).Should().Be(0);
        (await db.ProductosSincronizacionAw.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Duplicado_en_el_lote_no_crea_segundo_registro()
    {
        using var db = NewDb();
        await SembrarAsync(db, "duplicado", igualAlOrigen: true);
        var r = await CorrerAsync(db, "duplicado");

        (r.Leidos, r.SinCambios, r.Creados).Should().Be((2, 2, 0));
        (await db.ProductosAw.CountAsync()).Should().Be(1);
        (await db.ProductosSincronizacionAw.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Duplicado_sin_estado_previo_crea_una_vez()
    {
        using var db = NewDb();
        var r = await CorrerAsync(db, "duplicado");

        (r.Creados, r.SinCambios).Should().Be((1, 1));
        (await db.ProductosAw.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("baja")]
    [InlineData("baja-con-historico")]
    public async Task Baja_inactiva_con_fecha_y_no_elimina(string escenario)
    {
        using var db = NewDb();
        await SembrarAsync(db, escenario);
        var r = await CorrerAsync(db, escenario);

        r.Actualizados.Should().Be(1);
        var p = await db.ProductosAw.SingleAsync(); // sigue consultable por id (histórico)
        p.Estatus.Should().Be(EstatusCatalogo.Inactivo);
        p.FechaBaja.Should().NotBeNull();
    }

    [Fact]
    public async Task Conflicto_con_producto_manual_no_se_sobrescribe()
    {
        // ponytail: el sincronizador no lee Version del origen; ConflictoVersion por VersionEsperada se cubre en AplicarProductoAwServiceTests.
        using var db = NewDb();
        db.ProductosAw.Add(new ProductoAw(Guid.NewGuid(), "DEMO-P009", "EDICION MANUAL", "M2", OrigenMaster.Manual));
        await db.SaveChangesAsync();
        var r = await CorrerAsync(db, "conflicto-version");

        r.Conflictos.Should().Be(1);
        (await db.ProductosAw.SingleAsync()).Descripcion.Should().Be("EDICION MANUAL");
    }

    [Fact]
    public async Task Fiscal_ya_completado_no_se_pisa()
    {
        using var db = NewDb();
        await SembrarAsync(db, "campo-fiscal-ya-completado");
        var p = await db.ProductosAw.SingleAsync();
        p.AsignarDatosFiscales("43211701", "MTK", "02", 0.16m);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var r = await CorrerAsync(db, "campo-fiscal-ya-completado");

        r.Actualizados.Should().Be(1);
        var q = await db.ProductosAw.SingleAsync();
        q.Descripcion.Should().Be("DESCRIPCION NUEVA A+W");
        (q.ClaveProdServSat, q.ClaveUnidadSat).Should().Be(("43211701", "MTK"));
    }

    [Fact]
    public async Task Medida_nula_no_es_cero()
    {
        using var db = NewDb();
        await CorrerAsync(db, "medida-nula-no-es-cero");

        var v = await db.ProductosAwVariantes.SingleAsync();
        (v.AltoMm, v.AnchoMm, v.EspesorMm).Should().Be(((decimal?)null, (decimal?)null, (decimal?)null));
    }

    [Fact]
    public async Task Error_en_una_fila_no_aborta_el_lote_y_se_reporta_por_referencia()
    {
        using var db = NewDb();
        var ok = new AwProductoOrigenVariante("V1", null, null, 6m, "6mm");
        var origen = new AwProductosOrigenSimulado([
            new("A-OK", "UNO", "m²", false, [ok], null),            // 'm²' normaliza a M2
            new("B-MAL", "DOS", "M2", false, [ok with { ClaveVariante = " " }], null), // variante inválida
            new("C-OK", "TRES", "M2", false, [ok], null),
        ]);
        var r = await Sincronizador(db, origen, lote: 2).SincronizarBarridoAsync(default);

        (r.Leidos, r.Creados, r.Errores).Should().Be((3, 2, 1));
        r.ErroresPorReferencia.Should().ContainSingle(e => e.Referencia == "B-MAL" && e.Codigo == "fila_invalida");
        (await db.ProductosAw.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Origen_deshabilitado_se_rechaza()
    {
        using var db = NewDb();
        var s = new AwProductosSincronizador(db, new AplicarProductoAwService(db), new ServiceCollection().BuildServiceProvider(),
            Options.Create(new AwProductosOptions()), TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance);

        var act = () => s.SincronizarBarridoAsync(default);
        (await act.Should().ThrowAsync<AwProductosSyncException>()).Which.Code.Should().Be("origen_deshabilitado");
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
