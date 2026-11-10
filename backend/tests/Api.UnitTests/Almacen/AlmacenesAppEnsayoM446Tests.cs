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
