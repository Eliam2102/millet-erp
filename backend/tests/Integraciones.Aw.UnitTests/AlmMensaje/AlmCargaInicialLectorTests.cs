using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

namespace Millet.Integraciones.Aw.UnitTests.AlmMensaje;

/// <summary>
/// M-283 (contrato v9 §4): carga inicial de existencias desde <c>alm_carga_inicial</c> contra el origen simulado. Solo la carga de
/// número más alto, acuse APLICADO / ERROR por renglón, una falla no acusa y otra carga ya aplicada frena la nueva.
/// </summary>
public sealed class AlmCargaInicialLectorTests
{
    private sealed class Procesador : IProcesadorMensajeApp
    {
        public List<JsonElement> Sobres { get; } = [];
        public HashSet<string> Rechazar { get; } = [];
        public HashSet<string> Lanzar { get; } = [];
        public HashSet<string> Duplicados { get; } = [];

        public Task<AcuseApp> ProcesarAsync(JsonElement sobre, CancellationToken ct)
        {
            var id = sobre.GetProperty("mensajeId").GetString()!;
            if (Lanzar.Contains(id)) throw new InvalidOperationException("falla del ERP");
            Sobres.Add(sobre.Clone());
            return Task.FromResult(Rechazar.Contains(id) ? new AcuseApp(id, "ERROR", $"REV-{id}", "No existe la medida 1x1")
                : Duplicados.Contains(id) ? new AcuseApp(id, "DUPLICADO", "M-AJP-1", null)
                : new AcuseApp(id, "RECIBIDO", "M-AJP-1", null));
        }
    }

    private static (SimuladoAlmCargaInicialOrigen Origen, Procesador Proc, AlmCargaInicialLector Lector) Crear()
    {
        var proc = new Procesador();
        var origen = new SimuladoAlmCargaInicialOrigen();
        var sp = new ServiceCollection().AddSingleton<IProcesadorMensajeApp>(proc).BuildServiceProvider();
        var lector = new AlmCargaInicialLector(origen, sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AlmMensajeOptions { CargaInicialHabilitada = true }), NullLogger<AlmCargaInicialLector>.Instance);
        return (origen, proc, lector);
    }

    private static AlmCargaInicialRenglon Hoja(int carga, int renglon) => new(carga, renglon, "CONKAL", "EXPORTACION", "HEGLA 1",
        100006, 501, 3302m, 6100m, "L-1", null, "E-7", 3, 60.4266m, 130m, null, "PESOSMX", new DateTime(2026, 11, 12, 18, 0, 0));

    private static AlmCargaInicialRenglon Herraje(int carga, int renglon) => new(carga, renglon, "CONKAL", "GENERAL", null,
        300120, 7812, null, null, null, null, null, 20, null, null, 18.5m, "PESOSMX", null);

    [Fact]
    public async Task Aplica_solo_la_carga_mas_alta_y_acusa_APLICADO_o_ERROR_por_renglon()
    {
        var (origen, proc, lector) = Crear();
        origen.Agregar(Hoja(1, 1));
        origen.Agregar(Hoja(2, 1));
        origen.Agregar(Herraje(2, 2));
        origen.Agregar(Hoja(2, 3));
        proc.Rechazar.Add("carga-inicial:2:2");
        proc.Duplicados.Add("carga-inicial:2:3"); // ya aplicado antes de perder el acuse

        var r = await lector.ProcesarAsync(default);

        Assert.Equal((2, 1), r);
        Assert.Equal(["carga-inicial:2:1", "carga-inicial:2:2", "carga-inicial:2:3"],
            proc.Sobres.Select(s => s.GetProperty("mensajeId").GetString()));
        Assert.Equal(("APLICADO", (string?)null), origen.Acuse(2, 1));
        Assert.Equal(("ERROR", "No existe la medida 1x1"), origen.Acuse(2, 2));
        Assert.Equal(("APLICADO", (string?)null), origen.Acuse(2, 3));
        Assert.Equal((null, null), origen.Acuse(1, 1)); // la carga 1 quedó sustituida por la 2
        Assert.Equal((0, 0), await lector.ProcesarAsync(default)); // nada pendiente
    }

    [Fact]
    public async Task El_renglon_va_como_ENTRADA_MANUAL_con_su_llave_lote_y_costo()
    {
        var (origen, proc, lector) = Crear();
        origen.Agregar(Hoja(5, 7));
        origen.Agregar(Herraje(5, 8));
        await lector.ProcesarAsync(default);

        var hoja = proc.Sobres[0];
        Assert.Equal("ENTRADA_MANUAL", hoja.GetProperty("tipo").GetString());
        var d = hoja.GetProperty("datos");
        Assert.Equal(("CONKAL", "CARGA_INICIAL"), (d.GetProperty("sucursal").GetString(), d.GetProperty("motivoClave").GetString()));
        var alm = d.GetProperty("almacenes")[0];
        Assert.Equal(("EXPORTACION", "HEGLA 1"), (alm.GetProperty("almacenGeneral").GetProperty("tipo").GetString(),
            alm.GetProperty("almacenGeneral").GetProperty("linea").GetString()));
        var g = alm.GetProperty("grupos")[0];
        Assert.Equal(("L-1", 3, 60.4266m, 130m), (g.GetProperty("lote").GetString(), g.GetProperty("piezas").GetInt32(),
            g.GetProperty("m2").GetDecimal(), g.GetProperty("costoM2").GetDecimal()));

        var herraje = proc.Sobres[1].GetProperty("datos").GetProperty("almacenes")[0];
        Assert.Equal(JsonValueKind.Null, herraje.GetProperty("almacenGeneral").GetProperty("linea").ValueKind);
        Assert.Equal(18.5m, herraje.GetProperty("grupos")[0].GetProperty("costoPieza").GetDecimal());
        Assert.True(AlmCargaInicialLector.EsCargaInicial(proc.Sobres[1].GetProperty("mensajeId").GetString()!));
    }

    [Fact]
    public async Task Una_falla_del_ERP_no_acusa_y_se_reintenta()
    {
        var (origen, proc, lector) = Crear();
        origen.Agregar(Hoja(1, 1));
        origen.Agregar(Hoja(1, 2));
        proc.Lanzar.Add("carga-inicial:1:1");

        Assert.Equal((1, 0), await lector.ProcesarAsync(default));
        Assert.Equal((null, null), origen.Acuse(1, 1));

        proc.Lanzar.Clear();
        Assert.Equal((1, 0), await lector.ProcesarAsync(default));
        Assert.Equal(("APLICADO", (string?)null), origen.Acuse(1, 1));
    }

    [Fact]
    public async Task Con_otra_carga_ya_aplicada_la_nueva_no_se_aplica()
    {
        var (origen, proc, lector) = Crear();
        origen.Agregar(Hoja(1, 1));
        await lector.ProcesarAsync(default);
        origen.Agregar(Hoja(2, 1)); // TI regeneró la carga: aplicarla sumaría la existencia dos veces

        Assert.Equal((0, 0), await lector.ProcesarAsync(default));
        Assert.Single(proc.Sobres);
        Assert.Equal((null, null), origen.Acuse(2, 1));
    }

    [Fact]
    public async Task Sin_carga_no_hace_nada()
    {
        var (_, proc, lector) = Crear();
        Assert.Equal((0, 0), await lector.ProcesarAsync(default));
        Assert.Empty(proc.Sobres);
    }
}
