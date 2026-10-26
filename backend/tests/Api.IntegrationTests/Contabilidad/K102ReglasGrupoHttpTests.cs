using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// K10.2 contra el API y Postgres reales: la regla del grupo de cuentas (rubro, C-03) aplica a las cuentas del rubro, la
/// específica por cuenta y tipo de documento gana sobre ella, la vigencia se toma a la fecha contable, una cuenta de balance
/// sin reglas se acepta sin dimensiones, la póliza manual nombra juntas las dimensiones faltantes (CA10.4) y el puerto que
/// usará el motor (C1.4) reporta el faltante (K10.2-a). Todo es de PRUEBA (FIX-*), no política de Contabilidad.
/// </summary>
public class K102ReglasGrupoHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Polizas = Base + "/polizas";

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateOnly Hoy() => Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);

    /// <summary>
    /// Rubro de gastos (FIX-…-RG) con la rama 6 → 6.1 (fletes); rubro de balance (FIX-…-RB) con la rama 1 → 1.1 (bancos) sin reglas;
    /// una ubicación ligada a la sucursal con su área y su equipo; dos tipos de documento (factura de proveedor y otro).
    /// </summary>
    private sealed record Escenario(
        string Suf, HttpClient Admin, Guid Sucursal, Guid Dim1, Guid Dim2, Guid Dim3, Guid Grupo2, Guid Grupo3,
        Guid RubroGastos, Guid Fletes, Guid Bancos, Guid TipoFactura, Guid TipoOtro);

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        var hoy = Hoy();
        await AsegurarPeriodosAbiertosAsync(admin, hoy, hoy.AddDays(6));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var g2 = await mediator.Send(new CrearGrupoDim2Command($"FIX G2 {suf}"));
        var g3 = await mediator.Send(new CrearGrupoDim3Command($"FIX G3 {suf}"));
        var d1 = await mediator.Send(new CrearDim1Command($"U{suf}", $"FIX Ubicación {suf}"));
        var d2 = await mediator.Send(new CrearDim2Command(d1.Id, $"A{suf}", "FIX Área", g2.Id));
        var d3 = await mediator.Send(new CrearDim3Command(d2.Id, $"Q{suf}", "FIX Equipo", g3.Id));

        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"KG{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var sucursalId = Id(await Json(sucursal));
        (await admin.PutAsJsonAsync($"{Base}/ubicaciones-sucursal/{d1.Id}", new { sucursalId })).EnsureSuccessStatusCode();

        var rubroGastos = await CrearRubroAsync(admin, Codigo(suf, "RG"), "FIX GASTOS DE OPERACIÓN");
        var rubroBalance = await CrearRubroAsync(admin, Codigo(suf, "RB"), "FIX ACTIVO CIRCULANTE");
        var gastos = await RaizEnRubroAsync(admin, Codigo(suf, "6"), "FIX gastos de venta", rubroGastos);
        var fletes = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX fletes", Id(gastos));
        // Como en el catálogo real (102.00.00.00 BANCOS), la raíz acumula: la cuenta afectable es su subcuenta.
        var bancosRaiz = await RaizEnRubroAsync(admin, Codigo(suf, "1"), "FIX bancos", rubroBalance);
        var bancos = await CrearCuenta(admin, Codigo(suf, "1.1"), "FIX banco cuenta de cheques", Id(bancosRaiz));

        var factura = await CrearTipoAsync(admin, $"FIX-{suf}F", $"FIX Factura de proveedor {suf}");
        var otro = await CrearTipoAsync(admin, $"FIX-{suf}O", $"FIX Asiento {suf}");
        return new(suf, admin, sucursalId, d1.Id, d2.Id, d3.Id, g2.Id, g3.Id, rubroGastos, Id(fletes), Id(bancos), factura, otro);
    }

    private static async Task<Guid> CrearRubroAsync(HttpClient c, string codigo, string nombre)
    {
        var r = await c.PostCatalogoYAutorizarAsync($"{Base}/cuentas", new { codigo, nombre, cuentaControl = "Ninguna", clase = "Rubro" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return Id(await Json(r));
    }

    private static async Task<JsonElement> RaizEnRubroAsync(HttpClient c, string codigo, string nombre, Guid rubroId)
    {
        var raiz = await CrearCuenta(c, codigo, nombre);
        var r = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{Id(raiz)}",
            new { nombre, naturaleza = "Deudora", cuentaControl = "Ninguna", rubroId }, Etag(raiz));
        r.EnsureSuccessStatusCode();
        return await Json(r);
    }

    private static async Task<Guid> CrearTipoAsync(HttpClient c, string clave, string nombre)
    {
        var r = await c.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre, esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return Id(await Json(r));
    }

    private static async Task<HttpResponseMessage> PostRegla(HttpClient c, Guid cuentaId, Guid? tipoId, string dimension, DateOnly desde, DateOnly? hasta = null) =>
        await c.PostAsJsonAsync($"{Base}/reglas-dimension", new
        {
            cuentaId, tipoDocumentoId = tipoId, dimension, requerimiento = "Obligatorio", vigenteDesde = desde, vigenteHasta = hasta,
            esPrueba = true, nota = "FIX regla K10.2",
        });

    private static async Task ReglaAsync(HttpClient c, Guid cuentaId, Guid? tipoId, string dimension, DateOnly desde, DateOnly? hasta = null) =>
        Assert.Equal(HttpStatusCode.Created, (await PostRegla(c, cuentaId, tipoId, dimension, desde, hasta)).StatusCode);

    /// <summary>C-03: gastos → ubicación, área y equipo, sobre el rubro.</summary>
    private static async Task ReglasGastosAsync(Escenario e, DateOnly desde, DateOnly? hasta = null)
    {
        foreach (var d in new[] { "Dim1", "Dim2", "Dim3" }) await ReglaAsync(e.Admin, e.RubroGastos, null, d, desde, hasta);
    }

    private static object Mov(Escenario e, Guid cuenta, Guid tipo, DateOnly fecha, Guid? dim3 = null, string? proyecto = null) =>
        new { cuentaId = cuenta, tipoDocumentoId = tipo, fechaContable = fecha, sucursalId = e.Sucursal, dim3Id = dim3, proyecto, referencia = $"FIX-{e.Suf}" };

    private static async Task<JsonElement> Validar(HttpClient c, object mov)
    {
        var r = await c.PostAsJsonAsync($"{Base}/movimientos/validar", mov);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static string[] Faltantes(JsonElement v) =>
        [.. v.GetProperty("errores").EnumerateArray()
            .Where(x => x.GetProperty("codigo").GetString() == "CONTAB_DIM_OBLIGATORIA_FALTANTE")
            .Select(x => x.GetProperty("campo").GetString()!).Order()];

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var like = $"FIX-{e.Suf}-%";
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", like);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id IN ({0}, {1})", e.TipoFactura, e.TipoOtro);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id IN ({0}, {1})", e.TipoFactura, e.TipoOtro);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ubicaciones_sucursal WHERE dim1_id = {0}", e.Dim1);
        await Limpiar(factory.Services, e.Suf);
        var cc = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim3 WHERE id = {e.Dim3}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim2 WHERE id = {e.Dim2}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim1 WHERE id = {e.Dim1}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim2 WHERE id = {e.Grupo2}");
        await cc.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim3 WHERE id = {e.Grupo3}");
    }

    [Fact]
    public async Task K102b_la_regla_del_rubro_aplica_a_sus_cuentas_y_la_especifica_por_cuenta_y_documento_gana()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            await ReglasGastosAsync(e, hoy);
            await ReglaAsync(e.Admin, e.Fletes, e.TipoFactura, "Proyecto", hoy);

            // Grupo: la cuenta de fletes (nieta del rubro por RubroId de su raíz) pide ubicación, área y equipo.
            Assert.Equal(["dim1Id", "dim2Id", "dim3Id"], Faltantes(await Validar(e.Admin, Mov(e, e.Fletes, e.TipoOtro, hoy))));
            var otro = await Validar(e.Admin, Mov(e, e.Fletes, e.TipoOtro, hoy, dim3: e.Dim3));
            Assert.True(otro.GetProperty("valido").GetBoolean(), otro.GetRawText());

            // Específica: fletes en factura de proveedor exige proyecto; en otro tipo de documento se usa la regla del grupo.
            var sinProyecto = await Validar(e.Admin, Mov(e, e.Fletes, e.TipoFactura, hoy, dim3: e.Dim3));
            Assert.Equal(["proyecto"], Faltantes(sinProyecto));
            var conProyecto = await Validar(e.Admin, Mov(e, e.Fletes, e.TipoFactura, hoy, dim3: e.Dim3, proyecto: "FIX-PRY"));
            Assert.True(conProyecto.GetProperty("valido").GetBoolean(), conProyecto.GetRawText());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K102c_la_regla_vigente_a_la_fecha_contable_decide_aunque_la_politica_cambie_despues()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var hoy = Hoy();
            var cambio = hoy.AddDays(4); // «1-dic»: desde ese día el grupo exige además proyecto
            await ReglasGastosAsync(e, hoy);
            await ReglaAsync(e.Admin, e.RubroGastos, null, "Proyecto", cambio);

            var antes = await Validar(e.Admin, Mov(e, e.Fletes, e.TipoOtro, hoy.AddDays(1), dim3: e.Dim3));
            Assert.True(antes.GetProperty("valido").GetBoolean(), antes.GetRawText());
            Assert.Equal(["proyecto"], Faltantes(await Validar(e.Admin, Mov(e, e.Fletes, e.TipoOtro, cambio, dim3: e.Dim3))));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K102e_cuenta_de_balance_sin_dimensiones_se_acepta()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            await ReglasGastosAsync(e, Hoy());
            var v = await Validar(e.Admin, Mov(e, e.Bancos, e.TipoOtro, Hoy()));
            Assert.True(v.GetProperty("valido").GetBoolean(), v.GetRawText());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task CA104_poliza_de_gastos_sin_dimensiones_pide_ubicacion_area_y_equipo()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            await ReglasGastosAsync(e, Hoy());
            var r = await e.Admin.PostAsJsonAsync(Polizas, new
            {
                tipoOperacionId = e.TipoOtro, fecha = Hoy(), concepto = $"FIX gasto sin dimensiones {e.Suf}",
                partidas = new[]
                {
                    new { cuentaId = e.Fletes, concepto = "FIX flete", cargo = 100m, abono = 0m, sucursalId = e.Sucursal },
                    new { cuentaId = e.Bancos, concepto = "FIX pago", cargo = 0m, abono = 100m, sucursalId = e.Sucursal },
                },
            });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var resumen = (await Json(r)).GetProperty("errores").EnumerateArray()
                .Single(x => x.GetProperty("code").GetString() == "CONTAB_DIM_FALTANTES");
            Assert.Equal("partidas[0]", resumen.GetProperty("field").GetString());
            Assert.Contains("capture ubicación, área y equipo", resumen.GetProperty("message").GetString());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K102d_usuario_operativo_no_captura_poliza_con_un_centro_que_no_tiene_asignado()
    {
        Escenario? e = null;
        HttpClient? operativo = null;
        try
        {
            e = await CrearEscenarioAsync();
            // Capturista contable sin asignación de centros y sin centros_costo.dim3.leer-todos.
            operativo = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadPolizasLeer,
                PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadMovimientosGestionarTodasSucursales);
            object Poliza() => new
            {
                tipoOperacionId = e.TipoOtro, fecha = Hoy(), concepto = $"FIX gasto con equipo {e.Suf}",
                partidas = new object[]
                {
                    new { cuentaId = e.Fletes, concepto = "FIX flete", cargo = 100m, abono = 0m, sucursalId = e.Sucursal, dim3Id = e.Dim3 },
                    new { cuentaId = e.Bancos, concepto = "FIX pago", cargo = 0m, abono = 100m, sucursalId = e.Sucursal },
                },
            };

            var r = await operativo.PostAsJsonAsync(Polizas, Poliza());
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var error = (await Json(r)).GetProperty("errores").EnumerateArray()
                .Single(x => x.GetProperty("code").GetString() == "CONTAB_DIM_CENTRO_FUERA_DE_ALCANCE");
            Assert.Equal("partidas[0].dim3Id", error.GetProperty("field").GetString());
            Assert.Contains("no está en tus centros asignados", error.GetProperty("message").GetString());

            // Con alcance total (super admin) el mismo centro se acepta: el rechazo es por la asignación, no por el centro.
            Assert.Equal(HttpStatusCode.Created, (await e.Admin.PostAsJsonAsync(Polizas, Poliza())).StatusCode);
        }
        finally
        {
            if (operativo is not null) await LimpiarClienteAsync(operativo);
            await LimpiarAsync(e);
        }
    }

    [Fact]
    public async Task K102a_el_puerto_del_motor_reporta_el_equipo_faltante_de_un_gasto()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            await ReglasGastosAsync(e, Hoy());
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var puerto = scope.ServiceProvider.GetRequiredService<IDimensionContableValidacionPort>();

            // Evento de gasto (CxP) con ubicación y área pero sin equipo: el motor lo mandará a la bandeja con DatoFaltante.
            var v = await puerto.ValidarAsync(new MovimientoDimensionado(e.Fletes, e.TipoFactura, Hoy(), e.Sucursal,
                e.Dim1, e.Dim2, null, OrigenMovimiento.AuxiliarCxP, Proyecto: "FIX-PRY"), default);

            Assert.False(v.Valido);
            var error = Assert.Single(v.Errores);
            Assert.Equal("CONTAB_DIM_OBLIGATORIA_FALTANTE", error.Codigo);
            Assert.Equal(DimensionContable.Dim3, error.Dimension);
        }
        finally { await LimpiarAsync(e); }
    }
}
