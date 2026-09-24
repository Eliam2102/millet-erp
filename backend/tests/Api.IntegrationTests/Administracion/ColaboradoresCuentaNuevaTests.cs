using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using static Millet.Api.IntegrationTests.Administracion.ColaboradoresEndpointsTests;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Alta unificada, camino B "Cuenta Microsoft nueva" (F1-ADM-01, plan 15,
/// F4): el alta deja al usuario en <c>ProvisionandoCuenta</c>,
/// <see cref="ProvisionCuentaEntraWorker"/> crea la cuenta en el directorio
/// simulado y envía el acceso, y el admin puede reintentar o reenviar.
///
/// El ciclo automático del worker está apagado para todo el assembly
/// (<c>TestAssemblyInit</c>): cada prueba corre un ciclo a mano. El host de
/// esta clase cambia el correo saliente por uno que se puede hacer fallar.
/// </summary>
public class ColaboradoresCuentaNuevaTests : IClassFixture<ColaboradoresCuentaNuevaTests.HostCuentaNueva>
{
    private const int CuentaNueva = 2;
    private const string EmailContacto = "contacto.personal@gmail.com";

    private readonly HostCuentaNueva _host;

    public ColaboradoresCuentaNuevaTests(HostCuentaNueva host)
    {
        _host = host;
        _host.Correo.Fallar = false;
    }

    [Fact]
    public async Task Alta_Deja_Usuario_En_Provision_Sin_Tocar_El_Directorio()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();

        var response = await PostAltaAsync(client, org, CuentaNueva, upn, org.RolId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var acceso = (await ReadJsonAsync(response)).GetProperty("acceso");
        Assert.Equal((int)EstadoAcceso.ProvisionandoCuenta, acceso.GetProperty("estadoAcceso").GetInt32());
        Assert.Equal($"{Usuario.PrefijoOidPendiente}{upn}", acceso.GetProperty("entraOid").GetString());

        // La cuenta en Entra se crea hasta que corre el worker, nunca dentro del alta.
        var directorio = _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>();
        Assert.Null(await directorio.BuscarPorCorreoAsync(upn, CancellationToken.None));
        Assert.DoesNotContain(_host.Correo.Enviados, c => c.Upn == upn);
    }

    [Fact]
    public async Task Dar_Acceso_Nuevo_A_Empleado_Sin_Usuario_Activa_El_Worker()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var alta = await PostAltaAsync(client, org, acceso: 0, correo: null, rolId: null);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var empleadoId = (await ReadJsonAsync(alta)).GetProperty("empleado").GetProperty("id").GetGuid();
        var upn = NuevoUpn();

        var acceso = await client.PostAsJsonAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso", new
        {
            Acceso = CuentaNueva,
            CorreoCorporativo = upn,
            EmailContacto,
            RolId = org.RolId,
        });
        Assert.Equal(HttpStatusCode.OK, acceso.StatusCode);
        var usuarioId = (await ReadJsonAsync(acceso)).GetProperty("acceso").GetProperty("usuarioId").GetGuid();
        Assert.Equal(EstadoAcceso.ProvisionandoCuenta, (await LeerUsuarioAsync(usuarioId)).EstadoAcceso);

        await CorrerWorkerAsync();

        Assert.Equal(EstadoAcceso.PendientePrimerAcceso, (await LeerUsuarioAsync(usuarioId)).EstadoAcceso);
        Assert.Single(_host.Correo.Enviados, c => c.Upn == upn);
    }

    [Fact]
    public async Task Worker_Crea_La_Cuenta_Vincula_El_Oid_Y_Envia_El_Acceso()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        var (empleadoId, usuarioId) = await AltaCuentaNuevaAsync(client, org, upn);

        await CorrerWorkerAsync();

        var cuenta = await _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>()
            .BuscarPorCorreoAsync(upn, CancellationToken.None);
        Assert.NotNull(cuenta);

        var usuario = await LeerUsuarioAsync(usuarioId);
        Assert.Equal(EstadoAcceso.PendientePrimerAcceso, usuario.EstadoAcceso);
        Assert.Equal(cuenta.ObjectId, usuario.EntraOid);
        Assert.NotNull(usuario.AccesoEnviadoEn);
        Assert.Null(usuario.MotivoErrorProvision);

        var correo = Assert.Single(_host.Correo.Enviados, c => c.Upn == upn);
        Assert.Equal(EmailContacto, correo.Destinatario);
        Assert.False(string.IsNullOrWhiteSpace(correo.ContrasenaTemporal));

        // La contraseña no queda en la auditoría; el cambio se atribuye al worker.
        using var scope = _host.Factory.Services.CreateScope();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var auditoria = await compartido.Set<AuditLogEntry>()
            .Where(a => a.EntidadId == usuarioId || a.EntidadId == empleadoId)
            .ToListAsync();
        Assert.DoesNotContain(auditoria, a =>
            a.Cambios.Contains(correo.ContrasenaTemporal) || (a.Metadatos ?? "").Contains(correo.ContrasenaTemporal));
        Assert.Contains(auditoria, a => (a.Metadatos ?? "").Contains(nameof(ProvisionCuentaEntraWorker)));

        var estado = await client.GetAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
        var body = await ReadJsonAsync(estado);
        Assert.Equal((int)EstadoAcceso.PendientePrimerAcceso, body.GetProperty("estadoAcceso").GetInt32());
        Assert.Equal(EmailContacto, body.GetProperty("emailContacto").GetString());
    }

    [Fact]
    public async Task Correo_Fallido_Deja_Error_Y_Reintentar_Adopta_La_Misma_Cuenta()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        var (empleadoId, usuarioId) = await AltaCuentaNuevaAsync(client, org, upn);

        _host.Correo.Fallar = true;
        await CorrerWorkerAsync();

        var fallido = await LeerUsuarioAsync(usuarioId);
        Assert.Equal(EstadoAcceso.ErrorProvision, fallido.EstadoAcceso);
        Assert.Contains("buzón", fallido.MotivoErrorProvision);
        // La cuenta sí se creó, pero el OID no se vincula hasta que el acceso llegue.
        Assert.True(fallido.TieneOidPendiente);
        var cuenta = await _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>()
            .BuscarPorCorreoAsync(upn, CancellationToken.None);
        Assert.NotNull(cuenta);

        _host.Correo.Fallar = false;
        var reintento = await client.PostAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso/reintentar", null);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal(
            (int)EstadoAcceso.ProvisionandoCuenta,
            (await ReadJsonAsync(reintento)).GetProperty("estadoAcceso").GetInt32());

        await CorrerWorkerAsync();

        var provisionado = await LeerUsuarioAsync(usuarioId);
        Assert.Equal(EstadoAcceso.PendientePrimerAcceso, provisionado.EstadoAcceso);
        Assert.Equal(cuenta.ObjectId, provisionado.EntraOid);
        Assert.Single(_host.Correo.Enviados, c => c.Upn == upn);
    }

    [Fact]
    public async Task Reenviar_Acceso_Genera_Otra_Contrasena()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        var (empleadoId, usuarioId) = await AltaCuentaNuevaAsync(client, org, upn);
        await CorrerWorkerAsync();
        var primerEnvio = (await LeerUsuarioAsync(usuarioId)).AccesoEnviadoEn;

        var response = await client.PostAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso/reenviar", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correos = _host.Correo.Enviados.Where(c => c.Upn == upn).ToList();
        Assert.Equal(2, correos.Count);
        Assert.NotEqual(correos[0].ContrasenaTemporal, correos[1].ContrasenaTemporal);
        Assert.True((await LeerUsuarioAsync(usuarioId)).AccesoEnviadoEn > primerEnvio);
    }

    [Fact]
    public async Task Baja_Impide_Reenvio_Y_Reactivacion_Directa_Del_Usuario()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        var (empleadoId, usuarioId) = await AltaCuentaNuevaAsync(client, org, upn);
        await CorrerWorkerAsync();
        var correosAntes = _host.Correo.Enviados.Count(c => c.Upn == upn);

        var baja = await client.PostAsync($"/api/v1/admin/empleados/{empleadoId}/desactivar", null);
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);
        await AssertProblemAsync(await client.PostAsync(
            $"{ColaboradoresEndpoint}/{empleadoId}/acceso/reenviar", null),
            HttpStatusCode.UnprocessableEntity, "COLABORADOR_INACTIVO");
        await AssertProblemAsync(await client.PostAsync(
            $"/api/v1/identidad/usuarios/{usuarioId}/reactivar", null),
            HttpStatusCode.UnprocessableEntity, "COLABORADOR_INACTIVO");
        Assert.Equal(correosAntes, _host.Correo.Enviados.Count(c => c.Upn == upn));
    }

    [Fact]
    public async Task Baja_Antes_Del_Worker_No_Crea_Cuenta_Ni_Envia_Correo()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        var (empleadoId, usuarioId) = await AltaCuentaNuevaAsync(client, org, upn);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/v1/admin/empleados/{empleadoId}/desactivar", null)).StatusCode);
        await CorrerWorkerAsync();

        Assert.Null(await _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>()
            .BuscarPorCorreoAsync(upn, CancellationToken.None));
        Assert.DoesNotContain(_host.Correo.Enviados, c => c.Upn == upn);
        Assert.False((await LeerUsuarioAsync(usuarioId)).Activo);
    }

    [Fact]
    public async Task Reenviar_Mientras_Se_Provisiona_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var (empleadoId, _) = await AltaCuentaNuevaAsync(client, org, NuevoUpn());

        var response = await client.PostAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso/reenviar", null);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "USUARIO_ACCESO_NO_REENVIABLE");
    }

    [Fact]
    public async Task Reintentar_Sin_Error_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var (empleadoId, _) = await AltaCuentaNuevaAsync(client, org, NuevoUpn());

        var response = await client.PostAsync($"{ColaboradoresEndpoint}/{empleadoId}/acceso/reintentar", null);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "USUARIO_NO_EN_ERROR_PROVISION");
    }

    [Fact]
    public async Task Correo_Que_Ya_Existe_En_Entra_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var upn = NuevoUpn();
        await _host.Factory.Services.GetRequiredService<IEntraDirectorioPort>().CrearCuentaAsync(
            new SolicitudCuentaEntra(upn, "Ya Existe", EmailContacto, upn), CancellationToken.None);

        var response = await PostAltaAsync(client, org, CuentaNueva, upn, org.RolId);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ENTRA_UPN_EN_USO");
    }

    [Fact]
    public async Task Sin_Correo_De_Contacto_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);

        var response = await client.PostAsJsonAsync(ColaboradoresEndpoint, new
        {
            Id = Guid.Empty,
            Clave = RandomClave("CLB"),
            Nombre = "Colaborador de Prueba",
            org.SucursalId,
            org.DepartamentoId,
            org.PuestoId,
            Acceso = CuentaNueva,
            CorreoCorporativo = NuevoUpn(),
            RolId = org.RolId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Falla_Del_Empleado_No_Deja_Usuario_En_Provision()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var clave = RandomClave("CLB");
        await AltaCuentaNuevaAsync(client, org, NuevoUpn(), clave);
        var upn = NuevoUpn();

        // Misma clave de empleado: el alta revienta después de crear el usuario.
        var response = await PostAltaAsync(client, org, CuentaNueva, upn, org.RolId, clave);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = _host.Factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        Assert.False(await identidad.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == upn));
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static string NuevoUpn() => $"nuevo-{RandomSufijo()}@millet.mx";

    private static async Task<(Guid EmpleadoId, Guid UsuarioId)> AltaCuentaNuevaAsync(
        HttpClient client, Organizacion org, string upn, string? clave = null)
    {
        var response = await PostAltaAsync(client, org, CuentaNueva, upn, org.RolId, clave);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        return (
            body.GetProperty("empleado").GetProperty("id").GetGuid(),
            body.GetProperty("acceso").GetProperty("usuarioId").GetGuid());
    }

    private Task<int> CorrerWorkerAsync() =>
        ActivatorUtilities.CreateInstance<ProvisionCuentaEntraWorker>(_host.Factory.Services)
            .ProcesarPendientesAsync(CancellationToken.None);

    private async Task<Usuario> LeerUsuarioAsync(Guid usuarioId)
    {
        using var scope = _host.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        return await identidad.Usuarios.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == usuarioId);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _host.Factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Host con correo saliente controlable.</summary>
    public sealed class HostCuentaNueva : IDisposable
    {
        public CorreoControlado Correo { get; } = new();

        public WebApplicationFactory<Program> Factory { get; }

        public HostCuentaNueva()
        {
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.ConfigureTestServices(s =>
                {
                    s.RemoveAll<ICorreoSalientePort>();
                    s.AddSingleton<ICorreoSalientePort>(Correo);
                });
            });
        }

        public void Dispose() => Factory.Dispose();
    }

    public sealed class CorreoControlado : ICorreoSalientePort
    {
        private readonly ConcurrentQueue<CorreoAccesoColaborador> _enviados = new();

        private volatile bool _fallar;

        public bool Fallar
        {
            get => _fallar;
            set => _fallar = value;
        }

        public IReadOnlyCollection<CorreoAccesoColaborador> Enviados => _enviados;

        public Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
        {
            if (_fallar) throw new InvalidOperationException("El buzón de servicio rechazó el envío.");
            _enviados.Enqueue(correo);
            return Task.CompletedTask;
        }
    }
}
