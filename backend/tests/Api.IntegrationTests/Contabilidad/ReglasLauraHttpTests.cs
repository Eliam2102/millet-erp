using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Fix = Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// Ronda «reglas de Laura» (P19–P26) contra el API y Postgres reales: conversión dinámica del padre, prueba de movimiento
/// por origen, rubros, importación del formato de Contabilidad y perfiles. Datos FIX-*.
/// </summary>
public class ReglasLauraHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private async Task RegistrarUso(Guid cuentaId)
    {
        using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        using var scope = enEmpresa.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegistrarUsoCuentaCommand(cuentaId, "FIX-POLIZAS", "FIX-REF"));
    }

    private static async Task<JsonElement> Validar(HttpClient c, string codigo, string origen) =>
        await Json(await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas/validar-movimiento", new { codigo, origen }));

    [Fact]
    public async Task Hija_bajo_afectable_sin_movimientos_convierte_al_padre_y_con_movimientos_es_422()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var raiz = await CrearCuenta(c, Codigo(suf, "1"));
            var hoja = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: Id(raiz));
            Assert.Equal("Afectable", hoja.GetProperty("tipo").GetString());

            // Sin movimientos: la hija se crea y el padre pasa a acumular.
            var nieta = await CrearCuenta(c, Codigo(suf, "1.1.1"), padreId: Id(hoja));
            Assert.Equal("Afectable", nieta.GetProperty("tipo").GetString());
            Assert.Equal(3, nieta.GetProperty("nivel").GetInt32());
            Assert.Equal("Titulo", (await Obtener(c, Id(hoja))).GetProperty("tipo").GetString());
            Assert.False((await Validar(c, Codigo(suf, "1.1"), "Manual")).GetProperty("valida").GetBoolean());

            // Con movimientos: 422 con mensaje de usuario y nada cambia.
            var usada = await CrearCuenta(c, Codigo(suf, "1.2"), padreId: Id(raiz));
            await RegistrarUso(Id(usada));
            var r = await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "1.2.1"), nombre = "FIX x", padreId = Id(usada), cuentaControl = "Ninguna" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var p = await Json(r);
            Assert.Equal("CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO", p.GetProperty("code").GetString());
            Assert.Contains("ya tiene movimientos", p.GetProperty("detail").GetString());
            Assert.Equal("Afectable", (await Obtener(c, Id(usada))).GetProperty("tipo").GetString());
            Assert.Equal(0, await Contar(factory.Services, "cuentas_contables", $"codigo = '{Codigo(suf, "1.2.1")}'"));

            // Colectiva: tampoco puede tener hijas.
            var colectiva = await CrearCuenta(c, Codigo(suf, "1.3"), padreId: Id(raiz), control: "Deudores");
            var bajoColectiva = await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "1.3.1"), nombre = "FIX x", padreId = Id(colectiva), cuentaControl = "Ninguna" });
            Assert.Equal("CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE", await Code(bajoColectiva));

            // Desactivar la última hija: el padre sigue acumulando (histórico), no vuelve a afectable.
            (await Send(c, HttpMethod.Post, $"{Base}/cuentas/{Id(nieta)}/desactivar", null, Etag(nieta))).EnsureSuccessStatusCode();
            Assert.Equal("Titulo", (await Obtener(c, Id(hoja))).GetProperty("tipo").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Validar_movimiento_rechaza_manual_en_colectiva_y_acumulativa_y_acepta_el_modulo_autorizado()
    {
        var suf = Sufijo();
        try
        {
            var admin = await LoginAsync(factory);
            var raiz = await CrearCuenta(admin, Codigo(suf, "1"));
            await CrearCuenta(admin, Codigo(suf, "1.1"), padreId: Id(raiz), control: "Deudores");
            await CrearCuenta(admin, Codigo(suf, "1.2"), padreId: Id(raiz), control: "Acreedores");
            var consulta = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer);

            Assert.Equal("ControlSoloAuxiliar", (await Validar(consulta, Codigo(suf, "1.1"), "Manual")).GetProperty("motivo").GetString());
            Assert.True((await Validar(consulta, Codigo(suf, "1.1"), "AuxiliarCxC")).GetProperty("valida").GetBoolean());
            Assert.Equal("ControlSoloAuxiliar", (await Validar(consulta, Codigo(suf, "1.2"), "AuxiliarCxC")).GetProperty("motivo").GetString());
            Assert.True((await Validar(consulta, Codigo(suf, "1.2"), "AuxiliarCxP")).GetProperty("valida").GetBoolean());
            Assert.Equal("Titulo", (await Validar(consulta, Codigo(suf, "1"), "Manual")).GetProperty("motivo").GetString());

            // Una colectiva de nivel 1 no es posible: acumula.
            var raizColectiva = await admin.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "2"), nombre = "FIX x", cuentaControl = "Clientes" });
            Assert.Equal("CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE", await Code(raizColectiva));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Rubro_manual_agrupa_raices_no_es_padre_no_esta_en_el_arbol_y_no_recibe_movimientos()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var rr = await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "R"), nombre = "FIX Rubro", cuentaControl = "Ninguna", clase = "Rubro" });
            Assert.Equal(HttpStatusCode.Created, rr.StatusCode);
            var rubro = await Json(rr);
            Assert.Equal("Rubro", rubro.GetProperty("clase").GetString());
            Assert.False(rubro.GetProperty("pendienteValidacion").GetBoolean());

            var raiz = await CrearCuenta(c, Codigo(suf, "1"));
            var edit = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{Id(raiz)}",
                new { nombre = "FIX raiz", naturaleza = "Deudora", cuentaControl = "Ninguna", rubroId = Id(rubro) }, Etag(raiz));
            edit.EnsureSuccessStatusCode();
            Assert.Equal(Id(rubro), (await Json(edit)).GetProperty("rubroId").GetGuid());

            var delRubro = await Json(await c.GetAsync($"{Base}/cuentas?rubroId={Id(rubro)}"));
            Assert.Equal(Id(raiz), delRubro.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());

            var bajoRubro = await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "R.1"), nombre = "FIX x", padreId = Id(rubro), cuentaControl = "Ninguna" });
            Assert.Equal("CONTAB_CUENTA_PADRE_INVALIDO", await Code(bajoRubro));

            var arbol = (await Json(await c.GetAsync($"{Base}/cuentas/arbol"))).EnumerateArray().Select(n => n.GetProperty("codigo").GetString()).ToList();
            Assert.DoesNotContain(Codigo(suf, "R"), arbol);
            Assert.Contains(Codigo(suf, "1"), arbol);
            Assert.Equal("Rubro", (await Validar(c, Codigo(suf, "R"), "Manual")).GetProperty("motivo").GetString());

            var hija = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: Id(raiz));
            var malRubro = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{Id(hija)}",
                new { nombre = "FIX", padreId = Id(raiz), naturaleza = "Deudora", cuentaControl = "Ninguna", rubroId = Id(rubro) }, Etag(hija));
            Assert.Equal("CONTAB_CUENTA_RUBRO_INVALIDO", await Code(malRubro));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Importar_el_formato_de_Contabilidad_carga_todo_omite_titulos_crea_rubros_y_deriva_el_tipo()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var req = Fix.FormatoLaura($"FIX-{suf}", $"FIX-{suf}-F");
            var vp = await Json(await c.PostCatalogoYAutorizarAsync($"{Base}/importaciones/vista-previa", req));
            Assert.True(vp.GetProperty("puedeAplicar").GetBoolean());
            Assert.Equal(3, vp.GetProperty("resumen").GetProperty("omitidas").GetInt32());
            Assert.Equal(23, vp.GetProperty("resumen").GetProperty("crear").GetInt32());

            var r = await c.PostCatalogoYAutorizarAsync($"{Base}/importaciones", req with { Huella = vp.GetProperty("huella").GetString() });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(23, (await Json(r)).GetProperty("lote").GetProperty("creadas").GetInt32());

            var todas = (await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&limit=100"))).GetProperty("items").EnumerateArray()
                .ToDictionary(i => i.GetProperty("codigo").GetString()!.Replace($"FIX-{suf}-", "", StringComparison.Ordinal));
            Assert.Equal("Titulo", todas["601.01.00.00"].GetProperty("tipo").GetString());
            Assert.Equal("Afectable", todas["601.01.01.00"].GetProperty("tipo").GetString());
            Assert.Equal(2, todas["170.05.00.07"].GetProperty("nivel").GetInt32());
            Assert.Equal(Id(todas["170.00.00.00"]), todas["170.05.00.07"].GetProperty("padreId").GetGuid());
            Assert.Equal("Rubro", todas["700.00.00.00"].GetProperty("clase").GetString());
            Assert.Equal("Notas a los Estados Financieros", todas["700.00.00.00"].GetProperty("grupoReporte").GetString());
            Assert.False(todas["700.00.00.00"].GetProperty("pendienteValidacion").GetBoolean());
            Assert.True(todas["701.00.00.00"].GetProperty("pendienteValidacion").GetBoolean());

            var delRubro = (await Json(await c.GetAsync($"{Base}/cuentas?rubroId={Id(todas["100.00.00.00"])}"))).GetProperty("items")
                .EnumerateArray().Select(i => i.GetProperty("codigo").GetString()!.Replace($"FIX-{suf}-", "", StringComparison.Ordinal)).Order().ToArray();
            Assert.Equal(["101.00.00.00", "102.00.00.00", "170.00.00.00", "201.00.00.00"], delRubro);
            Assert.Equal(2, (await Json(await c.GetAsync($"{Base}/cuentas?q={suf}&pendientes=true"))).GetProperty("total").GetInt32());

            // Reimportar el mismo archivo no duplica.
            var otra = await c.PostCatalogoYAutorizarAsync($"{Base}/importaciones", req);
            Assert.True((await Json(otra)).GetProperty("idempotente").GetBoolean());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Importacion_que_da_hijas_a_una_afectable_existente_la_convierte_si_no_tiene_movimientos()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var raiz = await CrearCuenta(c, Codigo(suf, "5.00.00"));
            var hoja = await CrearCuenta(c, Codigo(suf, "5.10.00"), padreId: Id(raiz));
            var usada = await CrearCuenta(c, Codigo(suf, "5.20.00"), padreId: Id(raiz));
            await RegistrarUso(Id(usada));

            var conUso = $"codigo;nombre;naturaleza\n{Codigo(suf, "5.20.01")};FIX bajo usada;Deudora\n";
            var rechazo = await c.PostCatalogoYAutorizarAsync($"{Base}/importaciones", Fix.Request(conUso, $"FIX-{suf}-F"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
            Assert.Contains((await Json(rechazo)).GetProperty("errores").EnumerateArray(), e => e.GetProperty("codigo").GetString() == "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO");

            var sinUso = $"codigo;nombre;naturaleza\n{Codigo(suf, "5.10.01")};FIX bajo hoja;Deudora\n";
            Assert.Equal(HttpStatusCode.Created, (await c.PostCatalogoYAutorizarAsync($"{Base}/importaciones", Fix.Request(sinUso, $"FIX-{suf}-F"))).StatusCode);
            Assert.Equal("Titulo", (await Obtener(c, Id(hoja))).GetProperty("tipo").GetString());
            Assert.Equal("Afectable", (await Obtener(c, Id(usada))).GetProperty("tipo").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Perfiles_contador_general_mantiene_e_importa_y_direccion_solo_consulta()
    {
        var suf = Sufijo();
        try
        {
            // Supuesto (P7): ejecuta el Contador General (leer + administrar + importar); la Dirección de Administración y
            // Finanzas autoriza, pero la modalidad de aprobación de la importación sigue pendiente: hoy solo consulta.
            var contador = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer,
                PermisosCanonicos.ContabilidadCatalogoAdministrar, PermisosCanonicos.ContabilidadCatalogoImportar);
            var direccion = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer);

            var raiz = await CrearCuenta(contador, Codigo(suf, "1.0")); // la importación deduce el padre de 1.1 por segmentos
            var edit = await Send(contador, HttpMethod.Put, $"{Base}/cuentas/{Id(raiz)}", new { nombre = "FIX editada", naturaleza = "Deudora", cuentaControl = "Ninguna" }, Etag(raiz));
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            var csv = $"codigo;nombre;naturaleza\n{Codigo(suf, "1.1")};FIX importada;Deudora\n";
            Assert.Equal(HttpStatusCode.Created, (await contador.PostCatalogoYAutorizarAsync($"{Base}/importaciones", Fix.Request(csv, $"FIX-{suf}-F"))).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await direccion.GetAsync($"{Base}/cuentas/{Id(raiz)}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await direccion.GetAsync($"{Base}/importaciones")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await direccion.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "2"), nombre = "x", cuentaControl = "Ninguna" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await direccion.PostCatalogoYAutorizarAsync($"{Base}/importaciones", Fix.Request(csv, $"FIX-{suf}-F"))).StatusCode);
        }
        finally { await Limpiar(factory.Services, suf); }
    }
}
