using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using MediatR;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// C1.4 · motor de pólizas desde eventos: un caso por criterio de la ficha (CA10.3, C1.4-a … C1.4-h). Usa la plantilla #2 sembrada
/// (cuentas_por_pagar.factura.registrada.v1, TT) y los roles sembrados de la empresa: cada prueba mapea los roles a cuentas FIX
/// propias y restaura el mapeo al terminar. Datos ficticios.
/// </summary>
public sealed class MotorContableHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Motor = "/api/v1/contabilidad/motor";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string Tipo = NormalizadorEventos.FacturaProveedorRegistrada;
    private static readonly string[] RolesFactura = ["MERCANCIA_RECIBIDA_POR_FACTURAR", "IVA_ACREDITABLE_PENDIENTE", "PROVEEDORES", "RETENCIONES_POR_PAGAR", "GASTO"];

    private sealed record Cuentas(string Suf, HttpClient Admin, Guid Sucursal, Guid Puente, Guid Iva, Guid Proveedores, Guid ProveedoresB, Guid Proveedor);

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static DateOnly Hoy() => Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);

    private async Task<Cuentas> PrepararAsync(DateOnly? fecha = null)
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        await AsegurarPeriodosAbiertosAsync(admin, fecha ?? Hoy());
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"PZ{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var activo = Id(await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo"));
        var puente = Id(await CrearCuenta(admin, Codigo(suf, "1.1"), "FIX mercancía recibida por facturar", activo));
        var iva = Id(await CrearCuenta(admin, Codigo(suf, "1.2"), "FIX IVA acreditable pendiente", activo));
        var pasivo = Id(await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora"));
        var prov = Id(await CrearCuenta(admin, Codigo(suf, "2.1"), "FIX proveedores A", pasivo, naturaleza: "Acreedora", control: "Proveedores"));
        var provB = Id(await CrearCuenta(admin, Codigo(suf, "2.2"), "FIX proveedores B", pasivo, naturaleza: "Acreedora", control: "Proveedores"));
        var (proveedor, _) = await MotorTerceros.SembrarAsync(factory);
        return new(suf, admin, Id(await Json(sucursal)), puente, iva, prov, provB, proveedor);
    }

    /// <summary>Mapea los roles de la plantilla #2 y devuelve el mapeo previo para restaurarlo.</summary>
    private async Task<Dictionary<string, Guid?>> MapearAsync(Cuentas c, Guid? iva = null, bool sinIva = false, Guid? gasto = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var roles = await db.RolesCuenta.IgnoreQueryFilters().Where(r => r.EmpresaId == EmpresaBootstrapId && RolesFactura.Contains(r.Codigo)).ToListAsync();
        var previo = roles.ToDictionary(r => r.Codigo, r => r.CuentaId);
        await Set("MERCANCIA_RECIBIDA_POR_FACTURAR", c.Puente);
        await Set("IVA_ACREDITABLE_PENDIENTE", sinIva ? null : iva ?? c.Iva);
        await Set("PROVEEDORES", c.Proveedores);
        if (gasto is not null) await Set("GASTO", gasto);
        return previo;

        async Task Set(string codigo, Guid? cuenta) => await db.Database.ExecuteSqlRawAsync(
            "UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}", cuenta!, EmpresaBootstrapId, codigo);
    }

    private async Task RestaurarAsync(Dictionary<string, Guid?>? previo, Cuentas? c)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        foreach (var (codigo, cuenta) in previo ?? [])
            await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}",
                cuenta!, EmpresaBootstrapId, codigo);
        if (c is null) return;
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE cuenta_nueva_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{c.Suf}-%");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE payload->>'proveedorId' IS NOT NULL AND payload->>'sucursalId' = {0}", c.Sucursal.ToString());
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT p.id::text FROM contabilidad.polizas p JOIN contabilidad.polizas_partidas x ON x.poliza_id = p.id WHERE x.sucursal_id = {0})", c.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE id IN (SELECT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = {0})", c.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{c.Suf}-%");
        await Limpiar(factory.Services, c.Suf);
    }

    private static object Factura(Cuentas c, Guid facturaId, DateOnly fecha, decimal subtotal = 1000m, decimal iva = 160m,
        Guid? proveedor = null, bool conSucursal = true) => new
    {
        empresaId = EmpresaBootstrapId,
        ocurridoEn = new DateTimeOffset(fecha.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-6)),
        facturaProveedorId = facturaId,
        ordenCompraId = Guid.NewGuid(),
        totalFactura = subtotal + iva,
        lineas = new[] { new { lineaFacturaId = Guid.NewGuid(), lineaOcId = Guid.NewGuid(), cantidad = 1m, importe = subtotal } },
        proveedorId = proveedor ?? c.Proveedor,
        uuid = Guid.NewGuid().ToString().ToUpperInvariant(),
        subtotal, iva, retencionesTotal = 0m, moneda = "MXN", tipoCambio = 1m,
        sucursalId = conSucursal ? c.Sucursal : (Guid?)null,
    };

    private static Task<HttpResponseMessage> Procesar(HttpClient cliente, string tipo, Guid eventoId, object payload) =>
        cliente.PostAsJsonAsync($"{Motor}/eventos", new { tipo, eventoId, payload });

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static Guid PolizaId(JsonElement resultado) => resultado.GetProperty("poliza").GetProperty("id").GetGuid();

    private static async Task<JsonElement> RechazoAsync(HttpClient c, Guid eventoId) =>
        (await Json(await c.GetAsync($"{Motor}/rechazos"))).EnumerateArray().Single(x => x.GetProperty("eventoId").GetGuid() == eventoId);

    [Fact]
    public async Task CA10_3_factura_de_1160_genera_TT_autorizada_con_puente_iva_y_proveedor_como_auxiliar_y_su_uuid()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c);
            var factura = Guid.NewGuid();
            var proveedor = c.Proveedor;
            var payload = Factura(c, factura, Hoy(), proveedor: proveedor);

            var r = await Ok(await Procesar(c.Admin, Tipo, Guid.NewGuid(), payload));

            Assert.True(r.GetProperty("generada").GetBoolean());
            Assert.StartsWith("TT-", r.GetProperty("poliza").GetProperty("folio").GetString());
            var p = await Json(await c.Admin.GetAsync($"/api/v1/contabilidad/polizas/{PolizaId(r)}"));
            Assert.Equal("Autorizada", p.GetProperty("estado").GetString());
            Assert.Equal("TT", p.GetProperty("tipoClave").GetString());
            var partidas = p.GetProperty("partidas").EnumerateArray().ToList();
            Assert.Equal(3, partidas.Count);
            Assert.Contains(partidas, x => x.GetProperty("cuentaId").GetGuid() == c.Puente && x.GetProperty("cargo").GetDecimal() == 1000m);
            Assert.Contains(partidas, x => x.GetProperty("cuentaId").GetGuid() == c.Iva && x.GetProperty("cargo").GetDecimal() == 160m);
            var abono = Assert.Single(partidas, x => x.GetProperty("cuentaId").GetGuid() == c.Proveedores);
            Assert.Equal(1160m, abono.GetProperty("abono").GetDecimal());
            Assert.Equal(proveedor, abono.GetProperty("proveedor").GetProperty("id").GetGuid());
            var doc = p.GetProperty("documentoOrigen");
            Assert.Equal("cuentas_por_pagar", doc.GetProperty("modulo").GetString());
            Assert.Equal(factura, doc.GetProperty("id").GetGuid());
            Assert.False(string.IsNullOrEmpty(doc.GetProperty("uuid").GetString()));
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task E6_la_linea_de_servicio_de_la_factura_de_CxP_va_a_gasto_y_solo_la_mercancia_al_puente()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            var gastos = Id(await CrearCuenta(c.Admin, Codigo(c.Suf, "5"), "FIX gastos"));
            var gasto = Id(await CrearCuenta(c.Admin, Codigo(c.Suf, "5.1"), "FIX gasto por servicio", gastos));
            previo = await MapearAsync(c, gasto: gasto);
            var factura = Guid.NewGuid();
            // El evento real que publica CxP (86e3vrh7u), serializado como sale al outbox.
            var evento = new Millet.CuentasPorPagar.Application.Integration.FacturaProveedorRegistradaIntegrationEvent(
                EmpresaBootstrapId, new DateTimeOffset(Hoy().ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-6)), factura, Guid.NewGuid(), 1160m,
                [new(Guid.NewGuid(), Guid.NewGuid(), 1m, 600m), new(Guid.NewGuid(), Guid.NewGuid(), 1m, 400m, EsServicio: true)], [],
                ProveedorId: c.Proveedor, Uuid: Guid.NewGuid().ToString().ToUpperInvariant(), Subtotal: 1000m, Iva: 160m, RetencionesTotal: 0m,
                Moneda: "MXN", TipoCambio: 1m, SucursalId: c.Sucursal);
            var payload = JsonSerializer.SerializeToElement(evento);

            var r = await Ok(await Procesar(c.Admin, Tipo, Guid.NewGuid(), payload));
            Assert.True(r.GetProperty("generada").GetBoolean(), r.ToString());

            var p = await Json(await c.Admin.GetAsync($"/api/v1/contabilidad/polizas/{PolizaId(r)}"));
            var partidas = p.GetProperty("partidas").EnumerateArray().ToList();
            Assert.Equal(600m, Assert.Single(partidas, x => x.GetProperty("cuentaId").GetGuid() == c.Puente).GetProperty("cargo").GetDecimal());
            Assert.Equal(400m, Assert.Single(partidas, x => x.GetProperty("cuentaId").GetGuid() == gasto).GetProperty("cargo").GetDecimal());
            Assert.Equal(1160m, Assert.Single(partidas, x => x.GetProperty("cuentaId").GetGuid() == c.Proveedores).GetProperty("abono").GetDecimal());
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task C14a_el_mismo_evento_dos_veces_y_en_paralelo_da_una_sola_poliza()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c);
            var evento = Guid.NewGuid();
            var payload = Factura(c, Guid.NewGuid(), Hoy());

            var paralelo = await Task.WhenAll(Procesar(c.Admin, Tipo, evento, payload), Procesar(c.Admin, Tipo, evento, payload));
            var ids = new List<Guid>();
            foreach (var r in paralelo) ids.Add(PolizaId(await Ok(r)));
            var tercera = await Ok(await Procesar(c.Admin, Tipo, evento, payload));
            ids.Add(PolizaId(tercera));

            Assert.Single(ids.Distinct());
            Assert.True(tercera.GetProperty("poliza").GetProperty("idempotente").GetBoolean());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            Assert.Equal(1, await db.Polizas.IgnoreQueryFilters().CountAsync(x => x.LlaveIdempotencia == $"{Tipo}:{evento}"));
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task C14b_periodo_cerrado_va_a_la_bandeja_y_al_reabrir_y_reprocesar_nace_la_poliza()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        var anio = Random.Shared.Next(2100, 2990);
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c);
            // Ejercicio de prueba: enero–septiembre cerrados (cierre secuencial).
            var ejercicio = await Json(await c.Admin.PostAsJsonAsync("/api/v1/contabilidad/periodos/ejercicios", new { anio }));
            ejercicio = await Json(await Send(c.Admin, HttpMethod.Post, $"/api/v1/contabilidad/periodos/ejercicios/{Id(ejercicio)}/abrir",
                new { numeros = Enumerable.Range(1, 12).ToArray() }, Etag(ejercicio)));
            foreach (var n in Enumerable.Range(1, 9))
                (await Send(c.Admin, HttpMethod.Post, $"/api/v1/contabilidad/periodos/{Id(await PeriodoAsync(c, ejercicio, n))}/cerrar",
                    new { motivo = "FIX cierre secuencial de prueba" }, Etag(await PeriodoAsync(c, ejercicio, n)))).EnsureSuccessStatusCode();
            var evento = Guid.NewGuid();

            var r = await Ok(await Procesar(c.Admin, Tipo, evento, Factura(c, Guid.NewGuid(), new DateOnly(anio, 9, 15))));
            Assert.False(r.GetProperty("generada").GetBoolean());
            Assert.Equal("PeriodoCerrado", r.GetProperty("causa").GetString());
            var rechazo = await RechazoAsync(c.Admin, evento);
            Assert.Equal("Pendiente", rechazo.GetProperty("estado").GetString());

            var septiembre = await PeriodoAsync(c, ejercicio, 9);
            (await Send(c.Admin, HttpMethod.Post, $"/api/v1/contabilidad/periodos/{Id(septiembre)}/reabrir",
                new { motivo = "FIX reapertura para reprocesar" }, Etag(septiembre))).EnsureSuccessStatusCode();
            var re = await Ok(await c.Admin.PostAsync($"{Motor}/rechazos/{Id(rechazo)}/reprocesar", null));

            Assert.True(re.GetProperty("generada").GetBoolean());
            rechazo = await RechazoAsync(c.Admin, evento);
            Assert.Equal("Reprocesado", rechazo.GetProperty("estado").GetString());
            Assert.Equal(PolizaId(re), rechazo.GetProperty("polizaId").GetGuid());
        }
        finally
        {
            await RestaurarAsync(previo, c);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", anio);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", anio);
        }
    }

    private static async Task<JsonElement> PeriodoAsync(Cuentas c, JsonElement ejercicio, int numero) =>
        (await Json(await c.Admin.GetAsync($"/api/v1/contabilidad/periodos/ejercicios/{Id(ejercicio)}"))).GetProperty("periodos").EnumerateArray()
            .Single(x => x.GetProperty("numero").GetInt32() == numero);

    [Fact]
    public async Task C14c_cambiar_la_cuenta_del_rol_solo_afecta_polizas_nuevas_y_queda_en_bitacora()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c);
            var antes = await Ok(await Procesar(c.Admin, Tipo, Guid.NewGuid(), Factura(c, Guid.NewGuid(), Hoy())));

            var rol = (await Json(await c.Admin.GetAsync($"{Motor}/roles"))).EnumerateArray().Single(x => x.GetProperty("codigo").GetString() == "PROVEEDORES");
            var cambio = await Send(c.Admin, HttpMethod.Put, $"{Motor}/roles/{Id(rol)}/cuenta",
                new { cuentaId = c.ProveedoresB, motivo = "FIX Laura reasigna proveedores" }, $"\"{rol.GetProperty("version").GetInt32()}\"");
            Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
            var despues = await Ok(await Procesar(c.Admin, Tipo, Guid.NewGuid(), Factura(c, Guid.NewGuid(), Hoy())));

            async Task<Guid> CuentaProveedor(JsonElement r) => (await Json(await c.Admin.GetAsync($"/api/v1/contabilidad/polizas/{PolizaId(r)}")))
                .GetProperty("partidas").EnumerateArray().Single(x => x.GetProperty("abono").GetDecimal() > 0).GetProperty("cuentaId").GetGuid();
            Assert.Equal(c.Proveedores, await CuentaProveedor(antes));
            Assert.Equal(c.ProveedoresB, await CuentaProveedor(despues));
            var bitacora = (await Json(await c.Admin.GetAsync($"{Motor}/roles/{Id(rol)}/bitacora"))).EnumerateArray().First();
            Assert.Contains(Codigo(c.Suf, "2.1"), bitacora.GetProperty("cuentaAnterior").GetString());
            Assert.Contains(Codigo(c.Suf, "2.2"), bitacora.GetProperty("cuentaNueva").GetString());
            Assert.Equal("FIX Laura reasigna proveedores", bitacora.GetProperty("motivo").GetString());
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task C14d_e_f_sin_plantilla_rol_sin_cuenta_y_dato_faltante_van_a_la_bandeja_con_su_causa()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c, sinIva: true);

            var sinPlantilla = Guid.NewGuid();
            var d = await Ok(await Procesar(c.Admin, "fix.evento_sin_plantilla.v1", sinPlantilla, new { empresaId = EmpresaBootstrapId, ocurridoEn = DateTimeOffset.UtcNow }));
            Assert.Equal("SinPlantilla", d.GetProperty("causa").GetString());
            Assert.Equal("SinPlantilla", (await RechazoAsync(c.Admin, sinPlantilla)).GetProperty("causa").GetString());

            var sinIva = Guid.NewGuid();
            var e = await Ok(await Procesar(c.Admin, Tipo, sinIva, Factura(c, Guid.NewGuid(), Hoy())));
            Assert.Equal("RolSinCuenta", e.GetProperty("causa").GetString());
            Assert.Equal("IVA_ACREDITABLE_PENDIENTE", e.GetProperty("detalle").GetString());

            await MapearAsync(c);
            var sinSucursal = Guid.NewGuid();
            var f = await Ok(await Procesar(c.Admin, Tipo, sinSucursal, Factura(c, Guid.NewGuid(), Hoy(), conSucursal: false)));
            Assert.Equal("DatoFaltante", f.GetProperty("causa").GetString());
            Assert.Equal("SucursalId", f.GetProperty("detalle").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE evento_id IN ({0}, {1}, {2})", sinPlantilla, sinIva, sinSucursal);
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task C14g_el_motor_sin_usuario_genera_la_poliza_con_la_empresa_del_evento()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c);
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var payload = JsonSerializer.Serialize(Factura(c, Guid.NewGuid(), Hoy()), Web);

            var r = await mediator.Send(new ProcesarEventoContableCommand(Tipo, Guid.NewGuid(), payload));

            Assert.True(r.Generada);
            Assert.StartsWith("TT-", r.Poliza!.Folio);
        }
        finally { await RestaurarAsync(previo, c); }
    }

    [Fact]
    public async Task C14h_sin_permiso_no_se_reprocesa_ni_se_descarta_y_descartar_exige_motivo()
    {
        Cuentas? c = null; Dictionary<string, Guid?>? previo = null;
        try
        {
            c = await PrepararAsync();
            previo = await MapearAsync(c, sinIva: true);
            var evento = Guid.NewGuid();
            await Ok(await Procesar(c.Admin, Tipo, evento, Factura(c, Guid.NewGuid(), Hoy())));
            var rechazo = await RechazoAsync(c.Admin, evento);
            var lector = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadMotorLeer);

            Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsync($"{Motor}/rechazos/{Id(rechazo)}/reprocesar", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsJsonAsync($"{Motor}/rechazos/{Id(rechazo)}/descartar", new { motivo = "FIX no corresponde a Millet" })).StatusCode);

            var sinMotivo = await c.Admin.PostAsJsonAsync($"{Motor}/rechazos/{Id(rechazo)}/descartar", new { motivo = "corto" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinMotivo.StatusCode);
            var descartado = await Ok(await c.Admin.PostAsJsonAsync($"{Motor}/rechazos/{Id(rechazo)}/descartar", new { motivo = "FIX evento duplicado de prueba" }));
            Assert.Equal("Descartado", descartado.GetProperty("estado").GetString());
            await LimpiarClienteAsync(lector);
        }
        finally { await RestaurarAsync(previo, c); }
    }
}
