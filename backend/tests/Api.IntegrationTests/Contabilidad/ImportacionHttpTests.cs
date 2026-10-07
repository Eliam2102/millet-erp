using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Fix = Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>Importación (vista previa, perfilado, aplicar) contra el API y Postgres reales (§6, §12, §20). Datos FIX-*.</summary>
public class ImportacionHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cab = "codigo;nombre;naturaleza;tipo_cuenta;codigo_origen\n";

    private static ImportacionRequest Req(string csv, string suf, string? huella = null) => Fix.Request(csv, $"FIX-{suf}-F", huella);

    /// <summary>1 título + 2 afectables (el padre se deduce por segmentos del código).</summary>
    private static string Feliz(string suf) => Cab
        + $"{Codigo(suf, "100.00.00.00")};FIX Raiz;Deudora;Titulo;O-1\n"
        + $"{Codigo(suf, "100.10.00.00")};FIX Hoja 1;Deudora;Afectable;O-2\n"
        + $"{Codigo(suf, "100.20.00.00")};FIX Hoja 2;Acreedora;Afectable;O-3\n";

    private static async Task<JsonElement> Post(HttpClient c, string ruta, object body, HttpStatusCode esperado)
    {
        var r = await c.PostAsJsonAsync($"{Base}{ruta}", body);
        Assert.Equal(esperado, r.StatusCode);
        return await Json(r);
    }

    private async Task<long> TotalFilasModulo()
    {
        long n = 0;
        foreach (var t in new[] { "cuentas_contables", "cuentas_contables_origen", "importaciones_catalogo", "cuentas_contables_uso" })
            n += await Contar(factory.Services, t);
        return n;
    }

    [Fact]
    public async Task Importar_1_titulo_2_afectables_crea_3_cuentas_con_origen_niveles_y_un_lote()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var vp = await Post(c, "/importaciones/vista-previa", Req(Feliz(suf), suf), HttpStatusCode.OK);
            Assert.True(vp.GetProperty("puedeAplicar").GetBoolean());
            Assert.Equal(3, vp.GetProperty("resumen").GetProperty("crear").GetInt32());

            var r = await Post(c, "/importaciones", Req(Feliz(suf), suf, vp.GetProperty("huella").GetString()), HttpStatusCode.Created);
            Assert.False(r.GetProperty("idempotente").GetBoolean());
            Assert.Equal(3, r.GetProperty("lote").GetProperty("creadas").GetInt32());

            var lista = await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&limit=50"));
            var items = lista.GetProperty("items").EnumerateArray().OrderBy(i => i.GetProperty("codigo").GetString()).ToList();
            Assert.Equal([1, 2, 2], items.Select(i => i.GetProperty("nivel").GetInt32()).ToArray());
            Assert.Equal(items[0].GetProperty("id").GetGuid(), items[1].GetProperty("padreId").GetGuid());
            Assert.Equal(3, await Contar(factory.Services, "cuentas_contables_origen", $"fuente = 'FIX-{suf}-F'"));
            Assert.Equal(1, await Contar(factory.Services, "importaciones_catalogo", $"fuente = 'FIX-{suf}-F'"));
            var detalle = await Obtener(c, items[1].GetProperty("id").GetGuid());
            Assert.Equal("O-2", detalle.GetProperty("origenes")[0].GetProperty("codigoOrigen").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Reimportar_el_mismo_archivo_no_duplica_y_con_una_fila_nueva_da_SinCambios_en_las_demas()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            await Post(c, "/importaciones", Req(Feliz(suf), suf), HttpStatusCode.Created);
            var antes = await TotalFilasModulo();

            var again = await Post(c, "/importaciones", Req(Feliz(suf), suf), HttpStatusCode.OK);
            Assert.True(again.GetProperty("idempotente").GetBoolean());
            Assert.Equal(antes, await TotalFilasModulo());

            var mas = Feliz(suf) + $"{Codigo(suf, "100.30.00.00")};FIX Hoja 3;Deudora;Afectable;O-4\n";
            var r = await Post(c, "/importaciones", Req(mas, suf), HttpStatusCode.Created);
            Assert.Equal(1, r.GetProperty("lote").GetProperty("creadas").GetInt32());
            Assert.Equal(3, r.GetProperty("lote").GetProperty("sinCambios").GetInt32());
            Assert.Equal(4, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Huella_distinta_a_la_de_la_vista_previa_es_409()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var r = await c.PostAsJsonAsync($"{Base}/importaciones", Req(Feliz(suf), suf, new string('a', 64)));
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("CONTAB_IMPORT_HUELLA_NO_COINCIDE", await Code(r));
            Assert.Equal(0, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Archivo_con_errores_es_422_con_errores_por_fila_y_no_escribe_nada_ni_siquiera_las_filas_validas()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            // fila 4: naturaleza desconocida; filas 2 y 3 son válidas.
            var csv = Cab
                + $"{Codigo(suf, "100.00.00.00")};R;Deudora;Titulo;O-1\n"
                + $"{Codigo(suf, "100.10.00.00")};H;Deudora;Afectable;O-2\n"
                + $"{Codigo(suf, "100.20.00.00")};H2;Inventada;Afectable;O-3\n";
            var antes = await TotalFilasModulo();
            var r = await c.PostAsJsonAsync($"{Base}/importaciones", Req(csv, suf));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var p = await Json(r);
            Assert.Equal("CONTAB_IMPORT_FILAS_CON_ERRORES", p.GetProperty("code").GetString());
            var e = p.GetProperty("errores").EnumerateArray().Single();
            Assert.Equal(4, e.GetProperty("fila").GetInt32());
            Assert.Equal("naturaleza", e.GetProperty("columna").GetString());
            Assert.Equal("CONTAB_IMPORT_NATURALEZA_DESCONOCIDA", e.GetProperty("codigo").GetString());
            Assert.Equal("Error", e.GetProperty("severidad").GetString());
            var sug = e.GetProperty("sugerencia").GetString();
            Assert.Contains("Deudora", sug);
            Assert.DoesNotContain("Aliases", sug);
            Assert.Equal(antes, await TotalFilasModulo());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Jerarquia_ciclica_y_codigo_repetido_en_el_archivo_rechazan_el_lote_completo()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var a = Codigo(suf, "1");
            var b = Codigo(suf, "2");
            var ciclo = "codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\n" + $"{a};A;{b};Deudora;Titulo\n{b};B;{a};Deudora;Titulo\n";
            var r1 = await Json(await c.PostAsJsonAsync($"{Base}/importaciones", Req(ciclo, suf)));
            Assert.Contains(r1.GetProperty("errores").EnumerateArray(), e => e.GetProperty("codigo").GetString() == "CONTAB_IMPORT_CICLO");
            var dup = "codigo;nombre\n" + $"{a};A\n{a};A otra\n";
            var r2 = await Json(await c.PostAsJsonAsync($"{Base}/importaciones", Req(dup, suf)));
            Assert.Contains(r2.GetProperty("errores").EnumerateArray(), e => e.GetProperty("codigo").GetString() == "CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO");
            Assert.Equal(0, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Vista_previa_y_perfilado_no_escriben_ninguna_tabla()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var antes = await TotalFilasModulo();
            await Post(c, "/importaciones/vista-previa", Req(Feliz(suf), suf), HttpStatusCode.OK);
            var perfil = await Post(c, "/importaciones/perfilado", Req(Fix.HojaBase().Replace("FIX-100", $"FIX-{suf}-100"), suf), HttpStatusCode.OK);
            Assert.Equal(antes, await TotalFilasModulo());
            // El perfil de la hoja base ficticia reporta lo que dicen P14–P16.
            Assert.Equal(5, perfil.GetProperty("pendientesValidacion").GetProperty("sinNaturaleza").GetInt32());
            Assert.Equal(1, perfil.GetProperty("nivelContable").GetProperty("discrepancias").GetInt32());
            Assert.Equal(1, perfil.GetProperty("estructura").GetProperty("huerfanas").GetInt32());
            Assert.Contains(perfil.GetProperty("columnasSinMapeo").EnumerateArray(), x => x.GetProperty("columna").GetString() == "tipo");
            Assert.DoesNotContain(suf, perfil.GetRawText());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Vista_previa_perfilado_y_aplicar_coinciden_sobre_el_mismo_archivo()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var csv = Cab + $"{Codigo(suf, "100.00.00.00")};R;Deudora;Titulo;O-1\n{Codigo(suf, "100.10.00.00")};H;Inventada;Afectable;O-2\n{Codigo(suf, "100.20.00.00")};H;Deudora;Afectable;O-3\n";
            var vp = await Post(c, "/importaciones/vista-previa", Req(csv, suf), HttpStatusCode.OK);
            var perfil = await Post(c, "/importaciones/perfilado", Req(csv, suf), HttpStatusCode.OK);
            Assert.False(vp.GetProperty("puedeAplicar").GetBoolean());
            Assert.Equal(vp.GetProperty("huella").GetString(), perfil.GetProperty("resumen").GetProperty("huella").GetString());
            Assert.Equal(vp.GetProperty("resumen").GetProperty("rechazadas").GetInt32(), perfil.GetProperty("resumen").GetProperty("acciones").GetProperty("Rechazar").GetInt32());
            var aplicar = await c.PostAsJsonAsync($"{Base}/importaciones", Req(csv, suf));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, aplicar.StatusCode);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Padre_despues_del_hijo_se_inserta_en_orden_y_respeta_las_llaves_foraneas()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            // 4 niveles, del más profundo al más alto.
            var csv = "codigo;nombre;naturaleza;tipo_cuenta\n"
                + $"{Codigo(suf, "100.10.20.30")};N4;Deudora;Afectable\n{Codigo(suf, "100.10.20.00")};N3;Deudora;Titulo\n"
                + $"{Codigo(suf, "100.10.00.00")};N2;Deudora;Titulo\n{Codigo(suf, "100.00.00.00")};N1;Deudora;Titulo\n";
            await Post(c, "/importaciones", Req(csv, suf), HttpStatusCode.Created);
            var niveles = await Json(await c.GetAsync($"{Base}/cuentas?q={suf}"));
            Assert.Equal([1, 2, 3, 4], niveles.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nivel").GetInt32()).Order().ToArray());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Hoja_base_provisional_importa_cuentas_pendientes_de_naturaleza_y_con_tipo_derivado()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            // La fila de ancho distinto queda huérfana (error): se importa el resto de la hoja.
            var hoja = string.Join('\n', Fix.HojaBase().Replace("FIX-100", $"FIX-{suf}-100").Split('\n').Take(5)) + "\n";
            var r = await Post(c, "/importaciones", Req(hoja, suf), HttpStatusCode.Created);
            Assert.Equal(4, r.GetProperty("lote").GetProperty("creadas").GetInt32());
            var lista = await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&pendientes=true"));
            Assert.Equal(4, lista.GetProperty("total").GetInt32());
            Assert.All(lista.GetProperty("items").EnumerateArray(), i =>
            {
                Assert.Equal(JsonValueKind.Null, i.GetProperty("naturaleza").ValueKind);
                Assert.Equal("Ninguna", i.GetProperty("cuentaControl").GetString());
            });
            // P19: el tipo se deriva de la jerarquía (raíz y nivel 2 con hijas acumulan; hojas de nivel 3, afectables).
            Assert.Equal(["Titulo", "Titulo", "Afectable", "Afectable"], lista.GetProperty("items").EnumerateArray()
                .OrderBy(i => i.GetProperty("codigo").GetString()).Select(i => i.GetProperty("tipo").GetString()!).ToArray());
            Assert.Equal("100", (await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&limit=1"))).GetProperty("items")[0].GetProperty("codigoAgrupador").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Importacion_es_upsert_no_destructivo_y_protege_cuentas_usadas()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            await Post(c, "/importaciones", Req(Feliz(suf), suf), HttpStatusCode.Created);
            var hoja1 = (await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&limit=50"))).GetProperty("items").EnumerateArray()
                .First(i => i.GetProperty("codigo").GetString() == Codigo(suf, "100.10.00.00")).GetProperty("id").GetGuid();
            using (var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId))
            using (var scope = enEmpresa.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegistrarUsoCuentaCommand(hoja1, "FIX-POLIZAS", null));

            // Lo ausente del archivo NO se desactiva; un cambio de naturaleza sobre la cuenta usada se rechaza.
            var cambia = "codigo;nombre;naturaleza;tipo_cuenta\n" + $"{Codigo(suf, "100.10.00.00")};FIX Hoja 1;Acreedora;Afectable\n";
            var rechazo = await c.PostAsJsonAsync($"{Base}/importaciones", Req(cambia, suf));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
            Assert.Contains((await Json(rechazo)).GetProperty("errores").EnumerateArray(), e => e.GetProperty("codigo").GetString() == "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO");
            Assert.Equal("Deudora", (await Obtener(c, hoja1)).GetProperty("naturaleza").GetString());

            // Solo el nombre cambia: Actualizar; el resto del catálogo sigue activo.
            var nombre = "codigo;nombre\n" + $"{Codigo(suf, "100.10.00.00")};FIX Hoja 1 renombrada\n";
            var ok = await Post(c, "/importaciones", Req(nombre, suf), HttpStatusCode.Created);
            Assert.Equal(1, ok.GetProperty("lote").GetProperty("actualizadas").GetInt32());
            var detalle = await Obtener(c, hoja1);
            Assert.Equal("FIX Hoja 1 renombrada", detalle.GetProperty("nombre").GetString());
            Assert.Equal("Deudora", detalle.GetProperty("naturaleza").GetString()); // celda ausente no borra
            Assert.Equal(3, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%' AND estatus = 0"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Dos_importaciones_simultaneas_del_mismo_archivo_dejan_un_solo_lote_y_nunca_un_500()
    {
        var suf = Sufijo();
        try
        {
            var a = await LoginAsync(factory);
            var b = await LoginAsync(factory);
            var rs = await Task.WhenAll(
                a.PostAsJsonAsync($"{Base}/importaciones", Req(Feliz(suf), suf)),
                b.PostAsJsonAsync($"{Base}/importaciones", Req(Feliz(suf), suf)));
            Assert.All(rs, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"estado inesperado {r.StatusCode}"));
            Assert.Contains(rs, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Equal(3, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
            Assert.Equal(1, await Contar(factory.Services, "importaciones_catalogo", $"fuente = 'FIX-{suf}-F'"));
            Assert.Equal(3, await Contar(factory.Services, "cuentas_contables_origen", $"fuente = 'FIX-{suf}-F'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Fallo_a_mitad_de_la_aplicacion_revierte_todo_analisis_viejo_contra_otro_lote_ya_aplicado()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ContabilidadDbContext>();

            // Análisis de X (3 cuentas) calculado ANTES de que otro lote cree una de ellas.
            var x = Cab + $"{Codigo(suf, "100.00.00.00")};R;Deudora;Titulo;O-1\n{Codigo(suf, "100.10.00.00")};H1;Deudora;Afectable;O-2\n{Codigo(suf, "100.20.00.00")};H2;Deudora;Afectable;O-3\n";
            var peticionX = Req(x, suf);
            var (analisisX, _) = await AnalisisImportacion.EjecutarAsync(db, sp.GetRequiredService<FormatoCatalogo>(), peticionX, default);
            Assert.True(analisisX.PuedeAplicar);
            Assert.All(analisisX.Filas, f => Assert.Equal(Accion.Crear, f.Accion));

            // Otro lote (archivo distinto) crea la raíz con otro origen y se confirma.
            var y = Cab + $"{Codigo(suf, "100.00.00.00")};R;Deudora;Titulo;O-9\n";
            await Post(c, "/importaciones", Req(y, suf), HttpStatusCode.Created);

            // Persistir el análisis viejo de X choca con el índice único del código de la raíz: el SaveChanges
            // ÚNICO falla y NO debe quedar H1, H2, sus orígenes ni el lote de X (rollback total).
            var handler = ActivatorUtilities.CreateInstance<AplicarImportacionHandler>(sp);
            var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.PersistirAsync(analisisX, peticionX, default));
            Assert.Equal("CONTAB_IMPORT_CONFLICTO_CONCURRENTE", ex.Code);
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables_origen", $"fuente = 'FIX-{suf}-F'"));
            Assert.Equal(1, await Contar(factory.Services, "importaciones_catalogo", $"fuente = 'FIX-{suf}-F'"));
            Assert.Equal(0, await Contar(factory.Services, "importaciones_catalogo", $"huella_sha256 = '{analisisX.Huella}'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Volumen_5000_filas_se_aplica_y_5001_se_rechaza_por_limite_con_tiempos_dentro_de_presupuesto()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var ok = Req(Fix.Volumen(5000, $"FIX-{suf}"), suf);

            var sw = Stopwatch.StartNew();
            var vp = await Post(c, "/importaciones/vista-previa", ok, HttpStatusCode.OK);
            var tVista = sw.Elapsed;
            Assert.True(vp.GetProperty("puedeAplicar").GetBoolean());
            Assert.Equal(5000, vp.GetProperty("resumen").GetProperty("crear").GetInt32());

            sw.Restart();
            await Post(c, "/importaciones/perfilado", ok, HttpStatusCode.OK);
            var tPerfil = sw.Elapsed;

            sw.Restart();
            var r = await Post(c, "/importaciones", ok, HttpStatusCode.Created);
            var tAplicar = sw.Elapsed;
            Assert.Equal(5000, r.GetProperty("lote").GetProperty("creadas").GetInt32());
            Assert.Equal(5000, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
            Console.WriteLine($"TIEMPOS 5000 filas: vista previa {tVista.TotalSeconds:F1}s, perfilado {tPerfil.TotalSeconds:F1}s, aplicar {tAplicar.TotalSeconds:F1}s");
            Assert.True(tVista < TimeSpan.FromSeconds(30) && tPerfil < TimeSpan.FromSeconds(30) && tAplicar < TimeSpan.FromSeconds(60),
                $"presupuesto excedido: {tVista}, {tPerfil}, {tAplicar}");

            var de = await c.PostAsJsonAsync($"{Base}/importaciones", Req(Fix.Volumen(5001, $"FIX-{suf}"), suf));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, de.StatusCode);
            Assert.Equal("CONTAB_IMPORT_LIMITE_FILAS", await Code(de));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Cuentas_de_control_de_la_configuracion_marcan_la_cuenta_al_importar()
    {
        var suf = Sufijo();
        try
        {
            using var otra = factory.ConEmpresa(null,
                ("Contabilidad:Catalogo:CuentasControl:0:Codigo", Codigo(suf, "100.10.00.00")),
                ("Contabilidad:Catalogo:CuentasControl:0:Tipo", "Clientes"));
            var c = await LoginAsync(otra);
            await Post(c, "/importaciones", Req(Feliz(suf), suf), HttpStatusCode.Created);
            var hoja = (await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&limit=50"))).GetProperty("items").EnumerateArray()
                .First(i => i.GetProperty("codigo").GetString() == Codigo(suf, "100.10.00.00"));
            Assert.Equal("Clientes", hoja.GetProperty("cuentaControl").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }
}
