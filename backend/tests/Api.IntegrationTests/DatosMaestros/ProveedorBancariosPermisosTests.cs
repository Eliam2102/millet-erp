using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// 86e3pdmee · ADM-02: varias cuentas bancarias por proveedor con validación.
/// - Captura con <c>bancarios-editar</c>: la cuenta queda «Por validar».
/// - Valida un administrador (<c>cuentas-administrar</c>) distinto de quien capturó, con estado de cuenta vigente.
/// - Sin permiso no se ve ni se captura; sin <c>cuentas-administrar</c> el número va enmascarado.
/// - La bitácora nunca lleva la CLABE completa.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ProveedorBancariosPermisosTests
{
    private const string Clabe = "012180000000001234";
    private static readonly Guid TipoEstadoCuenta = Guid.Parse("00000011-0001-0000-0000-000000000006");
    private readonly WebApplicationFactory<Program> _factory;

    public ProveedorBancariosPermisosTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Cuentas(Guid proveedorId) => $"{AdjuntosProveedorAmbiente.Base}/{proveedorId}/cuentas-bancarias";

    private static HttpRequestMessage Accion(Guid proveedorId, Guid cuentaId, string ruta, int version, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Cuentas(proveedorId)}/{cuentaId}/{ruta}") { Content = JsonContent.Create(body) };
        req.Headers.Add("X-Expected-Version", version.ToString());
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return req;
    }

    [Fact]
    public async Task Tesoreria_captura_por_validar_y_otro_administrador_valida_con_estado_de_cuenta()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        var (tesoreria, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar);

        var alta = await tesoreria.PostAsJsonAsync(Cuentas(provId), new { banco = "BBVA", clabe = Clabe, moneda = "MXN", titular = "Proveedor SA" });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var cuenta = await AdjuntosProveedorAmbiente.LeerAsync(alta);
        Assert.Equal((int)EstadoCuentaBancariaProveedor.PorValidar, cuenta.GetProperty("estado").GetInt32());
        var cuentaId = cuenta.GetProperty("id").GetGuid();

        var duplicada = await tesoreria.PostAsJsonAsync(Cuentas(provId), new { banco = "BBVA", clabe = Clabe, moneda = "MXN", titular = "Proveedor SA" });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);

        // Tesorería no puede validar (no tiene cuentas-administrar) y ve el número enmascarado.
        var sinPermiso = await tesoreria.SendAsync(Accion(provId, cuentaId, "validar", cuenta.GetProperty("version").GetInt32(),
            new { documentoAdjuntoId = Guid.NewGuid(), cotejoCompleto = true }));
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        var lista = await AdjuntosProveedorAmbiente.LeerAsync(await tesoreria.GetAsync(Cuentas(provId)));
        Assert.Matches(@"^\*+1234$", lista[0].GetProperty("numero").GetString()!);

        using var admin = await amb.SuperAdminAsync();
        var documento = await AdjuntosProveedorAmbiente.SubirOkAsync(admin, provId, TipoEstadoCuenta);
        var (validador, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.DatosMaestrosProveedoresCuentasAdministrar);
        var validar = await validador.SendAsync(Accion(provId, cuentaId, "validar", cuenta.GetProperty("version").GetInt32(),
            new { documentoAdjuntoId = documento, cotejoCompleto = true, nota = "Llamada al teléfono registrado" }));
        Assert.Equal(HttpStatusCode.OK, validar.StatusCode);
        var validada = await AdjuntosProveedorAmbiente.LeerAsync(validar);
        Assert.Equal((int)EstadoCuentaBancariaProveedor.Activa, validada.GetProperty("estado").GetInt32());
        Assert.True(validada.GetProperty("predeterminada").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var bitacora = await core.AuditLog.Where(a => a.AggregateRootId == provId && a.Operacion.StartsWith("proveedor.cuenta-")).ToListAsync();
        Assert.Equal(2, bitacora.Count);
        Assert.All(bitacora, a => Assert.DoesNotContain(Clabe, a.Resumen + a.Cambios + a.EntidadEtiqueta));
    }

    [Fact]
    public async Task Quien_capturo_no_valida_aunque_sea_administrador()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        using var admin = await amb.SuperAdminAsync();
        var alta = await AdjuntosProveedorAmbiente.LeerAsync(await admin.PostAsJsonAsync(Cuentas(provId),
            new { banco = "Banorte", clabe = "072180000000004569", moneda = "MXN", titular = "Proveedor SA" }));
        var documento = await AdjuntosProveedorAmbiente.SubirOkAsync(admin, provId, TipoEstadoCuenta);

        var validar = await admin.SendAsync(Accion(provId, alta.GetProperty("id").GetGuid(), "validar", alta.GetProperty("version").GetInt32(),
            new { documentoAdjuntoId = documento, cotejoCompleto = true }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, validar.StatusCode);
        Assert.Equal("PROVEEDOR_CUENTA_MISMA_PERSONA", await AdjuntosProveedorAmbiente.CodigoAsync(validar));
    }

    [Fact]
    public async Task Compras_sin_permisos_bancarios_responde_403_al_ver_y_al_capturar()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        var (compras, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        Assert.Equal(HttpStatusCode.Forbidden, (await compras.GetAsync(Cuentas(provId))).StatusCode);
        var alta = await compras.PostAsJsonAsync(Cuentas(provId), new { banco = "BBVA", clabe = Clabe, moneda = "MXN", titular = "Proveedor SA" });
        Assert.Equal(HttpStatusCode.Forbidden, alta.StatusCode);
    }

    [Fact]
    public async Task Tesoreria_con_bancarios_editar_no_puede_marcar_predeterminada_responde_403()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        var (tesoreria, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar);

        var r = await tesoreria.SendAsync(Accion(provId, Guid.NewGuid(), "predeterminada", 1, new { }));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        // Con cuentas-administrar el permiso alcanza (la cuenta no existe: 404, no 403).
        var (admin, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresCuentasAdministrar);
        var r2 = await admin.SendAsync(Accion(provId, Guid.NewGuid(), "predeterminada", 1, new { }));
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
    }
}
