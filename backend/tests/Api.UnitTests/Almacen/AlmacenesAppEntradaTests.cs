using System.Text.Json;
using Millet.Almacen.Domain.Ports;
using Millet.Api.Endpoints.Almacen;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>
/// M-159: lectura de ENTRADA_ALMACEN (contrato v8) con mensajes reales de la base de ensayo de la app (AlmacenRQ_MP_ENSAYO,
/// 9-nov): vidrio por medida con reclamadas y herraje sin medida. <c>compra</c> no es un folio del ERP y no debe romper.
/// </summary>
public sealed class AlmacenesAppEntradaTests
{
    // EN-002036: 10 hojas 6MM CLARO 5182x3302 (171.1100 m²), 3 reclamadas, línea BYSTRONIC.
    private const string Vidrio = """
{"mensajeId":"86ead19d-feb5-4ad1-85ef-1212cd761ac9","tipo":"ENTRADA_ALMACEN","version":1,"fecha":"2026-11-09T17:29:01.9863171-06:00","origen":"AlmacenRQ_MP","referencia":"EN-002036","datos":{"entrada":"EN-002036","compra":"zz_arnes_RP1","sucursal":"CONKAL","piezas":10,"lineas":[{"requisicion":"RQ-00002535","posicion":1,"articuloAw":100006,"articuloAlmacenAw":9435,"articulo":"6MM CLARO","ancho":5182.00,"alto":3302.00,"almacenGeneral":{"id":1,"nombre":"Conkal exportacion","tipo":"EXPORTACION","linea":"BYSTRONIC"},"piezas":10,"grupos":[{"articuloAw":100006,"articuloAlmacenAw":9435,"ancho":5182.00,"alto":3302.00,"lote":"zz_arnes","pedimento":null,"etiqueta":"zz_arnes_RP","piezas":10,"m2":171.1100}],"almacenes":[{"almacenGeneral":{"id":1,"nombre":"Conkal exportacion","tipo":"EXPORTACION","linea":"BYSTRONIC"},"piezas":10,"grupos":[{"articuloAw":100006,"articuloAlmacenAw":9435,"ancho":5182.00,"alto":3302.00,"lote":"zz_arnes","pedimento":null,"etiqueta":"zz_arnes_RP","piezas":10,"m2":171.1100}]}],"reclamadas":3}]}}
""";

    // EN-002035: 4 piezas de herraje (ancho/alto/m2 en null).
    private const string Herraje = """
{"mensajeId":"5eaea4de-ac99-417c-a66a-98cc9ee483ba","tipo":"ENTRADA_ALMACEN","version":1,"fecha":"2026-11-09T17:29:00.707891-06:00","origen":"AlmacenRQ_MP","referencia":"EN-002035","datos":{"entrada":"EN-002035","compra":"zz_arnes_EKH","sucursal":"CONKAL","piezas":4,"lineas":[{"requisicion":"RQ-00002534","posicion":1,"articuloAw":920035,"articuloAlmacenAw":null,"articulo":"JC100 JALADERA DE CRISTAL CLARO DE 10 MM","ancho":null,"alto":null,"almacenGeneral":{"id":1,"nombre":"Conkal exportacion","tipo":"EXPORTACION","linea":null},"piezas":4,"grupos":[{"articuloAw":920035,"articuloAlmacenAw":4032,"ancho":null,"alto":null,"lote":"zz_arnes","pedimento":null,"etiqueta":null,"piezas":4,"m2":null}],"almacenes":[{"almacenGeneral":{"id":1,"nombre":"Conkal exportacion","tipo":"EXPORTACION","linea":null},"piezas":4,"grupos":[{"articuloAw":920035,"articuloAlmacenAw":4032,"ancho":null,"alto":null,"lote":"zz_arnes","pedimento":null,"etiqueta":null,"piezas":4,"m2":null}]}],"reclamadas":0}]}}
""";

    private static JsonElement Datos(string sobre) => JsonDocument.Parse(sobre).RootElement.GetProperty("datos").Clone();

    private static JsonElement Datos(string sobre, Func<string, string> cambio) => Datos(cambio(sobre));

    private static readonly Guid Sucursal = Guid.NewGuid();

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public string? Sucursal_ { get; private set; }
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct)
        {
            Sucursal_ = sucursalCodigo;
            if (grupos.Any(g => g.ArticuloAw == "999999"))
                return Task.FromResult(new ResolverSalidaResultado("No existe un artículo ligado al artículo A+W 999999.", Guid.Empty, []));
            return Task.FromResult(new ResolverSalidaResultado(null, Sucursal, grupos.Select(g => g.AnchoMm is null
                ? new GrupoSalidaResuelto(Guid.NewGuid(), "PZA", null, null, null)
                : new GrupoSalidaResuelto(Guid.NewGuid(), "M2", Guid.NewGuid(), g.AnchoMm, g.AltoMm, AreaHoja.M2(g.AnchoMm.Value, g.AltoMm!.Value))).ToList()));
        }
    }

    private sealed class UsuarioServicio : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), Guid.NewGuid(), true));
    }

    [Fact]
    public async Task Vidrio_real_entra_por_el_total_incluidas_las_reclamadas_en_m2_de_la_hoja()
    {
        var resolver = new Resolver();
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(Datos(Vidrio), resolver, new UsuarioServicio(), default);

        Assert.Null(error);
        Assert.Equal("CONKAL", resolver.Sucursal_);
        Assert.Equal(Sucursal, r!.SucursalId);
        var e = Assert.Single(r.Entradas);
        Assert.Equal(10 * AreaHoja.M2(5182m, 3302m), e.Cantidad);
        Assert.NotNull(e.ProductoAwVarianteId);
        Assert.Equal("zz_arnes", e.Lote); // grupos[].lote llega a la partida (llave del saldo)
        Assert.Equal("App Almacenes ENTRADA_ALMACEN EN-002036 compra zz_arnes_RP1", r.Motivo);
    }

    [Fact]
    public async Task Diez_hojas_3302x6100_suben_10_por_20_1422_m2()
    {
        var datos = Datos(Vidrio, s => s.Replace("5182.00", "3302").Replace("\"alto\":3302.00", "\"alto\":6100").Replace("171.11", "201.422"));
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(datos, new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        Assert.Equal(10 * 20.1422m, Assert.Single(r!.Entradas).Cantidad);
    }

    [Fact]
    public async Task Herraje_real_sin_medida_entra_por_piezas()
    {
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(Datos(Herraje), new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        var e = Assert.Single(r!.Entradas);
        Assert.Equal(4m, e.Cantidad);
        Assert.Null(e.ProductoAwVarianteId);
    }

    [Fact]
    public async Task Articulo_AW_desconocido_da_el_error_del_resolvedor()
    {
        var datos = Datos(Herraje, s => s.Replace("920035", "999999"));
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(datos, new Resolver(), new UsuarioServicio(), default);

        Assert.Null(r);
        Assert.Contains("999999", error);
    }

    [Theory]
    [InlineData("\"piezas\":4,\"lineas\"", "\"piezas\":5,\"lineas\"", "datos.piezas")]
    [InlineData("\"reclamadas\":0", "\"reclamadas\":9", "reclamadas")]
    [InlineData("\"sucursal\":\"CONKAL\"", "\"sucursal\":null", "sucursal")]
    public async Task Datos_inconsistentes_dan_ERROR(string de, string a, string contiene)
    {
        var datos = Datos(Herraje, s => s.Replace(de, a));
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(datos, new Resolver(), new UsuarioServicio(), default);
        Assert.Null(r);
        Assert.Contains(contiene, error);
    }

    [Fact]
    public async Task Grupos_de_almacenes_que_no_suman_la_linea_dan_ERROR()
    {
        // Solo cambia el detalle por división: la suma de almacenes[].grupos ya no da las piezas de la línea.
        var datos = Datos(Vidrio, s =>
        {
            var i = s.IndexOf("\"almacenes\"", StringComparison.Ordinal);
            return s[..i] + s[i..].Replace("\"piezas\":10,\"m2\":171.11", "\"piezas\":9,\"m2\":153.999");
        });
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(datos, new Resolver(), new UsuarioServicio(), default);

        Assert.Null(r);
        Assert.Contains("no coincide con piezas", error);
    }

    [Fact]
    public async Task Lleva_compra_y_entrada_para_ligar_la_recepcion_a_la_OC()
    {
        var (r, _) = await AlmacenesAppEntrada.ManejarAsync(Datos(Vidrio), new Resolver(), new UsuarioServicio(), default);

        Assert.Equal(("zz_arnes_RP1", "EN-002036"), (r!.Compra, r.Entrada));
    }

    [Fact]
    public void Un_ENTRADA_ALMACEN_en_ERROR_se_reevalua_y_se_ve_en_la_bandeja_de_mensajes()
    {
        // Una sola lista para el reenvío y para GET /api/v1/almacen/mensajes-app/revision: no pueden divergir.
        Assert.Contains(AlmacenesAppEntrada.Tipo, AlmacenesAppProcesador.TiposReevaluables);
        Assert.Contains("AJUSTE_SALIDA", AlmacenesAppProcesador.TiposReevaluables);
    }
}
