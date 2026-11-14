using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Api.Endpoints.Almacen;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>
/// Caminos malos del contrato v11 que no cubría ninguna prueba: lo que el ERP tiene que rechazar de cada mensaje, con el texto que
/// Almacenes va a leer en su acuse. El camino bueno de cada tipo vive en <see cref="AlmacenesAppEnsayoM446Tests"/> (mensajes reales
/// de ENSAYO) y en <see cref="AlmacenesAppVentaAlbaranTests"/>.
/// </summary>
public sealed class AlmacenesAppContratoV11Tests
{
    private static JsonElement Datos(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Task<(AlmacenesAppSalida.Resuelta? Salida, string? Error)> SalidaAsync(string tipo, string json) =>
        AlmacenesAppSalida.ManejarAsync(tipo, Datos(json), new Resolver(), new UsuarioServicio(), default);

    // ── Sobre (v11 §3): sin él no se puede evitar un duplicado ──────────────────────────────────

    [Theory]
    [InlineData("""{"tipo":"ROTURA","version":1,"datos":{}}""", "mensajeId")]
    [InlineData("""{"mensajeId":"m-1","version":1,"datos":{}}""", "sobre inválido")]
    [InlineData("""{"mensajeId":"m-1","tipo":"ROTURA","datos":{}}""", "sobre inválido")]
    [InlineData("""{"mensajeId":"m-1","tipo":"ROTURA","version":1}""", "sobre inválido")]
    public void Un_sobre_incompleto_se_rechaza_antes_de_mirar_los_datos(string sobre, string esperado)
    {
        Assert.Contains(esperado, AlmacenesAppProcesador.ErrorDeSobre(Datos(sobre)));
    }

    [Fact]
    public void Un_mensajeId_de_mas_de_64_no_se_acepta()
    {
        var sobre = """{"mensajeId":"ID","tipo":"ROTURA","version":1,"datos":{}}""".Replace("ID", new string('x', 65));

        Assert.Contains("hasta 64", AlmacenesAppProcesador.ErrorDeSobre(Datos(sobre)));
    }

    // ── REQUISICION_ENVIADA (v11 p.2): autorizacion completa o nada ─────────────────────────────

    private const string RqBase = """
        {"folio":"RQ-00009001","sucursal":"CONKAL","proveedor":{"claveAw":164},"fechaRequerida":"2026-11-20",
         "lineas":[{"posicion":1,"articuloAw":100006,"ancho":1200,"alto":2600,"piezas":10}]}
        """;

    [Fact]
    public void La_requisicion_con_autorizacion_completa_entra_con_su_usuario_y_fecha()
    {
        var json = RqBase.Replace("\"lineas\"",
            "\"autorizacion\":{\"usuario\":\"ealmacen\",\"fecha\":\"2026-11-13T09:40:00-06:00\"},\"lineas\"");

        var (rq, error) = AlmacenesAppRequisicionEnviada.Parsear(Datos(json));

        Assert.Null(error);
        Assert.Equal("ealmacen", rq!.AutorizadaPor);
        Assert.Equal(DateTimeOffset.Parse("2026-11-13T09:40:00-06:00"), rq.AutorizadaEn);
    }

    [Theory]
    // Un usuario más largo que la columna del ERP no se recorta en silencio: se rechaza.
    [InlineData("""{"usuario":"UUUU","fecha":"2026-11-13T09:40:00-06:00"}""", true)]
    [InlineData("""{"usuario":"ealmacen","fecha":"13/11/2026"}""", false)]
    [InlineData("""{"usuario":"ealmacen"}""", false)]
    [InlineData("""{"fecha":"2026-11-13T09:40:00-06:00"}""", false)]
    [InlineData("""{"usuario":"","fecha":"2026-11-13T09:40:00-06:00"}""", false)]
    [InlineData("\"ealmacen\"", false)]
    public void Una_autorizacion_incompleta_o_ilegible_es_ERROR(string autorizacion, bool usuarioLargo)
    {
        if (usuarioLargo) autorizacion = autorizacion.Replace("UUUU", new string('u', 101));
        var json = RqBase.Replace("\"lineas\"", $"\"autorizacion\":{autorizacion},\"lineas\"");

        var (rq, error) = AlmacenesAppRequisicionEnviada.Parsear(Datos(json));

        Assert.Null(rq);
        Assert.Contains("autorizacion", error);
    }

    [Fact]
    public void Una_fecha_requerida_con_otro_formato_es_ERROR()
    {
        var (rq, error) = AlmacenesAppRequisicionEnviada.Parsear(Datos(RqBase.Replace("2026-11-20", "20/11/2026")));

        Assert.Null(rq);
        Assert.Contains("AAAA-MM-DD", error);
    }

    [Fact]
    public void Una_requisicion_sin_proveedor_de_AW_es_ERROR()
    {
        var (rq, error) = AlmacenesAppRequisicionEnviada.Parsear(Datos(RqBase.Replace("\"claveAw\":164", "\"claveAw\":0")));

        Assert.Null(rq);
        Assert.Contains("claveAw", error);
    }

    // ── RECLAMACION_PROVEEDOR (v11 p.9): solo tenía camino bueno ────────────────────────────────

    private const string ReclamacionBase = """
        {"reclamacion":"RP-00000001","entrada":"EN-000990","compra":"OC-5521","sucursal":"CONKAL",
         "proveedor":{"claveAw":164,"nombre":"3M"},"movimiento":"MV-0006400","moneda":"PESOSMX",
         "lineas":[{"requisicion":"RQ-00001320","posicion":1,"lineaErp":"1","articuloAw":100006,
                    "ancho":2600,"alto":3600,"motivo":"Rota","piezas":2,"m2":18.72,"costoM2":100.0,
                    "grupos":[{"articuloAw":100006,"ancho":2600,"alto":3600,"lote":"L-1","piezas":2,"m2":18.72}]}]}
        """;

    [Fact]
    public async Task La_reclamacion_lleva_la_entrada_y_la_compra_al_motivo_y_sale_por_devolucion()
    {
        var (r, error) = await SalidaAsync(AlmacenesAppSalida.Reclamacion, ReclamacionBase);

        Assert.Null(error);
        var s = Assert.Single(r!.Salidas);
        Assert.Equal(TipoMovimiento.SalidaPorDevolucionAProveedor, s.Tipo);
        Assert.Contains("RP-00000001", s.Motivo);
        Assert.Contains("entrada EN-000990", s.Motivo);
        Assert.Contains("compra OC-5521", s.Motivo);
        Assert.Contains("Rota", s.Motivo);
        // El costo del mensaje es el de la compra en su moneda: el ERP sale al costo de la entrada o a su promedio.
        Assert.Null(s.CostoUnitarioMxn);
        // Va contra el proveedor, no contra un área: sin centro de costo.
        Assert.Null(s.CentroCostoId);
    }

    [Theory]
    [InlineData("\"lineas\"", "\"otras\"", "lineas")]
    [InlineData("\"piezas\":2,\"m2\":18.72,\"costoM2\"", "\"piezas\":0,\"m2\":18.72,\"costoM2\"", "mayor que cero")]
    [InlineData("\"sucursal\":\"CONKAL\"", "\"sucursal\":\"\"", "sucursal")]
    [InlineData("\"moneda\":\"PESOSMX\"", "\"moneda\":\"USD\"", "moneda no soportada")]
    public async Task Una_reclamacion_mal_armada_es_ERROR_sin_mover_nada(string de, string a, string esperado)
    {
        var (r, error) = await SalidaAsync(AlmacenesAppSalida.Reclamacion, ReclamacionBase.Replace(de, a));

        Assert.Null(r);
        Assert.Contains(esperado, error);
    }

    // ── Moneda (v11): el ERP solo lleva inventario en pesos, en todos los mensajes con costo ─────

    [Theory]
    [InlineData(AlmacenesAppSalida.EntradaManual)]
    [InlineData("SALIDA_MANUAL")]
    [InlineData(AlmacenesAppSalida.VentaAlbaran)]
    [InlineData("AJUSTE_SALIDA")]
    public async Task Ningun_mensaje_entra_con_una_moneda_que_no_sean_pesos(string tipo)
    {
        const string json = """
            {"movimiento":"MV-1","sucursal":"CONKAL","moneda":"USD","piezas":1,
             "almacenes":[{"almacenGeneral":{"tipo":"EXPORTACION","linea":"HEGLA 1"},"piezas":1,
                           "grupos":[{"articuloAw":100006,"ancho":1200,"alto":2600,"piezas":1,"m2":3.12,"costoM2":10}]}]}
            """;

        var (r, error) = await SalidaAsync(tipo, json);

        Assert.Null(r);
        Assert.Contains("moneda no soportada: USD", error);
    }

    [Fact]
    public async Task MXN_se_acepta_igual_que_PESOSMX()
    {
        const string json = """
            {"movimiento":"MV-1","sucursal":"CONKAL","moneda":"MXN","piezas":1,
             "almacenes":[{"almacenGeneral":{"tipo":"EXPORTACION","linea":"HEGLA 1"},"piezas":1,
                           "grupos":[{"articuloAw":100006,"ancho":1200,"alto":2600,"piezas":1,"m2":3.12,"costoM2":10}]}]}
            """;

        var (r, error) = await SalidaAsync("SALIDA_MANUAL", json);

        Assert.Null(error);
        Assert.Single(r!.Salidas);
    }

    // ── CAMBIO_ALMACEN (v11 p.6) ────────────────────────────────────────────────────────────────

    private const string CambioBase = """
        {"movimiento":"MV-0006420","sucursal":"CONKAL","motivo":"Se asignaron a la obra","piezas":2,
         "almacenDestino":{"id":3,"nombre":"Conkal obras","tipo":"OBRAS","linea":"HEGLA 1"},
         "almacenes":[{"almacenGeneral":{"id":2,"nombre":"Conkal exportacion","tipo":"EXPORTACION","linea":"HEGLA 1"},
                       "piezas":2,"grupos":[{"articuloAw":100006,"ancho":1200,"alto":2600,"lote":"L-1","piezas":2,"m2":6.24}]}]}
        """;

    [Fact]
    public async Task El_traslado_deja_el_origen_y_el_destino_en_el_motivo()
    {
        var (r, error) = await AlmacenesAppCambio.ManejarAsync(Datos(CambioBase), new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        var s = Assert.Single(r!.Origen);
        Assert.Equal(TipoMovimiento.TrasladoInterno, s.Tipo);
        Assert.Contains("EXPORTACION", s.Motivo);
        Assert.Contains("OBRAS", s.Motivo);
        Assert.Equal(new LlaveAlmacenApp("OBRAS", "HEGLA 1"), r.Destino);
    }

    [Fact]
    public async Task Un_traslado_al_mismo_almacen_del_que_sale_no_tiene_sentido_pero_hoy_se_acepta()
    {
        // v11 no lo prohíbe y la app no lo manda; queda documentado: si algún día llega, el par entra y sale del mismo almacén.
        var mismo = CambioBase.Replace("\"almacenDestino\":{\"id\":3,\"nombre\":\"Conkal obras\",\"tipo\":\"OBRAS\",\"linea\":\"HEGLA 1\"}",
            "\"almacenDestino\":{\"id\":2,\"nombre\":\"Conkal exportacion\",\"tipo\":\"EXPORTACION\",\"linea\":\"HEGLA 1\"}");

        var (r, error) = await AlmacenesAppCambio.ManejarAsync(Datos(mismo), new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        Assert.Equal(new LlaveAlmacenApp("EXPORTACION", "HEGLA 1"), r!.Destino);
    }

    // ── REQUISICION_REDUCIDA (v11 p.2) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task La_reducida_sin_cierre_con_faltante_solo_quita_las_piezas()
    {
        const string json = """
            {"folio":"RQ-00009002","motivo":"El proveedor no surtió todo","cierreConFaltante":false,
             "lineas":[{"posicion":1,"piezasQuitadas":4}]}
            """;
        var rqApp = new RqAppEspia();

        var (estado, _, error) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, Datos(json), rqApp, default);

        Assert.Equal(("RECIBIDO", (string?)null), (estado, error));
        Assert.DoesNotContain("cierre con faltante", rqApp.Motivo);
        Assert.Contains("El proveedor no surtió todo", rqApp.Motivo);
    }

    [Fact]
    public async Task La_reducida_con_cierre_con_faltante_lo_dice_en_el_motivo()
    {
        const string json = """
            {"folio":"RQ-00009002","motivo":"Se cierra","cierreConFaltante":true,
             "lineas":[{"posicion":1,"piezasQuitadas":4}]}
            """;
        var rqApp = new RqAppEspia();

        await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, Datos(json), rqApp, default);

        Assert.Contains("cierre con faltante", rqApp.Motivo);
    }

    [Theory]
    [InlineData("""{"folio":"","lineas":[{"posicion":1,"piezasQuitadas":4}]}""", "folio")]
    [InlineData("""{"folio":"RQ-1","lineas":[]}""", "lineas")]
    [InlineData("""{"folio":"RQ-1","lineas":[{"posicion":0,"piezasQuitadas":4}]}""", "mayores que cero")]
    [InlineData("""{"folio":"RQ-1","lineas":[{"posicion":1,"piezasQuitadas":0}]}""", "mayores que cero")]
    public async Task Una_reducida_mal_armada_es_ERROR_sin_llamar_a_Compras(string json, string esperado)
    {
        var rqApp = new RqAppEspia();

        var (estado, _, error) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, Datos(json), rqApp, default);

        Assert.Equal("ERROR", estado);
        Assert.Contains(esperado, error);
        Assert.False(rqApp.Llamado);
    }

    private sealed class RqAppEspia : IComprasCrearRqAppPort
    {
        public bool Llamado { get; private set; }
        public string Motivo { get; private set; } = "";

        public Task<AvisoRqAppResultado> CancelarAsync(string folio, string? motivo, CancellationToken ct, string? folioErp = null)
        {
            Llamado = true; Motivo = motivo ?? "";
            return Task.FromResult(new AvisoRqAppResultado("CON2026-000001", null));
        }

        public Task<AvisoRqAppResultado> ReducirAsync(string folio, IReadOnlyList<LineaQuitadaRqApp> lineas, string? motivo,
            CancellationToken ct, string? folioErp = null)
        {
            Llamado = true; Motivo = motivo ?? "";
            return Task.FromResult(new AvisoRqAppResultado("CON2026-000001", null));
        }

        public Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud solicitud, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct) =>
            Task.FromResult(new ResolverSalidaResultado(null, Guid.NewGuid(), grupos.Select(g => g.AnchoMm is null
                ? new GrupoSalidaResuelto(Guid.NewGuid(), "PZA", null, null, null)
                : new GrupoSalidaResuelto(Guid.NewGuid(), "M2", Guid.NewGuid(), g.AnchoMm, g.AltoMm, AreaHoja.M2(g.AnchoMm.Value, g.AltoMm!.Value))).ToList()));
    }

    private sealed class UsuarioServicio : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), Guid.NewGuid(), true));
    }
}
