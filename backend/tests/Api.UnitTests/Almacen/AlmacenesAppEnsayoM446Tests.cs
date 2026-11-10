using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Api.Endpoints.Almacen;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>
/// M-446 con los mensajes REALES de la base de ensayo de la app (AlmacenRQ_MP_ENSAYO, 9-nov; <c>Muestras/</c>): cada renglón lleva
/// la llave de su almacén (tipo + línea), CAMBIO_ALMACEN trae origen y destino, y REQUISICION_CANCELADA/REDUCIDA llegan a Compras
/// por su puerto con el folio de la app, el motivo y lo quitado.
/// </summary>
public sealed class AlmacenesAppEnsayoM446Tests
{
    private static readonly List<JsonElement> Muestras = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, "Almacen", "Muestras", "alm_mensaje_ensayo_2026-11-09.jsonl"))
        .Where(l => l.Trim().Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();

    private static IEnumerable<JsonElement> DeTipo(string tipo) => Muestras.Where(m => m.GetProperty("tipo").GetString() == tipo);

    [Fact]
    public void Las_23_muestras_tienen_sobre_valido()
    {
        Assert.Equal(23, Muestras.Count);
        Assert.All(Muestras, m => Assert.Null(AlmacenesAppProcesador.ErrorDeSobre(m)));
    }

    [Fact]
    public async Task CAMBIO_ALMACEN_real_sale_de_EXPORTACION_BYSTRONIC_y_entra_a_OBRAS_BYSTRONIC()
    {
        var cambios = DeTipo(AlmacenesAppCambio.Tipo).ToList();
        Assert.Equal(2, cambios.Count);
        foreach (var m in cambios)
        {
            var (r, error) = await AlmacenesAppCambio.ManejarAsync(m.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);

            Assert.Null(error);
            Assert.Equal(new LlaveAlmacenApp("OBRAS", "BYSTRONIC"), r!.Destino);
            var s = Assert.Single(r.Origen);
            Assert.Equal(TipoMovimiento.TrasladoInterno, s.Tipo);
            Assert.Equal(new LlaveAlmacenApp("EXPORTACION", "BYSTRONIC"), s.Almacen);
            Assert.Equal(2 * AreaHoja.M2(1800m, 2100m), s.Cantidad);
            Assert.Null(s.CostoUnitarioMxn); // al promedio del origen
            Assert.StartsWith("zz_arnes_CA", s.Lote);
            Assert.Contains("EXPORTACION BYSTRONIC → OBRAS BYSTRONIC", s.Motivo);
        }
    }

    [Fact]
    public void REQUISICION_ENVIADA_real_con_medidas_3302_punto_0_se_acepta()
    {
        // La app manda ancho y alto como número con decimales (1200.00 × 2600.00); antes se rechazaba «deben ser enteros».
        var envios = DeTipo("REQUISICION_ENVIADA").ToList();
        Assert.Equal(4, envios.Count);
        foreach (var m in envios)
        {
            var (solicitud, error) = AlmacenesAppRequisicionEnviada.Parsear(m.GetProperty("datos"));
            Assert.Null(error);
            Assert.NotNull(solicitud);
        }
    }

    [Fact]
    public void REQUISICION_ENVIADA_con_medida_fraccionaria_se_rechaza()
    {
        var datos = JsonDocument.Parse(DeTipo("REQUISICION_ENVIADA").First(m => m.GetRawText().Contains("\"ancho\":1200"))
            .GetProperty("datos").GetRawText().Replace("\"ancho\":1200.00", "\"ancho\":1200.50")).RootElement;
        var (solicitud, error) = AlmacenesAppRequisicionEnviada.Parsear(datos);
        Assert.Null(solicitud);
        Assert.Contains("enteros", error);
    }

    [Fact]
    public async Task CAMBIO_ALMACEN_sin_destino_da_ERROR()
    {
        var datos = JsonDocument.Parse(DeTipo(AlmacenesAppCambio.Tipo).First().GetProperty("datos").GetRawText()
            .Replace("\"almacenDestino\"", "\"otro\"")).RootElement;
        var (r, error) = await AlmacenesAppCambio.ManejarAsync(datos, new Resolver(), new UsuarioServicio(), default);
        Assert.Null(r);
        Assert.Contains("almacenDestino", error);
    }

    [Fact]
    public async Task SALIDA_MANUAL_y_AJUSTE_SALIDA_reales_llevan_la_llave_y_el_lote_de_cada_grupo()
    {
        foreach (var m in DeTipo("SALIDA_MANUAL").Concat(DeTipo("AJUSTE_SALIDA")))
        {
            var tipo = m.GetProperty("tipo").GetString()!;
            var (r, error) = await AlmacenesAppSalida.ManejarAsync(tipo, m.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);
            Assert.Null(error);
            Assert.All(r!.Salidas, s =>
            {
                Assert.NotNull(s.Almacen);
                Assert.False(string.IsNullOrEmpty(s.Lote));
            });
        }
        // MV-0014377: dos grupos en EXPORTACION BYSTRONIC y uno en OBRAS BYSTRONIC.
        var doc = DeTipo("SALIDA_MANUAL").First(m => m.GetProperty("referencia").GetString() == "MV-0014377");
        var (salida, _) = await AlmacenesAppSalida.ManejarAsync("SALIDA_MANUAL", doc.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);
        Assert.Equal(["EXPORTACION BYSTRONIC", "EXPORTACION BYSTRONIC", "OBRAS BYSTRONIC"], salida!.Salidas.Select(s => s.Almacen!.ToString()));
        // AJUSTE_SALIDA MV-0014355: línea HEGLA 1.
        var ajuste = DeTipo("AJUSTE_SALIDA").Single();
        var (a, _) = await AlmacenesAppSalida.ManejarAsync("AJUSTE_SALIDA", ajuste.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);
        Assert.All(a!.Salidas, s => Assert.Equal(new LlaveAlmacenApp("EXPORTACION", "HEGLA 1"), s.Almacen));
    }

    [Fact]
    public async Task SALIDA_MANUAL_v9_valua_al_promedio_del_ERP_acepta_PESOSMX_y_guarda_motivoClave()
    {
        // M-447 (contrato v9): el costoM2 de A+W es informativo; sin costo, SalidaAppAlmacenes toma el promedio del saldo.
        var datos = JsonDocument.Parse("""
            {"sucursal":"CONKAL","movimiento":"MV-1","motivo":"Faltante de inventario","motivoClave":"FALTANTE","moneda":"PESOSMX",
             "almacenes":[{"almacenGeneral":{"tipo":"EXPORTACION","linea":"HEGLA 1"},
               "grupos":[{"articuloAw":"6MM CLARO","ancho":3210,"alto":2600,"piezas":3,"m2":25.038,"costoM2":215.50}]}]}
            """).RootElement;
        var (r, error) = await AlmacenesAppSalida.ManejarAsync("SALIDA_MANUAL", datos, new Resolver(), new UsuarioServicio(), default);
        Assert.Null(error);
        var s = Assert.Single(r!.Salidas);
        Assert.Null(s.CostoUnitarioMxn);
        Assert.EndsWith("Faltante de inventario [FALTANTE]", s.Motivo);

        var usd = JsonDocument.Parse(datos.GetRawText().Replace("PESOSMX", "USD")).RootElement;
        var (r2, error2) = await AlmacenesAppSalida.ManejarAsync("SALIDA_MANUAL", usd, new Resolver(), new UsuarioServicio(), default);
        Assert.Null(r2);
        Assert.Contains("moneda no soportada", error2);
    }

    [Fact]
    public async Task ENTRADA_ALMACEN_real_de_herraje_lleva_la_llave_sin_linea()
    {
        var herraje = DeTipo(AlmacenesAppEntrada.Tipo).Single(m => m.GetProperty("referencia").GetString() == "EN-002035");
        var (r, error) = await AlmacenesAppEntrada.ManejarAsync(herraje.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);
        Assert.Null(error);
        Assert.Equal(new LlaveAlmacenApp("EXPORTACION", ""), Assert.Single(r!.Entradas).Almacen);
    }

    [Fact]
    public async Task REQUISICION_CANCELADA_reales_cancelan_por_folio_de_la_app_con_el_motivo_y_la_entrada()
    {
        foreach (var m in DeTipo(AlmacenesAppRequisicionEnviada.Cancelada))
        {
            var rq = new Rq();
            var (estado, folio, error) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
                AlmacenesAppRequisicionEnviada.Cancelada, m.GetProperty("datos"), rq, default);

            Assert.Equal(("RECIBIDO", "CNK2026-000001", (string?)null), (estado, folio, error));
            var d = m.GetProperty("datos");
            Assert.Equal(d.GetProperty("folio").GetString(), rq.Cancelada);
            Assert.Contains($"entrada {d.GetProperty("entradaCancelada").GetString()} cancelada", rq.Motivo);
            Assert.Contains($"compra {d.GetProperty("compra").GetString()}", rq.Motivo);
        }
    }

    [Fact]
    public async Task REQUISICION_CANCELADA_sin_compra_v8_tambien_se_acepta()
    {
        var datos = JsonDocument.Parse("""{"folio":"RQ-00002600","sucursal":"CONKAL","folioErp":null,"motivo":"El ERP la rechazó"}""").RootElement;
        var rq = new Rq();
        var (estado, _, _) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(AlmacenesAppRequisicionEnviada.Cancelada, datos, rq, default);
        Assert.Equal("RECIBIDO", estado);
        Assert.Equal("El ERP la rechazó", rq.Motivo);
    }

    [Fact]
    public async Task REQUISICION_REDUCIDA_real_quita_4_piezas_de_la_posicion_1()
    {
        var m = DeTipo(AlmacenesAppRequisicionEnviada.Reducida).Single();
        var rq = new Rq();
        var (estado, _, _) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, m.GetProperty("datos"), rq, default);

        Assert.Equal("RECIBIDO", estado);
        Assert.Equal("RQ-00002532", rq.Reducida);
        Assert.Equal(new LineaQuitadaRqApp(1, 4), Assert.Single(rq.Quitadas!));
        Assert.Contains("zz_arnes llego roto", rq.Motivo);
    }

    [Theory]
    [InlineData("""{"folio":"RQ-1","lineas":[]}""", "lineas")]
    [InlineData("""{"folio":"RQ-1","lineas":[{"posicion":1,"piezasQuitadas":0}]}""", "piezasQuitadas")]
    [InlineData("""{"lineas":[{"posicion":1,"piezasQuitadas":2}]}""", "datos.folio")]
    public async Task REQUISICION_REDUCIDA_invalida_da_ERROR_sin_llamar_a_Compras(string json, string contiene)
    {
        var rq = new Rq();
        var (estado, _, error) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, JsonDocument.Parse(json).RootElement, rq, default);
        Assert.Equal("ERROR", estado);
        Assert.Contains(contiene, error);
        Assert.Null(rq.Reducida);
    }

    [Fact]
    public async Task El_error_de_Compras_se_acusa_ERROR()
    {
        var m = DeTipo(AlmacenesAppRequisicionEnviada.Reducida).Single();
        var (estado, _, error) = await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(
            AlmacenesAppRequisicionEnviada.Reducida, m.GetProperty("datos"), new Rq("La RQ no tiene la posición 1."), default);
        Assert.Equal(("ERROR", "La RQ no tiene la posición 1."), (estado, error));
    }

    [Fact]
    public void CAMBIO_ALMACEN_en_ERROR_se_reevalua_y_se_ve_en_la_bandeja()
    {
        Assert.Contains(AlmacenesAppCambio.Tipo, AlmacenesAppProcesador.TiposReevaluables);
    }

    // ── M-283 (contrato v9): ENTRADA_MANUAL y RECLAMACION_PROVEEDOR ──────────────────────────────

    [Fact]
    public async Task ENTRADA_MANUAL_reales_se_parsean_como_ajuste_positivo_con_el_costo_de_AW_de_respaldo()
    {
        var entradas = DeTipo(AlmacenesAppSalida.EntradaManual).ToList();
        Assert.Equal(5, entradas.Count);
        foreach (var m in entradas)
        {
            var (r, error) = await AlmacenesAppSalida.ManejarAsync(AlmacenesAppSalida.EntradaManual, m.GetProperty("datos"),
                new Resolver(), new UsuarioServicio(), default);
            Assert.Null(error);
            Assert.All(r!.Salidas, s =>
            {
                Assert.Equal(TipoMovimiento.AjustePositivo, s.Tipo);
                Assert.Equal(new LlaveAlmacenApp("EXPORTACION", "BYSTRONIC"), s.Almacen);
                Assert.Equal(130m, s.CostoUnitarioMxn); // costoM2 de A+W: respaldo si el ERP no tiene promedio
                Assert.False(string.IsNullOrEmpty(s.Lote));
            });
        }
        // MV-0014379: clave de arnés fuera del catálogo; se guarda tal cual, sin rechazarla.
        var arnes = entradas.Single(m => m.GetProperty("referencia").GetString() == "MV-0014379");
        var (a, _) = await AlmacenesAppSalida.ManejarAsync(AlmacenesAppSalida.EntradaManual, arnes.GetProperty("datos"),
            new Resolver(), new UsuarioServicio(), default);
        Assert.Equal(2, a!.Salidas.Count);
        Assert.EndsWith("[ZZ_ARNES_DOC_SOBRANTE]", a.Salidas[0].Motivo);
    }

    [Fact]
    public async Task ENTRADA_MANUAL_de_hoja_costeada_por_pieza_pasa_el_costo_a_m2()
    {
        var datos = JsonDocument.Parse("""
            {"sucursal":"CONKAL","movimiento":"MV-9","motivo":"Sobrante","motivoClave":"SOBRANTE","moneda":"PESOSMX",
             "almacenes":[{"almacenGeneral":{"tipo":"EXPORTACION","linea":"HEGLA 1"},
               "grupos":[{"articuloAw":100006,"ancho":2600,"alto":3210,"lote":"L-1","piezas":1,"m2":8.346,"costoPieza":834.6}]},
              {"almacenGeneral":{"tipo":"EXPORTACION","linea":null},
               "grupos":[{"articuloAw":300120,"ancho":null,"alto":null,"piezas":20,"m2":null,"costoPieza":18.5}]}]}
            """).RootElement;
        var (r, error) = await AlmacenesAppSalida.ManejarAsync(AlmacenesAppSalida.EntradaManual, datos, new Resolver(), new UsuarioServicio(), default);
        Assert.Null(error);
        Assert.Equal(834.6m / AreaHoja.M2(2600m, 3210m), r!.Salidas[0].CostoUnitarioMxn);
        Assert.Equal(18.5m, r.Salidas[1].CostoUnitarioMxn);
    }

    [Fact]
    public async Task RECLAMACION_PROVEEDOR_real_sale_por_devolucion_al_proveedor_con_lote_y_sin_costo_del_mensaje()
    {
        var m = DeTipo(AlmacenesAppSalida.Reclamacion).Single();
        var (r, error) = await AlmacenesAppSalida.ManejarAsync(AlmacenesAppSalida.Reclamacion, m.GetProperty("datos"),
            new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        Assert.Equal(2, r!.Salidas.Count); // «Rota» 2 hojas y «No llegó» 1 hoja de 5182 × 3302
        Assert.Equal([2 * AreaHoja.M2(5182m, 3302m), AreaHoja.M2(5182m, 3302m)], r.Salidas.Select(s => s.Cantidad));
        Assert.All(r.Salidas, s =>
        {
            Assert.Equal(TipoMovimiento.SalidaPorDevolucionAProveedor, s.Tipo);
            Assert.Null(s.CostoUnitarioMxn); // el de la compra va en su moneda: el ERP no lo usa
            Assert.Null(s.Almacen);          // sin almacén: se reparte en la sucursal, primero el lote
            Assert.Equal("zz_arnes", s.Lote);
            Assert.Equal("salida_manual", s.Causa);
            Assert.Equal("App Almacenes RECLAMACION_PROVEEDOR MV-0014351 RP-00000105 entrada EN-002036 compra zz_arnes_RP1: Rota, No llegó", s.Motivo);
        });
    }

    [Fact]
    public void ENTRADA_MANUAL_y_RECLAMACION_en_ERROR_se_reevaluan_al_reenviarse()
    {
        Assert.Contains(AlmacenesAppSalida.EntradaManual, AlmacenesAppProcesador.TiposReevaluables);
        Assert.Contains(AlmacenesAppSalida.Reclamacion, AlmacenesAppProcesador.TiposReevaluables);
    }

    private sealed class Rq(string? error = null) : IComprasCrearRqAppPort
    {
        public string? Cancelada, Reducida, Motivo;
        public IReadOnlyList<LineaQuitadaRqApp>? Quitadas;
        public Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<AvisoRqAppResultado> CancelarAsync(string folioExterno, string? motivo, CancellationToken ct)
        {
            (Cancelada, Motivo) = (folioExterno, motivo);
            return Task.FromResult(new AvisoRqAppResultado("CNK2026-000001", error));
        }
        public Task<AvisoRqAppResultado> ReducirAsync(string folioExterno, IReadOnlyList<LineaQuitadaRqApp> lineas, string? motivo, CancellationToken ct)
        {
            (Reducida, Quitadas, Motivo) = (folioExterno, lineas, motivo);
            return Task.FromResult(new AvisoRqAppResultado("CNK2026-000001", error));
        }
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
