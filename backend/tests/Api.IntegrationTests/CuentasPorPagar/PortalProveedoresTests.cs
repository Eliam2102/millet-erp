using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Application.Portal;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>
/// P8.3 parte A: portal de proveedores. Sin correo real (IPortalCorreoPort capturado) y con datos FIX que se limpian.
/// Cubre los criterios del plano: aislamiento entre proveedores, el token del portal no sirve en el ERP,
/// 202 sin correo para no autorizados, código vencido / 5 intentos, FALTA_REPP detenida y permiso de administración.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class PortalProveedoresTests(WebApplicationFactory<Program> factory)
{
    private const string Portal = "/api/v1/portal-proveedores";

    private sealed class CorreoCapturado : IPortalCorreoPort
    {
        public List<(string Para, string Codigo)> Enviados { get; } = [];
        public Task EnviarCodigoAsync(string d, string c, CancellationToken ct) { lock (Enviados) Enviados.Add((d, c)); return Task.CompletedTask; }
        public List<string> Avisos { get; } = [];
        public Task EnviarAvisoPagoAsync(string d, AvisoPagoCorreo a, CancellationToken ct) { lock (Avisos) Avisos.Add(d); return Task.CompletedTask; }
    }

    private sealed class Reloj : IClock
    {
        public TimeSpan Adelanto { get; set; }
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Adelanto;
    }

    private sealed class PeriodoAbierto : IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Proveedores(params Guid[] activos) : IProveedorReadPort
    {
        public Task<ProveedorDto?> ObtenerAsync(Guid p, CancellationToken ct) =>
            Task.FromResult<ProveedorDto?>(activos.Contains(p) ? new(p, "FIX010101ABC", "FIX Proveedor Portal", null, false, true) : null);
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string r, CancellationToken ct) => Task.FromResult<ProveedorDto?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, _ => "FIX Proveedor Portal"));
    }

    private sealed record Entorno(WebApplicationFactory<Program> App, CorreoCapturado Correo, Reloj Reloj, Guid ProvA, Guid ProvB, string Sufijo);

    private Entorno Preparar()
    {
        var correo = new CorreoCapturado();
        var reloj = new Reloj();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var app = factory.ConEmpresa(EmpresaBootstrapId).WithWebHostBuilder(h => h.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPortalCorreoPort>(); s.AddSingleton<IPortalCorreoPort>(correo);
            s.RemoveAll<IClock>(); s.AddSingleton<IClock>(reloj);
            // Siembra directa en BD: el período contable real no es el objeto de esta prueba.
            s.RemoveAll<IPeriodoContablePort>(); s.AddSingleton<IPeriodoContablePort>(new PeriodoAbierto());
            s.RemoveAll<IProveedorReadPort>(); s.AddSingleton<IProveedorReadPort>(new Proveedores(a, b));
        }));
        return new Entorno(app, correo, reloj, a, b, Guid.NewGuid().ToString("N")[..8]);
    }

    private static async Task<Guid> AutorizarAsync(HttpClient admin, Guid proveedor, string email)
    {
        var r = await admin.PostAsJsonAsync($"/api/v1/cuentas-por-pagar/proveedores/{proveedor}/acceso-portal", new { email });
        r.EnsureSuccessStatusCode();
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpClient> EntrarAsync(Entorno e, string email)
    {
        var anonimo = e.App.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anonimo.PostAsJsonAsync($"{Portal}/codigo", new { email })).StatusCode);
        var codigo = e.Correo.Enviados.Last(x => x.Para == email).Codigo;
        var sesion = await anonimo.PostAsJsonAsync($"{Portal}/sesion", new { email, codigo });
        sesion.EnsureSuccessStatusCode();
        var cliente = e.App.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(sesion)).GetProperty("token").GetString());
        return cliente;
    }

    private static async Task<List<Guid>> Sembrar(Entorno e, Guid proveedor, bool faltaRepp, params string[] folios)
    {
        using var scope = e.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var facturas = folios.Select(f => Factura.CapturarSinOc(EmpresaBootstrapId, null, null, proveedor, Guid.NewGuid(), f, null,
            ahora, ahora, DateOnly.FromDateTime(DateTime.UtcNow), "MXN", null, 100, 0, 0, 0, 100, "FIX portal", ahora)).ToList();
        if (faltaRepp) facturas[0].EnviarARevision(Factura.MotivoFaltaReppId, Guid.NewGuid(), null, ahora);
        db.AddRange(facturas);
        await db.SaveChangesAsync();
        return facturas.Select(f => f.Id).ToList();
    }

    private static async Task LimpiarAsync(Entorno e)
    {
        using var scope = e.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var ids = new[] { e.ProvA, e.ProvB };
        var accesos = await db.AccesosPortalProveedor.Where(a => ids.Contains(a.ProveedorId)).Select(a => a.Id).ToListAsync();
        await db.CodigosPortal.Where(c => accesos.Contains(c.AccesoId)).ExecuteDeleteAsync();
        await db.AccesosPortalProveedor.Where(a => ids.Contains(a.ProveedorId)).ExecuteDeleteAsync();
        await db.FacturasProveedor.Where(f => ids.Contains(f.ProveedorId)).ExecuteDeleteAsync();
        var eventos = await db.OutboxEntries.ToListAsync();
        db.OutboxEntries.RemoveRange(eventos.Where(o => ids.Any(i => o.Payload.Contains(i.ToString(), StringComparison.Ordinal))));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Proveedor_con_correo_autorizado_entra_y_ve_solo_sus_facturas_y_la_de_falta_repp_aparece_detenida()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            await Sembrar(e, e.ProvA, faltaRepp: true, $"FIXA1-{e.Sufijo}", $"FIXA2-{e.Sufijo}");
            await Sembrar(e, e.ProvB, faltaRepp: false, $"FIXB1-{e.Sufijo}");

            var portal = await EntrarAsync(e, correoA);
            var r = await portal.GetAsync($"{Portal}/facturas");
            r.EnsureSuccessStatusCode();
            var items = (await Json(r)).EnumerateArray().ToList();

            Assert.Equal(2, items.Count);
            Assert.All(items, i => Assert.StartsWith("FIXA", i.GetProperty("folio").GetString()));
            var detenida = items.Single(i => i.GetProperty("folio").GetString() == $"FIXA1-{e.Sufijo}");
            Assert.True(detenida.GetProperty("detenida").GetBoolean());
            Assert.Contains("falta el complemento de pago", detenida.GetProperty("estado").GetString());
            Assert.Contains("Falta el complemento de pago de la factura", detenida.GetProperty("motivo").GetString());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Proveedor_A_que_pide_una_factura_del_proveedor_B_recibe_404()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var propias = await Sembrar(e, e.ProvA, false, $"FIXA1-{e.Sufijo}");
            var ajenas = await Sembrar(e, e.ProvB, false, $"FIXB1-{e.Sufijo}");

            var portal = await EntrarAsync(e, correoA);

            Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync($"{Portal}/facturas/{propias[0]}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"{Portal}/facturas/{ajenas[0]}")).StatusCode);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task El_token_del_portal_no_sirve_en_rutas_del_ERP_y_el_de_un_empleado_no_sirve_en_el_portal()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var portal = await EntrarAsync(e, correoA);

            foreach (var ruta in new[]
            {
                "/api/v1/cuentas-por-pagar/facturas", "/api/v1/cuentas-por-pagar/cfdis",
                "/api/v1/datos-maestros/proveedores", "/api/auth/me",
            })
            {
                var r = await portal.GetAsync(ruta);
                Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{ruta} respondió {(int)r.StatusCode}");
            }

            // El admin del ERP (superadmin) tampoco entra al portal con su token.
            var r2 = await admin.GetAsync($"{Portal}/facturas");
            Assert.True(r2.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"portal respondió {(int)r2.StatusCode}");
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Correo_no_autorizado_recibe_202_y_no_se_envia_ningun_correo()
    {
        var e = Preparar();
        await using var _ = e.App;
        var r = await e.App.CreateClient().PostAsJsonAsync($"{Portal}/codigo", new { email = $"nadie-{e.Sufijo}@fix.test" });

        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.Empty(e.Correo.Enviados);
    }

    [Fact]
    public async Task Codigo_vencido_se_rechaza()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var anonimo = e.App.CreateClient();
            await anonimo.PostAsJsonAsync($"{Portal}/codigo", new { email = correoA });
            var codigo = e.Correo.Enviados.Single().Codigo;

            e.Reloj.Adelanto = TimeSpan.FromMinutes(11);
            var r = await anonimo.PostAsJsonAsync($"{Portal}/sesion", new { email = correoA, codigo });

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Cinco_intentos_fallidos_agotan_el_codigo_aunque_el_sexto_sea_correcto()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var anonimo = e.App.CreateClient();
            await anonimo.PostAsJsonAsync($"{Portal}/codigo", new { email = correoA });
            var bueno = e.Correo.Enviados.Single().Codigo;
            var malo = bueno == "000000" ? "111111" : "000000";

            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync($"{Portal}/sesion", new { email = correoA, codigo = malo })).StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync($"{Portal}/sesion", new { email = correoA, codigo = bueno })).StatusCode);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Maximo_tres_codigos_por_hora_y_el_codigo_no_se_guarda_en_claro()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var anonimo = e.App.CreateClient();
            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Accepted, (await anonimo.PostAsJsonAsync($"{Portal}/codigo", new { email = correoA })).StatusCode);

            Assert.Equal(3, e.Correo.Enviados.Count);
            using var scope = e.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var hashes = await db.CodigosPortal.Select(c => c.CodigoHash).ToListAsync();
            Assert.DoesNotContain(hashes, h => e.Correo.Enviados.Any(x => x.Codigo == h));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Empleado_sin_el_permiso_no_puede_agregar_correos_y_con_el_permiso_si()
    {
        var e = Preparar();
        await using var _ = e.App;
        var sinPermiso = await ClienteConPermisosAsync(e.App);
        var conPermiso = await ClienteConPermisosAsync(e.App, PermisosCanonicos.CuentasPorPagarPortalAdministrar);
        try
        {
            var ruta = $"/api/v1/cuentas-por-pagar/proveedores/{e.ProvA}/acceso-portal";
            Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.PostAsJsonAsync(ruta, new { email = $"x-{e.Sufijo}@fix.test" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await conPermiso.PostAsJsonAsync(ruta, new { email = $"x-{e.Sufijo}@fix.test" })).StatusCode);
        }
        finally
        {
            await LimpiarAsync(e);
            await LimpiarClienteAsync(sinPermiso);
            await LimpiarClienteAsync(conPermiso);
        }
    }

    // ---- Parte B: subir CFDI y aviso de pago ----

    private static string CfdiXml(string tipo, string rfcEmisor, Guid uuid) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="{tipo}" Serie="A" Folio="F1"
                          Fecha="2026-10-30T10:00:00" Total="116.00" SubTotal="100.00" Moneda="MXN">
          <cfdi:Emisor Rfc="{rfcEmisor}" Nombre="Proveedor" />
          <cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet" />
          <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="01010101" Cantidad="1" ClaveUnidad="E48" Descripcion="x" ValorUnitario="100" Importe="100" /></cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="16.00" />
          <cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="{uuid.ToString().ToUpperInvariant()}" FechaTimbrado="2026-10-30T10:00:00" /></cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    private static Task<HttpResponseMessage> SubirAsync(HttpClient portal, string xml, string nombre = "cfdi.xml")
    {
        var form = new MultipartFormDataContent { { new StringContent(xml, System.Text.Encoding.UTF8, "application/xml"), "xml", nombre } };
        return portal.PostAsync($"{Portal}/cfdi", form);
    }

    private static async Task LimpiarCfdisAsync(Entorno e, params Guid[] uuids)
    {
        using var scope = e.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var textos = uuids.Select(u => u.ToString().ToUpperInvariant()).ToList();
        var cfdis = await db.CfdisRecibidos.ToListAsync();
        db.CfdisRecibidos.RemoveRange(cfdis.Where(c => textos.Contains(c.UuidCfdi.Valor)));
        var eventos = await db.OutboxEntries.ToListAsync();
        db.OutboxEntries.RemoveRange(eventos.Where(o => textos.Any(t => o.Payload.Contains(t, StringComparison.OrdinalIgnoreCase))));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Xml_valido_entra_a_CFDI_recibidos_con_canal_portal_y_un_uuid_repetido_dice_Ya_lo_teniamos_sin_duplicar()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        var uuid = Guid.NewGuid();
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var portal = await EntrarAsync(e, correoA);

            var primera = await SubirAsync(portal, CfdiXml("I", "FIX010101ABC", uuid));
            var repetida = await SubirAsync(portal, CfdiXml("I", "FIX010101ABC", uuid));

            Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
            Assert.Equal("Recibido", (await Json(primera)).GetProperty("resultado").GetString());
            Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
            Assert.Equal("YaLoTeniamos", (await Json(repetida)).GetProperty("resultado").GetString());
            using var scope = e.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var guardados = (await db.CfdisRecibidos.ToListAsync()).Where(c => string.Equals(c.UuidCfdi.Valor, uuid.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
            var uno = Assert.Single(guardados);
            Assert.Equal(CanalOrigenCfdi.PortalProveedor, uno.CanalOrigen);
        }
        finally { await LimpiarCfdisAsync(e, uuid); await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Complemento_tipo_P_entra_a_la_bandeja_de_CxP()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        var uuid = Guid.NewGuid();
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var portal = await EntrarAsync(e, correoA);

            Assert.Equal(HttpStatusCode.Created, (await SubirAsync(portal, CfdiXml("P", "FIX010101ABC", uuid))).StatusCode);

            var lista = await Json(await admin.GetAsync("/api/v1/cuentas-por-pagar/cfdis?rfcEmisor=FIX010101ABC&limit=500"));
            Assert.Contains(lista.GetProperty("items").EnumerateArray(),
                i => string.Equals(i.GetProperty("uuidCfdi").GetString(), uuid.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally { await LimpiarCfdisAsync(e, uuid); await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Xml_con_RFC_de_otro_proveedor_tipo_T_o_archivo_no_xml_se_rechazan_y_no_se_guarda_nada()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        var ajeno = Guid.NewGuid();
        var traslado = Guid.NewGuid();
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var portal = await EntrarAsync(e, correoA);

            var rfc = await SubirAsync(portal, CfdiXml("I", "OTR010101ABC", ajeno));
            var tipoT = await SubirAsync(portal, CfdiXml("T", "FIX010101ABC", traslado));
            var noXml = await SubirAsync(portal, "<cfdi/>", "factura.exe");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, rfc.StatusCode);
            Assert.Equal("PORTAL_RFC_NO_COINCIDE", await Code(rfc));
            Assert.Equal("PORTAL_TIPO_NO_ACEPTADO", await Code(tipoT));
            Assert.Equal("PORTAL_ARCHIVO_NO_XML", await Code(noXml));
            using var scope = e.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var ids = new[] { ajeno, traslado }.Select(u => u.ToString().ToUpperInvariant()).ToList();
            Assert.DoesNotContain((await db.CfdisRecibidos.ToListAsync()), c => ids.Contains(c.UuidCfdi.Valor));
        }
        finally { await LimpiarCfdisAsync(e, ajeno, traslado); await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Subir_CFDI_sin_token_del_portal_da_401()
    {
        var e = Preparar();
        await using var _ = e.App;
        var r = await SubirAsync(e.App.CreateClient(), CfdiXml("I", "FIX010101ABC", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Pago_de_Tesoreria_avisa_una_sola_vez_a_los_correos_autorizados_aunque_el_evento_llegue_dos_veces()
    {
        var e = Preparar();
        await using var _ = e.App;
        var admin = await LoginAsync(e.App);
        var correoA = $"a-{e.Sufijo}@fix.test";
        var pagoId = Guid.NewGuid();
        try
        {
            await AutorizarAsync(admin, e.ProvA, correoA);
            var ids = await Sembrar(e, e.ProvA, false, $"FIXA1-{e.Sufijo}");
            using (var scope = e.App.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var f = await db.FacturasProveedor.FirstAsync(x => x.Id == ids[0]);
                f.Autorizar(null, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
            var evento = Guid.NewGuid();
            var payload = new PagoFacturaProveedorPayload(EmpresaBootstrapId, DateTimeOffset.UtcNow, ids[0], pagoId, 10m, "MXN",
                new DateOnly(2026, 10, 30), null, "REF", null);

            using (var scope = e.App.Services.CreateScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new PagoFacturaProveedorAplicadoCommand(evento, payload));
                await sender.Send(new PagoFacturaProveedorAplicadoCommand(evento, payload));
            }
            for (var i = 0; i < 2; i++)
            {
                using var scope = e.App.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new EnviarAvisosPagoPortalCommand());
            }

            Assert.Equal([correoA], e.Correo.Avisos);
        }
        finally
        {
            using var scope = e.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await db.AvisosPagoPortal.Where(a => a.PagoId == pagoId).ExecuteDeleteAsync();
            await db.PagosProveedorLocal.Where(p => p.PagoId == pagoId).ExecuteDeleteAsync();
            await db.EventosProcesados.Where(x => x.EventoTipo == PagoFacturaProveedorAplicadoHandler.EventType && x.Detalle!.Contains(pagoId.ToString())).ExecuteDeleteAsync();
            await LimpiarAsync(e);
        }
    }
}
