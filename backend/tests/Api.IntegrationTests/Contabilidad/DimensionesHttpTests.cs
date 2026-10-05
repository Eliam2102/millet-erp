using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-CON-02 contra el API y Postgres reales: casos de la ficha (combinación válida, dimensión obligatoria ausente, centro
/// inactivo, centro de otra sucursal, conservación histórica) y negativos de vigencia, jerarquía, herencia y permisos.
/// Todas las reglas, tipos de documento, centros y sucursales son de PRUEBA (FIX-*), no política de Contabilidad.
/// </summary>
public class DimensionesHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateOnly Hoy() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Merida")).DateTime);

    /// <summary>Datos FIX de un escenario: árbol de centros propio, 2 sucursales, una rama de cuentas y un tipo de documento.</summary>
    private sealed record Escenario(
        string Suf, HttpClient Admin, Guid Dim1, Guid Dim2A, Guid Dim2B, Guid Dim2SinSucursal, Guid Dim3A, Guid Dim3AInactiva, Guid Dim3B,
        Guid SucursalA, Guid SucursalB, Guid CuentaRaiz, Guid CuentaHoja, Guid Tipo, Guid Grupo2, Guid Grupo3);

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var g2 = await mediator.Send(new CrearGrupoDim2Command($"FIX G2 {suf}"));
        var g3 = await mediator.Send(new CrearGrupoDim3Command($"FIX G3 {suf}"));
        var d1 = await mediator.Send(new CrearDim1Command($"D{suf}", $"FIX Planta {suf}"));
        var d2a = await mediator.Send(new CrearDim2Command(d1.Id, $"A{suf}", "FIX CeCo sucursal A", g2.Id));
        var d2b = await mediator.Send(new CrearDim2Command(d1.Id, $"B{suf}", "FIX CeCo sucursal B", g2.Id));
        var d2c = await mediator.Send(new CrearDim2Command(d1.Id, $"C{suf}", "FIX CeCo sin sucursal", g2.Id));
        var d3a = await mediator.Send(new CrearDim3Command(d2a.Id, $"QA{suf}", "FIX equipo A", g3.Id));
        var d3x = await mediator.Send(new CrearDim3Command(d2a.Id, $"QX{suf}", "FIX equipo dado de baja", g3.Id));
        await mediator.Send(new CambiarEstatusDim3Command(d3x.Id, d3x.Version, Activar: false));
        var d3b = await mediator.Send(new CrearDim3Command(d2b.Id, $"QB{suf}", "FIX equipo B", g3.Id));

        var sucA = await CrearSucursalAsync(admin, $"CDA{suf}");
        var sucB = await CrearSucursalAsync(admin, $"CDB{suf}");
        var raiz = await CrearCuenta(admin, Codigo(suf, "5"), "FIX gastos");
        var hoja = await CrearCuenta(admin, Codigo(suf, "5.1"), "FIX gasto de mantenimiento", Id(raiz));
        var tipo = await Json(await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Factura {suf}", esPrueba = true }));

        (await admin.PutAsJsonAsync($"{Base}/centros-sucursal/{d2a.Id}", new { sucursalIds = new[] { sucA } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"{Base}/centros-sucursal/{d2b.Id}", new { sucursalIds = new[] { sucB } })).EnsureSuccessStatusCode();

        return new(suf, admin, d1.Id, d2a.Id, d2b.Id, d2c.Id, d3a.Id, d3x.Id, d3b.Id, sucA, sucB, Id(raiz), Id(hoja), Id(tipo), g2.Id, g3.Id);
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var like = $"FIX-{e.Suf}-%";
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.movimientos_dimension_prueba WHERE cuenta_codigo LIKE {0}", like);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", like);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.centros_costo_sucursal WHERE dim2_id IN ({0}, {1}, {2})", e.Dim2A, e.Dim2B, e.Dim2SinSucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE clave = {0}", $"FIX-{e.Suf}");
        await Limpiar(factory.Services, e.Suf);
        var cc = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim3 WHERE dim2_id IN (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {e.Dim1})");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim2 WHERE dim1_id = {e.Dim1}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim1 WHERE id = {e.Dim1}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim2 WHERE id = {e.Grupo2}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim3 WHERE id = {e.Grupo3}");
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient admin, string clave)
    {
        var r = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = clave, Nombre = $"FIX Sucursal {clave}" });
        r.EnsureSuccessStatusCode();
        return Id(await Json(r));
    }

    private static async Task<JsonElement> CrearRegla(HttpClient c, Guid cuentaId, Guid? tipoId, string dimension, string requerimiento,
        DateOnly desde, DateOnly? hasta = null)
    {
        var r = await c.PostAsJsonAsync($"{Base}/reglas-dimension", new
        {
            cuentaId, tipoDocumentoId = tipoId, dimension, requerimiento, vigenteDesde = desde, vigenteHasta = hasta, esPrueba = true, nota = "FIX regla de prueba",
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await Json(r);
    }

    private static object Mov(Escenario e, Guid sucursal, DateOnly fecha, Guid? dim1 = null, Guid? dim2 = null, Guid? dim3 = null, Guid? cuenta = null) =>
        new { cuentaId = cuenta ?? e.CuentaHoja, tipoDocumentoId = e.Tipo, fechaContable = fecha, sucursalId = sucursal, dim1Id = dim1, dim2Id = dim2, dim3Id = dim3, referencia = $"FIX-{e.Suf}" };

    private static async Task<JsonElement> Validar(HttpClient c, object mov)
    {
        var r = await c.PostAsJsonAsync($"{Base}/movimientos/validar", mov);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static string[] Codigos(JsonElement validacion) =>
        [.. validacion.GetProperty("errores").EnumerateArray().Select(x => x.GetProperty("codigo").GetString()!)];

    /// <summary>Usuario con un rol que tiene SOLO esos permisos; devuelve también su id (para asociarlo a una sucursal).</summary>
    private async Task<(HttpClient Client, Guid UsuarioId)> UsuarioConPermisosAsync(params string[] codigos)
    {
        var admin = await LoginAsync(factory);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"contab-test-{sufijo}";
        var email = $"{oid}@test.local";
        var client = factory.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();
        var rolId = Id(await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles",
            new { Id = Guid.Empty, Codigo = $"rol-contab-{sufijo}", Nombre = $"Rol Contab {sufijo}", Descripcion = (string?)null })));
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();
        return (await LoginAsync(factory, oid, email), usuarioId);
    }

    // ── Casos de la ficha ────────────────────────────────────────────────────

    [Fact]
    public async Task Combinacion_valida_se_acepta_y_dimension_obligatoria_ausente_se_rechaza_indicando_cual_falta()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var regla = await CrearRegla(e.Admin, e.CuentaHoja, e.Tipo, "Dim3", "Obligatorio", hoy);
            Assert.Equal("Vigente", regla.GetProperty("estado").GetString());
            Assert.True(regla.GetProperty("esPrueba").GetBoolean());

            // Válida: la Dim3 obligatoria está; Dim2 y Dim1 se derivan del árbol.
            var ok = await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim3: e.Dim3A));
            Assert.True(ok.GetProperty("valido").GetBoolean(), string.Join(" | ", Codigos(ok)));
            Assert.Equal(e.Dim2A, ok.GetProperty("centros").GetProperty("dim2Id").GetGuid());
            Assert.Equal(e.Dim1, ok.GetProperty("centros").GetProperty("dim1Id").GetGuid());
            var dim3 = ok.GetProperty("requerimientos").EnumerateArray().Single(r => r.GetProperty("dimension").GetString() == "Dim3");
            Assert.Equal(Id(regla), dim3.GetProperty("reglaId").GetGuid());

            // Ausente: rechazo con mensaje que nombra la dimensión y el campo.
            var falta = await Validar(e.Admin, Mov(e, e.SucursalA, hoy));
            Assert.False(falta.GetProperty("valido").GetBoolean());
            var error = Assert.Single(falta.GetProperty("errores").EnumerateArray());
            Assert.Equal("CONTAB_DIM_OBLIGATORIA_FALTANTE", error.GetProperty("codigo").GetString());
            Assert.Equal("dim3Id", error.GetProperty("campo").GetString());
            Assert.Contains("Dimensión 3", error.GetProperty("mensaje").GetString());

            // Confirmar sin la dimensión: 422 con la lista de errores y nada se guarda.
            var r = await e.Admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(e, e.SucursalA, hoy));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var p = await Json(r);
            Assert.Equal("CONTAB_DIM_MOVIMIENTO_INVALIDO", p.GetProperty("code").GetString());
            Assert.Equal("CONTAB_DIM_OBLIGATORIA_FALTANTE", p.GetProperty("errores")[0].GetProperty("codigo").GetString());
            Assert.Equal(0, await Contar(factory.Services, "movimientos_dimension_prueba", $"cuenta_codigo LIKE 'FIX-{e.Suf}-%'"));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Centro_inactivo_se_rechaza_en_movimiento_nuevo_y_no_se_ofrece()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var v = await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim3: e.Dim3AInactiva));
            Assert.Contains("CONTAB_DIM_CENTRO_INACTIVO", Codigos(v));

            var opciones = await Json(await e.Admin.GetAsync($"{Base}/movimientos/centros?sucursalId={e.SucursalA}&nivel=Dim3&q={e.Suf}"));
            var ids = opciones.EnumerateArray().Select(Id).ToList();
            Assert.Contains(e.Dim3A, ids);
            Assert.DoesNotContain(e.Dim3AInactiva, ids);

            // Dar de baja el CeCo inhabilita también a sus equipos (cadena).
            using (var scope = factory.Services.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var d2 = await scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>().Dim2s.AsNoTracking().SingleAsync(x => x.Id == e.Dim2A);
                await mediator.Send(new DesactivarDim2Command(d2.Id, d2.Version));
            }
            Assert.Contains("CONTAB_DIM_CENTRO_INACTIVO", Codigos(await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2A))));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Centro_de_otra_sucursal_no_es_seleccionable_y_la_api_lo_rechaza()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var deA = (await Json(await e.Admin.GetAsync($"{Base}/movimientos/centros?sucursalId={e.SucursalA}&nivel=Dim3&q={e.Suf}"))).EnumerateArray().Select(Id).ToList();
            Assert.Contains(e.Dim3A, deA);
            Assert.DoesNotContain(e.Dim3B, deA);
            var ceCosA = (await Json(await e.Admin.GetAsync($"{Base}/movimientos/centros?sucursalId={e.SucursalA}&nivel=Dim2&q={e.Suf}"))).EnumerateArray().Select(Id).ToList();
            Assert.Equal(new[] { e.Dim2A }, ceCosA);

            // La API repite la validación: no confía en el selector.
            var otra = await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim3: e.Dim3B));
            Assert.Equal("CONTAB_DIM_CENTRO_OTRA_SUCURSAL", Assert.Single(Codigos(otra)));
            Assert.Contains("no está asignado a la sucursal", otra.GetProperty("errores")[0].GetProperty("mensaje").GetString());

            var sinSucursal = await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2SinSucursal));
            Assert.Equal("CONTAB_DIM_CENTRO_SIN_SUCURSAL", Assert.Single(Codigos(sinSucursal)));

            // Configuración: el CeCo B lista su sucursal; el C aparece como pendiente de asignar.
            var pendientes = await Json(await e.Admin.GetAsync($"{Base}/centros-sucursal?q={e.Suf}&soloSinSucursal=true"));
            Assert.Equal(new[] { e.Dim2SinSucursal }, pendientes.EnumerateArray().Select(x => x.GetProperty("dim2Id").GetGuid()).ToList());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Movimiento_registrado_conserva_la_regla_con_la_que_se_valido_tras_cambiar_la_politica()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var r1 = await CrearRegla(e.Admin, e.CuentaHoja, e.Tipo, "Dim3", "Opcional", hoy);

            var conf = await e.Admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Mov(e, e.SucursalA, hoy, dim2: e.Dim2A));
            Assert.Equal(HttpStatusCode.Created, conf.StatusCode);
            var movId = Id(await Json(conf));

            // Una regla en vigor no se edita: se cierra y se crea otra (más estricta) a partir de mañana.
            var editar = await Send(e.Admin, HttpMethod.Put, $"{Base}/reglas-dimension/{Id(r1)}",
                new { requerimiento = "Obligatorio", vigenteDesde = hoy, nota = "FIX" }, Etag(r1));
            Assert.Equal("CONTAB_REGLA_INICIADA_NO_EDITABLE", await Code(editar));
            var traslape = await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension",
                new { cuentaId = e.CuentaHoja, tipoDocumentoId = e.Tipo, dimension = "Dim3", requerimiento = "Obligatorio", vigenteDesde = hoy.AddDays(1), esPrueba = true });
            Assert.Equal(HttpStatusCode.Conflict, traslape.StatusCode);
            Assert.Equal("CONTAB_REGLA_VIGENCIA_TRASLAPADA", await Code(traslape));

            var cerrada = await Send(e.Admin, HttpMethod.Post, $"{Base}/reglas-dimension/{Id(r1)}/cerrar", new { vigenteHasta = hoy }, Etag(r1));
            cerrada.EnsureSuccessStatusCode();
            var r2 = await CrearRegla(e.Admin, e.CuentaHoja, e.Tipo, "Dim3", "Obligatorio", hoy.AddDays(1));
            Assert.Equal("Futura", r2.GetProperty("estado").GetString());
            Assert.True(r2.GetProperty("editable").GetBoolean());

            // El movimiento sigue consultable y muestra la regla vigente cuando se registró.
            var mov = await Json(await e.Admin.GetAsync($"{Base}/movimientos-prueba/{movId}"));
            var aplicada = mov.GetProperty("reglasAplicadas").EnumerateArray().Single(r => r.GetProperty("dimension").GetString() == "Dim3");
            Assert.Equal(Id(r1), aplicada.GetProperty("reglaId").GetGuid());
            Assert.Equal("Opcional", aplicada.GetProperty("requerimiento").GetString());
            Assert.Equal($"A{e.Suf}", mov.GetProperty("dim2").GetProperty("clave").GetString());

            // La política nueva aplica desde su fecha: hoy pasa, mañana la Dim3 es obligatoria.
            Assert.True((await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2A))).GetProperty("valido").GetBoolean());
            Assert.Equal("CONTAB_DIM_OBLIGATORIA_FALTANTE", Assert.Single(Codigos(await Validar(e.Admin, Mov(e, e.SucursalA, hoy.AddDays(1), dim2: e.Dim2A)))));
            var matriz = await Json(await e.Admin.GetAsync($"{Base}/reglas-dimension/efectivas?cuentaId={e.CuentaHoja}&tipoDocumentoId={e.Tipo}&fecha={hoy.AddDays(1):yyyy-MM-dd}"));
            Assert.Equal(Id(r2), matriz.EnumerateArray().Single(r => r.GetProperty("dimension").GetString() == "Dim3").GetProperty("reglaId").GetGuid());

            // Sin retroactividad: una regla que empieza ayer no se acepta.
            var retro = await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension",
                new { cuentaId = e.CuentaHoja, tipoDocumentoId = (Guid?)null, dimension = "Dim1", requerimiento = "Obligatorio", vigenteDesde = hoy.AddDays(-1), esPrueba = true });
            Assert.Equal("CONTAB_REGLA_VIGENCIA_RETROACTIVA", await Code(retro));
        }
        finally { await LimpiarAsync(e); }
    }

    // ── Negativos adicionales ────────────────────────────────────────────────

    [Fact]
    public async Task Regla_de_la_rama_se_hereda_no_aplica_rechaza_lo_capturado_y_la_jerarquia_debe_ser_congruente()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            await CrearRegla(e.Admin, e.CuentaRaiz, null, "Dim2", "Obligatorio", hoy);
            await CrearRegla(e.Admin, e.CuentaHoja, e.Tipo, "Dim1", "NoAplica", hoy);

            var heredada = await Validar(e.Admin, Mov(e, e.SucursalA, hoy));
            var err = Assert.Single(heredada.GetProperty("errores").EnumerateArray());
            Assert.Equal("CONTAB_DIM_OBLIGATORIA_FALTANTE", err.GetProperty("codigo").GetString());
            Assert.Contains($"regla definida en la cuenta {Codigo(e.Suf, "5")}", err.GetProperty("mensaje").GetString());

            // Dim1 derivada de la Dim2 no viola "no aplica"; capturada directamente sí.
            Assert.True((await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2A))).GetProperty("valido").GetBoolean());
            Assert.Equal("CONTAB_DIM_NO_APLICA", Assert.Single(Codigos(await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim1: e.Dim1, dim2: e.Dim2A)))));

            // Equipo de un CeCo con el CeCo de otra rama.
            Assert.Contains("CONTAB_DIM_JERARQUIA_INCONGRUENTE", Codigos(await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2A, dim3: e.Dim3B))));

            // Cuenta que acumula: la combinación tampoco es válida.
            Assert.Contains("CONTAB_DIM_CUENTA_NO_VALIDA", Codigos(await Validar(e.Admin, Mov(e, e.SucursalA, hoy, dim2: e.Dim2A, cuenta: e.CuentaRaiz))));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Solo_la_configuracion_contable_edita_reglas_y_el_alcance_por_sucursal_se_respeta()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            Assert.Equal(PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales, PermisosDimensiones.MovimientosTodasSucursales);

            // Sin sesión: 401. Solo lectura: no crea reglas (403).
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"{Base}/reglas-dimension")).StatusCode);
            var (lector, _) = await UsuarioConPermisosAsync(PermisosCanonicos.ContabilidadDimensionesLeer);
            Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"{Base}/reglas-dimension?cuentaId={e.CuentaHoja}")).StatusCode);
            var crear = await lector.PostAsJsonAsync($"{Base}/reglas-dimension",
                new { cuentaId = e.CuentaHoja, dimension = "Dim2", requerimiento = "Obligatorio", vigenteDesde = hoy, esPrueba = true });
            Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsJsonAsync($"{Base}/movimientos/validar", Mov(e, e.SucursalA, hoy))).StatusCode);

            // Operativo: valida movimientos solo de su sucursal (ADR-0051).
            var (operativo, usuarioId) = await UsuarioConPermisosAsync(PermisosCanonicos.ContabilidadMovimientosValidar);
            var ajena = await operativo.PostAsJsonAsync($"{Base}/movimientos/validar", Mov(e, e.SucursalA, hoy, dim3: e.Dim3A));
            Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
            Assert.Equal("SUCURSAL_NO_ASOCIADA", await Code(ajena));
            using (var scope = factory.Services.CreateScope())
            {
                var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                identidad.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, e.SucursalA, EmpresaBootstrapId));
                await identidad.SaveChangesAsync();
            }
            Assert.True((await Validar(operativo, Mov(e, e.SucursalA, hoy, dim3: e.Dim3A))).GetProperty("valido").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await operativo.GetAsync($"{Base}/movimientos/centros?sucursalId={e.SucursalB}&nivel=Dim3")).StatusCode);
            var suyas = (await Json(await operativo.GetAsync($"{Base}/movimientos/sucursales"))).EnumerateArray().Select(Id).ToList();
            Assert.Contains(e.SucursalA, suyas);
            Assert.DoesNotContain(e.SucursalB, suyas);

            // Corporativo: con el permiso de bypass opera cualquier sucursal sin estar asociado.
            var (corporativo, _) = await UsuarioConPermisosAsync(PermisosCanonicos.ContabilidadMovimientosValidar,
                PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales);
            Assert.True((await Validar(corporativo, Mov(e, e.SucursalB, hoy, dim3: e.Dim3B))).GetProperty("valido").GetBoolean());
        }
        finally { await LimpiarAsync(e); }
    }
}
