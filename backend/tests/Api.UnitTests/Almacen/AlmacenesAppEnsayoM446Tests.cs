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
    public void REQUISICION_ENVIADA_con_medida_sin_redondear_se_acepta_y_se_redondea_al_mm()
    {
        // v9: «medidas en milímetros, sin redondear». La línea de la RQ guarda mm enteros: 1200.5 → 1201, 2600.4 → 2600.
        var datos = JsonDocument.Parse(DeTipo("REQUISICION_ENVIADA").First().GetProperty("datos").GetRawText()
            .Replace("\"ancho\":1200.0", "\"ancho\":1200.5").Replace("\"alto\":2600.0", "\"alto\":2600.4")).RootElement;
        var (solicitud, error) = AlmacenesAppRequisicionEnviada.Parsear(datos);
        Assert.Null(error);
        var l = Assert.Single(solicitud!.Lineas);
        Assert.Equal((1201, 2600), (l.AnchoMm, l.AltoMm));
    }

    [Fact]
    public void REQUISICION_ENVIADA_lleva_la_posicion_de_la_app_y_obraAw_si_no_hay_obra()
    {
        var datos = JsonDocument.Parse("""
            {"folio":"RQ-9","sucursal":"CONKAL","proveedor":{"claveAw":164},"lineas":[
              {"posicion":3,"articuloAw":100006,"ancho":3302,"alto":6100,"piezas":2,"obraAw":7781,"obra":null},
              {"posicion":1,"articuloAw":920035,"ancho":null,"alto":null,"piezas":4,"obraAw":null,"obra":"Torre A"}]}
            """).RootElement;
        var (solicitud, error) = AlmacenesAppRequisicionEnviada.Parsear(datos);
        Assert.Null(error);
        Assert.Equal([(3, "7781"), (1, "Torre A")], solicitud!.Lineas.Select(l => (l.Posicion!.Value, l.Obra!)));

        var repetida = JsonDocument.Parse(datos.GetRawText().Replace("\"posicion\":1", "\"posicion\":3")).RootElement;
        Assert.Contains("posicion", AlmacenesAppRequisicionEnviada.Parsear(repetida).Item2);
        var aMedias = JsonDocument.Parse(datos.GetRawText().Replace("\"posicion\":1,", "")).RootElement;
        Assert.Contains("posicion", AlmacenesAppRequisicionEnviada.Parsear(aMedias).Item2);
    }

    [Fact]
    public async Task REQUISICION_CANCELADA_y_REDUCIDA_buscan_por_folioErp_si_viene()
    {
        var datos = JsonDocument.Parse("""{"folio":"RQ-00002600","sucursal":"CONKAL","folioErp":"CNK2026-000045","motivo":"x","lineas":[{"posicion":2,"piezasQuitadas":1}]}""").RootElement;
        var rq = new Rq();
        await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(AlmacenesAppRequisicionEnviada.Cancelada, datos, rq, default);
        Assert.Equal(("RQ-00002600", "CNK2026-000045"), (rq.Cancelada, rq.FolioErp));
        rq = new Rq();
        await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(AlmacenesAppRequisicionEnviada.Reducida, datos, rq, default);
        Assert.Equal(("RQ-00002600", "CNK2026-000045"), (rq.Reducida, rq.FolioErp));
        // Las muestras reales traen folioErp null: se busca por el folio de la app.
        rq = new Rq();
        await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(AlmacenesAppRequisicionEnviada.Reducida,
            DeTipo(AlmacenesAppRequisicionEnviada.Reducida).Single().GetProperty("datos"), rq, default);
        Assert.Null(rq.FolioErp);
    }

    [Fact]
    public async Task AJUSTE_SALIDA_real_lee_mesa_y_jobid_de_los_renglones_y_pedidoAw_numerico()
    {
        var m = DeTipo("AJUSTE_SALIDA").Single();
        var (r, error) = await AlmacenesAppSalida.ManejarAsync("AJUSTE_SALIDA", m.GetProperty("datos"), new Resolver(), new UsuarioServicio(), default);
        Assert.Null(error);
        Assert.All(r!.Salidas, s => Assert.Equal(
            "App Almacenes AJUSTE_SALIDA MV-0014355 documento previo CP-00000165 mesa HG1 job 777000001 pedido 501", s.Motivo));
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
    public async Task El_origen_v9_se_lee_del_regreso_de_prestamo_la_cancelacion_y_la_reclamacion()
    {
        async Task<AlmacenesAppSalida.Resuelta> Parsear(string tipo, JsonElement datos)
        {
            var (r, error) = await AlmacenesAppSalida.ManejarAsync(tipo, datos, new Resolver(), new UsuarioServicio(), default);
            Assert.Null(error);
            return r!;
        }
        // Regreso de préstamo real (MV-0014381): entra al costo de la salida MV-0014380.
        var regreso = DeTipo(AlmacenesAppSalida.EntradaManual).Single(m => m.GetProperty("referencia").GetString() == "MV-0014381");
        var r1 = await Parsear(AlmacenesAppSalida.EntradaManual, regreso.GetProperty("datos"));
        Assert.Equal(new OrigenApp("REGRESO_PRESTAMO", "MV-0014380"), r1.Origen);
        Assert.Contains("(origen REGRESO_PRESTAMO MV-0014380)", r1.Salidas[0].Motivo);

        // Cancelación de entrada (ejemplo del contrato v9): sale al costo de la entrada EN-000820.
        var cancelacion = JsonDocument.Parse("""
            {"movimiento":"MV-7","sucursal":"CONKAL","motivo":"Cancelación de entrada","motivoClave":"CANCELA_ENTRADA","moneda":"PESOSMX",
             "origen":{"tipo":"CANCELACION_ENTRADA","entrada":"EN-000820","compra":"OC-5521","encargoAw":36008200,"motivo":"Se capturó dos veces"},
             "almacenes":[{"almacenGeneral":{"tipo":"EXPORTACION","linea":"HEGLA 1"},
               "grupos":[{"articuloAw":100006,"ancho":2600,"alto":3210,"lote":"L-1","piezas":1,"m2":8.346,"costoM2":130}]}]}
            """).RootElement;
        Assert.Equal(new OrigenApp("CANCELACION_ENTRADA", "EN-000820"), (await Parsear("SALIDA_MANUAL", cancelacion)).Origen);

        // Devolución de venta: su salidaOriginal.
        var devolucion = JsonDocument.Parse(cancelacion.GetRawText().Replace(
            "\"origen\":{\"tipo\":\"CANCELACION_ENTRADA\",\"entrada\":\"EN-000820\"",
            "\"origen\":{\"tipo\":\"DEVOLUCION_VENTA\",\"salidaOriginal\":\"MV-0012345\"")).RootElement;
        Assert.Equal(new OrigenApp("DEVOLUCION_VENTA", "MV-0012345"), (await Parsear(AlmacenesAppSalida.EntradaManual, devolucion)).Origen);

        // Reclamación: nace de su entrada. AJUSTE_SALIDA trae origen de texto («CORTE»): no es origen v9.
        var rp = DeTipo(AlmacenesAppSalida.Reclamacion).Single();
        Assert.Equal(new OrigenApp(AlmacenesAppSalida.Reclamacion, "EN-002036"), (await Parsear(AlmacenesAppSalida.Reclamacion, rp.GetProperty("datos"))).Origen);
        Assert.Null((await Parsear("AJUSTE_SALIDA", DeTipo("AJUSTE_SALIDA").Single().GetProperty("datos"))).Origen);
        // Documento SM- sin origen.
        Assert.Null((await Parsear("SALIDA_MANUAL", DeTipo("SALIDA_MANUAL").First().GetProperty("datos"))).Origen);
    }

    [Fact]
    public void ENTRADA_MANUAL_y_RECLAMACION_en_ERROR_se_reevaluan_al_reenviarse()
    {
        Assert.Contains(AlmacenesAppSalida.EntradaManual, AlmacenesAppProcesador.TiposReevaluables);
        Assert.Contains(AlmacenesAppSalida.Reclamacion, AlmacenesAppProcesador.TiposReevaluables);
    }

    private sealed class Rq(string? error = null) : IComprasCrearRqAppPort
    {
        public string? Cancelada, Reducida, Motivo, FolioErp;
        public IReadOnlyList<LineaQuitadaRqApp>? Quitadas;
        public Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<AvisoRqAppResultado> CancelarAsync(string folioExterno, string? motivo, CancellationToken ct, string? folioErp = null)
        {
            (Cancelada, Motivo, FolioErp) = (folioExterno, motivo, folioErp);
            return Task.FromResult(new AvisoRqAppResultado("CNK2026-000001", error));
        }
        public Task<AvisoRqAppResultado> ReducirAsync(string folioExterno, IReadOnlyList<LineaQuitadaRqApp> lineas, string? motivo, CancellationToken ct,
            string? folioErp = null)
        {
            (Reducida, Quitadas, Motivo, FolioErp) = (folioExterno, lineas, motivo, folioErp);
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
