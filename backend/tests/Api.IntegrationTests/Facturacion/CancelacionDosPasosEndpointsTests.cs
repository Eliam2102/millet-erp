using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// F6.5 (86e3jvuvz): cancelación en dos pasos por HTTP. Ingresos solicita (no va al
/// PAC); el Contador General autoriza (va al PAC) o rechaza con motivo; quien
/// solicitó no puede autorizar aunque tenga el permiso. PAC falso; datos ficticios.
/// </summary>
public sealed class CancelacionDosPasosEndpointsTests : IClassFixture<CancelacionDosPasosEndpointsTests.Servidor>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("EKU9003173C9", "Emisor ficticio F6.5", "601", "97000");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio F6.5", "601", "97000", "G03", "MEX", false);
    private const string Base = "/api/v1/facturacion/comprobantes";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Pac _pac;

    public CancelacionDosPasosEndpointsTests(Servidor servidor)
    {
        _factory = servidor.Factory;
        _pac = servidor.PacFalso;
    }

    public sealed class Servidor : IDisposable
    {
        private readonly WebApplicationFactory<Program> _raiz = new();
        public Pac PacFalso { get; } = new();
        public WebApplicationFactory<Program> Factory { get; }

        public Servidor() => Factory = _raiz.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>();
            services.AddSingleton<ICfdiTimbradoPort>(PacFalso);
        }));

        public void Dispose() => _raiz.Dispose();
    }

    /// <summary>El PAC deja la cancelación en proceso y recuerda qué recibió.</summary>
    public sealed class Pac : ICfdiTimbradoPort
    {
        public List<CancelacionSolicitud> Cancelaciones { get; } = [];
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken)
        {
            lock (Cancelaciones) Cancelaciones.Add(solicitud);
            return Task.FromResult(new CancelacionResultado(CancelacionEstado.EnProceso, "EnProceso", null));
        }
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken cancellationToken) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    [Fact]
    public async Task Ingresos_solicita_sin_PAC_y_el_Contador_General_autoriza_y_se_envia()
    {
        var ingresos = await UsuarioConPermisos("ingresos", PermisosCanonicos.FacturacionCancelacionesSolicitar, PermisosCanonicos.FacturacionCancelacionesConsultar);
        var contador = await UsuarioConPermisos("contador", PermisosCanonicos.FacturacionCancelacionesAutorizar, PermisosCanonicos.FacturacionCancelacionesConsultar);
        var (id, uuid) = await SembrarFacturaTimbrada();

        var solicitud = await Leer(await Post(ingresos, $"{Base}/{id}/cancelar", new { motivoSat = "02", uuidSustituto = (string?)null }));
        Assert.Equal("PendienteAutorizacion", solicitud.GetProperty("estadoSolicitud").GetString());
        Assert.Equal("Timbrado", solicitud.GetProperty("estadoComprobante").GetString());
        Assert.DoesNotContain(_pac.Cancelaciones, c => c.Uuid == uuid);

        // Ingresos no tiene el permiso de autorizar.
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(ingresos, $"{Base}/{id}/cancelar/autorizar", null)).StatusCode);

        var bandeja = await Leer(await contador.GetAsync($"{Base}/cancelaciones"));
        var fila = bandeja.EnumerateArray().Single(f => f.GetProperty("comprobanteId").GetGuid() == id);
        Assert.Equal("02", fila.GetProperty("motivoSat").GetString());
        Assert.Equal("Usuario F6.5 ingresos", fila.GetProperty("solicitadaPor").GetString());

        var autorizada = await Leer(await Post(contador, $"{Base}/{id}/cancelar/autorizar", null));
        Assert.Equal("EnProceso", autorizada.GetProperty("estadoSolicitud").GetString());
        Assert.Equal("CancelacionPendiente", autorizada.GetProperty("estadoComprobante").GetString());
        Assert.Contains(_pac.Cancelaciones, c => c.Uuid == uuid && c.MotivoSat == "02");
    }

    [Fact]
    public async Task Quien_solicito_no_puede_autorizar_y_el_rechazo_exige_motivo_y_deja_la_factura_vigente()
    {
        // Un usuario con ambos permisos: el servidor le impide autorizar su propia solicitud.
        var ambos = await UsuarioConPermisos("ambos", PermisosCanonicos.FacturacionCancelacionesSolicitar,
            PermisosCanonicos.FacturacionCancelacionesAutorizar, PermisosCanonicos.FacturacionCancelacionesConsultar);
        var contador = await UsuarioConPermisos("contador", PermisosCanonicos.FacturacionCancelacionesAutorizar, PermisosCanonicos.FacturacionCancelacionesConsultar);
        var (id, uuid) = await SembrarFacturaTimbrada();
        (await Post(ambos, $"{Base}/{id}/cancelar", new { motivoSat = "03", uuidSustituto = (string?)null })).EnsureSuccessStatusCode();

        var propia = await Post(ambos, $"{Base}/{id}/cancelar/autorizar", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
        Assert.Contains("CANCELACION_AUTOAUTORIZACION", await propia.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(contador, $"{Base}/{id}/cancelar/rechazar", new { motivo = "" })).StatusCode);
        var rechazada = await Leer(await Post(contador, $"{Base}/{id}/cancelar/rechazar", new { motivo = "Se corrige con nota de crédito" }));
        Assert.Equal("RechazadaPorAutorizador", rechazada.GetProperty("estadoSolicitud").GetString());

        var consulta = await Leer(await contador.GetAsync($"{Base}/{id}/cancelar"));
        Assert.Equal("Timbrado", consulta.GetProperty("estadoComprobante").GetString());
        Assert.Equal("Se corrige con nota de crédito", consulta.GetProperty("motivoRechazo").GetString());
        Assert.Equal("Usuario F6.5 contador", consulta.GetProperty("decididaPor").GetString());
        Assert.DoesNotContain(_pac.Cancelaciones, c => c.Uuid == uuid);
    }

    [Fact]
    public async Task Ingresos_retira_una_solicitud_pendiente()
    {
        var ingresos = await UsuarioConPermisos("ingresos", PermisosCanonicos.FacturacionCancelacionesSolicitar, PermisosCanonicos.FacturacionCancelacionesConsultar);
        var (id, uuid) = await SembrarFacturaTimbrada();
        (await Post(ingresos, $"{Base}/{id}/cancelar", new { motivoSat = "02", uuidSustituto = (string?)null })).EnsureSuccessStatusCode();

        var retirada = await Leer(await Post(ingresos, $"{Base}/{id}/cancelar/retirar", null));

        Assert.Equal("Retirada", retirada.GetProperty("estadoSolicitud").GetString());
        Assert.Equal("Timbrado", retirada.GetProperty("estadoComprobante").GetString());
        Assert.DoesNotContain(_pac.Cancelaciones, c => c.Uuid == uuid);
    }

    private async Task<(Guid Id, string Uuid)> SembrarFacturaTimbrada()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var f = FacturaVenta.CrearBorrador(Empresa, $"F65-{Guid.NewGuid():N}"[..12], 1, TestComprasFixtures.SucursalMid, null, null, Receptor, Emisor,
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171700", "VIDRIO DEMO", "H87", 1, 100m, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        var uuid = Guid.NewGuid().ToString();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(uuid, "S", "S", "30001000000500003456", DateTimeOffset.UtcNow, "SPR190613I52", null);
        db.Add(f);
        await db.SaveChangesAsync();
        return (f.Id, uuid);
    }

    private async Task<HttpClient> UsuarioConPermisos(string nombre, params string[] permisos)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-f65-{nombre}-{sufijo}";
        var usuarioId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, oid, $"Rol de prueba F6.5 {nombre}"));
            foreach (var codigo in permisos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }
            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", $"Usuario F6.5 {nombre}"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = $"{oid}@test.local", Nombre = $"Usuario F6.5 {nombre}", EmpresaId = (Guid?)Empresa,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Leer(HttpResponseMessage response)
    {
        var texto = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {texto}");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }
}
