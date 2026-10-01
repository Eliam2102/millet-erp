using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>
/// ADM-07 C1: <see cref="AplicarProductoAwService"/> sobre EF InMemory. La carrera de alta
/// (unique violation de Postgres) y el índice único de variantes se validan solo contra Postgres.
/// </summary>
public class AplicarProductoAwServiceTests
{
    private static CompartidoDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"producto-aw-{Guid.NewGuid():N}")
            .Options;
        var db = new CompartidoDbContext(options, new BypassedEmpresaContext());
        db.UnidadesMedida.Add(new UnidadMedida(Guid.NewGuid(), "M2", "Metro cuadrado", DimensionUnidad.Area, 1m, 2, true));
        db.UnidadesMedida.Add(new UnidadMedida(Guid.NewGuid(), "M3", "Metro cúbico", DimensionUnidad.Volumen, 1000m, 3, false));
        db.SaveChanges();
        return db;
    }

    private static readonly ProductoAwVarianteDato V1 = new("V1", null, null, 6m, "6mm");

    private static AplicarProductoAwSnapshot Snap(
        string referencia = "DEMO-P001", string descripcion = "PRODUCTO DEMO 001", string unidad = "M2",
        bool baja = false, ProductoAwVarianteDato[]? variantes = null, int? versionEsperada = null,
        string? claveUnidadSat = null, DateTime? leido = null) => new(
        referencia, descripcion, unidad, baja, variantes ?? [V1],
        leido ?? DateTime.UtcNow, "1", "0-borrador", VersionEsperada: versionEsperada,
        ClaveUnidadSatSugerida: claveUnidadSat);

    private static Task<AplicarProductoAwResultado> Aplicar(CompartidoDbContext db, AplicarProductoAwSnapshot s) =>
        new AplicarProductoAwService(db).AplicarAsync(s, CancellationToken.None);

    [Fact]
    public async Task Alta_crea_producto_variante_y_registro()
    {
        using var db = NewDb();
        var r = await Aplicar(db, Snap());

        r.Accion.Should().Be(AplicarProductoAwAccion.Creado);
        var p = await db.ProductosAw.Include(x => x.Variantes).SingleAsync();
        p.Origen.Should().Be(OrigenMaster.Aw);
        p.UnidadMedida.Should().Be("M2");
        p.UnidadMedidaId.Should().NotBeNull();
        p.Variantes.Should().ContainSingle().Which.AltoMm.Should().BeNull();
        (await db.ProductosSincronizacionAw.SingleAsync()).HashOrigen.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("M2")]
    [InlineData("M3")]
    public async Task Unidades_de_A_W_M2_y_M3_resuelven_contra_el_catalogo(string unidad)
    {
        using var db = NewDb();
        var r = await Aplicar(db, Snap(unidad: unidad));

        r.Accion.Should().Be(AplicarProductoAwAccion.Creado);
        var p = await db.ProductosAw.SingleAsync();
        p.UnidadMedida.Should().Be(unidad);
        p.UnidadMedidaId.Should().NotBeNull();
    }

    [Fact]
    public async Task Mismo_hash_es_SinCambios_y_no_duplica()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var r = await Aplicar(db, Snap());

        r.Accion.Should().Be(AplicarProductoAwAccion.SinCambios);
        (await db.ProductosAw.CountAsync()).Should().Be(1);
        (await db.ProductosSincronizacionAw.CountAsync()).Should().Be(1);
        (await db.ProductosAwVariantes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Actualiza_descripcion_y_variantes_sin_pisar_fiscales_y_registra_diferencia()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var p = await db.ProductosAw.SingleAsync();
        p.AsignarDatosFiscales("43211701", "MTK", "02", 0.16m);
        p.AsignarDatosAduana("70072101", "06", 1.5m);
        await db.SaveChangesAsync();

        var v1b = new ProductoAwVarianteDato("V1", null, null, 8m, "8mm");
        var r = await Aplicar(db, Snap(descripcion: "DESCRIPCION NUEVA", variantes: [v1b], claveUnidadSat: "H87"));

        r.Accion.Should().Be(AplicarProductoAwAccion.Actualizado);
        var q = await db.ProductosAw.Include(x => x.Variantes).SingleAsync();
        q.Descripcion.Should().Be("DESCRIPCION NUEVA");
        q.Variantes.Single().EspesorMm.Should().Be(8m);
        q.ClaveProdServSat.Should().Be("43211701");
        q.ClaveUnidadSat.Should().Be("MTK");
        q.TasaIvaTraslado.Should().Be(0.16m);
        q.FraccionArancelaria.Should().Be("70072101");
        (await db.ProductosSincronizacionAw.SingleAsync()).Diferencias.Should().Contain("H87");
    }

    [Fact]
    public async Task Unidad_sin_equivalencia_en_alta_no_persiste_nada()
    {
        using var db = NewDb();
        var r = await Aplicar(db, Snap(unidad: "XYZ"));

        r.Accion.Should().Be(AplicarProductoAwAccion.NoAplicado);
        r.Resultado.Should().Be(ResultadoSincronizacionAw.Pendiente);
        r.Causa.Should().Be("UNIDAD_SIN_EQUIVALENCIA");
        (await db.ProductosAw.CountAsync()).Should().Be(0);
        (await db.ProductosSincronizacionAw.CountAsync()).Should().Be(0);
        (await db.ProductosAwVariantes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unidad_sin_equivalencia_en_existente_deja_el_producto_intacto_y_registra_causa()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var r = await Aplicar(db, Snap(descripcion: "OTRA", unidad: "XYZ"));

        r.Resultado.Should().Be(ResultadoSincronizacionAw.Pendiente);
        var p = await db.ProductosAw.SingleAsync();
        p.Descripcion.Should().Be("PRODUCTO DEMO 001");
        p.UnidadMedida.Should().Be("M2");
        var reg = await db.ProductosSincronizacionAw.SingleAsync();
        reg.Resultado.Should().Be(ResultadoSincronizacionAw.Pendiente);
        reg.Error.Should().Contain("UNIDAD_SIN_EQUIVALENCIA");
    }

    [Fact]
    public async Task Baja_da_de_baja_sin_borrar_y_conserva_variantes()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var r = await Aplicar(db, Snap(baja: true));

        r.Accion.Should().Be(AplicarProductoAwAccion.Actualizado);
        var p = await db.ProductosAw.Include(x => x.Variantes).SingleAsync();
        p.Estatus.Should().Be(EstatusCatalogo.Inactivo);
        p.FechaBaja.Should().NotBeNull();
        p.Variantes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Fin_de_baja_desde_Aw_reactiva_pero_una_baja_local_se_respeta()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        await Aplicar(db, Snap(baja: true));
        await Aplicar(db, Snap(baja: false));
        var p = await db.ProductosAw.SingleAsync();
        p.Estatus.Should().Be(EstatusCatalogo.Activo);
        p.FechaBaja.Should().BeNull();

        p.DarDeBaja(DateTime.UtcNow); // baja manual (Desactivar)
        await db.SaveChangesAsync();
        await Aplicar(db, Snap(descripcion: "CAMBIO"));
        (await db.ProductosAw.SingleAsync()).Estatus.Should().Be(EstatusCatalogo.Inactivo);
        (await db.ProductosSincronizacionAw.SingleAsync()).Diferencias.Should().Contain("Estatus");
    }

    [Fact]
    public async Task Version_esperada_distinta_es_Conflicto_y_no_sobrescribe()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var version = (await db.ProductosAw.AsNoTracking().SingleAsync()).Version;

        var r = await Aplicar(db, Snap(descripcion: "DESDE A+W", versionEsperada: version + 1));

        r.Accion.Should().Be(AplicarProductoAwAccion.Conflicto);
        r.Resultado.Should().Be(ResultadoSincronizacionAw.Conflicto);
        (await db.ProductosAw.AsNoTracking().SingleAsync()).Descripcion.Should().Be("PRODUCTO DEMO 001");

        var ok = await Aplicar(db, Snap(descripcion: "DESDE A+W", versionEsperada: version));
        ok.Accion.Should().Be(AplicarProductoAwAccion.Actualizado);
    }

    [Fact]
    public async Task Medidas_nulas_siguen_nulas_y_dos_medidas_del_mismo_codigo_son_dos_variantes()
    {
        using var db = NewDb();
        var r = await Aplicar(db, Snap(variantes:
        [
            new("V1", null, null, null, null),
            new("V2", 1000m, 2000m, 6m, "6mm"),
        ]));

        r.Accion.Should().Be(AplicarProductoAwAccion.Creado);
        (await db.ProductosAw.CountAsync()).Should().Be(1);
        var vs = await db.ProductosAwVariantes.OrderBy(v => v.ClaveVariante).ToListAsync();
        vs.Should().HaveCount(2);
        vs[0].AltoMm.Should().BeNull();
        vs[0].AnchoMm.Should().BeNull();
        vs[0].EspesorMm.Should().BeNull();
        vs[1].AltoMm.Should().Be(1000m);
    }

    [Fact]
    public async Task SoloCrear_no_toca_existente_y_acepta_unidad_fuera_de_catalogo_en_alta()
    {
        using var db = NewDb();
        var alta = await Aplicar(db, Snap(unidad: "XYZ") with { SoloCrear = true });
        alta.Accion.Should().Be(AplicarProductoAwAccion.Creado);
        (await db.ProductosAw.SingleAsync()).UnidadMedidaId.Should().BeNull();

        var otra = await Aplicar(db, Snap(descripcion: "OTRA") with { SoloCrear = true });
        otra.Accion.Should().Be(AplicarProductoAwAccion.SinCambios);
        (await db.ProductosAw.SingleAsync()).Descripcion.Should().Be("PRODUCTO DEMO 001");
    }

    [Fact]
    public async Task Desactivar_fija_FechaBaja_y_es_idempotente()
    {
        using var db = NewDb();
        await Aplicar(db, Snap());
        var id = (await db.ProductosAw.SingleAsync()).Id;
        var handler = new DesactivarProductoAwHandler(db);

        await handler.Handle(new DesactivarProductoAwCommand(id), CancellationToken.None);
        var fecha = (await db.ProductosAw.SingleAsync()).FechaBaja;
        await handler.Handle(new DesactivarProductoAwCommand(id), CancellationToken.None);

        var p = await db.ProductosAw.SingleAsync();
        p.Estatus.Should().Be(EstatusCatalogo.Inactivo);
        p.FechaBaja.Should().NotBeNull().And.Be(fecha);
    }

    [Fact]
    public async Task Provisionar_delega_en_el_servicio_y_no_pisa_existente()
    {
        using var db = NewDb();
        var handler = new ProvisionarProductoAwHandler(new AplicarProductoAwService(db));

        var nuevo = await handler.Handle(new ProvisionarProductoAwCommand("DEMO-P001", "PRODUCTO", "M2", "MTK"), CancellationToken.None);
        var repetido = await handler.Handle(new ProvisionarProductoAwCommand("DEMO-P001", "OTRA", "M2", "H87"), CancellationToken.None);

        nuevo.Creado.Should().BeTrue();
        repetido.Creado.Should().BeFalse();
        repetido.ProductoId.Should().Be(nuevo.ProductoId);
        var p = await db.ProductosAw.SingleAsync();
        p.Descripcion.Should().Be("PRODUCTO");
        p.ClaveUnidadSat.Should().Be("MTK");
        p.UnidadMedidaId.Should().NotBeNull();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
