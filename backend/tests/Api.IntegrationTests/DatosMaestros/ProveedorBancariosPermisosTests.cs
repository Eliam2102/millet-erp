using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// Pruebas de integración de permisos bancarios de proveedor por rol (G1.9 / F1-ADM-05).
/// Criterios de aceptación:
/// - G1.9-a: Rol CxP ve cuenta enmascarada y PATCH responde 403.
/// - G1.9-b: Rol Tesorería edita datos bancarios -> 204, audita en core.audit_log sin CLABE completa, re-envío idéntico no duplica auditoría.
/// - G1.9-c: Perfil sin permisos bancarios (Compras) -> GET y PATCH responden 403.
/// </summary>
public class ProveedorBancariosPermisosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProveedorBancariosPermisosTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task G19a_Cxp_Ve_Clabe_Enmascarada_Y_Patch_Responde_403()
    {
        var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

        // Pre-cargar datos bancarios con SuperAdmin
        var admin = await amb.SuperAdminAsync();
        var patchInicial = await admin.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new
            {
                banco = "BBVA",
                clabe = "012345678901234567",
                beneficiario = "Beneficiario Uno",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchInicial.StatusCode);

        // Cliente con permisos exactos del rol cxp
        var (cxpClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.DatosMaestrosProveedoresValidar,
            PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        // GET: 200 con CLABE enmascarada
        var getResp = await cxpClient.GetAsync($"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getJson = await AdjuntosProveedorAmbiente.LeerAsync(getResp);
        var clabe = getJson.GetProperty("clabe").GetString();
        Assert.NotNull(clabe);
        Assert.Matches(@"^\*+\d{4}$", clabe);
        Assert.EndsWith("4567", clabe);
        Assert.False(getJson.GetProperty("clabeCompleta").GetBoolean());

        // PATCH: 403 Forbidden
        var patchResp = await cxpClient.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new { banco = "Banorte", clabe = "987654321098765432" });
        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
    }

    [Fact]
    public async Task G19b_Tesoreria_Edita_Datos_Bancarios_204_Y_Audita_Sin_Clabe_Completa()
    {
        var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

        // Pre-cargar datos bancarios iniciales con SuperAdmin
        var admin = await amb.SuperAdminAsync();
        var clabeInicial = "012345678901234567";
        var patchInicial = await admin.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new
            {
                banco = "BBVA",
                clabe = clabeInicial,
                beneficiario = "Beneficiario Inicial",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchInicial.StatusCode);

        // Limpiar o contar auditorías previas de la inicial
        int logsPrevios;
        using (var scope = _factory.Services.CreateScope())
        {
            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            logsPrevios = await coreDb.AuditLog.CountAsync(a => a.EntidadId == provId && a.Operacion == "proveedor.bancarios-cambiados");
        }

        // Cliente con permisos exactos del rol tesoreria (SIN CompartidoCatalogosAdministrar)
        var (tesoreriaClient, usuarioTesoreriaId) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
            PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        var clabeNueva = "987654321098765432";
        var patchTesoreria = await tesoreriaClient.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new
            {
                banco = "Citibanamex",
                clabe = clabeNueva,
                beneficiario = "Beneficiario Nuevo",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchTesoreria.StatusCode);

        // GET devuelve la nueva CLABE enmascarada
        var getResp = await tesoreriaClient.GetAsync($"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getJson = await AdjuntosProveedorAmbiente.LeerAsync(getResp);
        Assert.Equal("Citibanamex", getJson.GetProperty("banco").GetString());
        var clabeObtenida = getJson.GetProperty("clabe").GetString();
        Assert.NotNull(clabeObtenida);
        Assert.EndsWith("5432", clabeObtenida);
        Assert.Matches(@"^\*+\d{4}$", clabeObtenida);

        // Verificar core.audit_log
        using (var scope = _factory.Services.CreateScope())
        {
            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var auditEntries = await coreDb.AuditLog
                .Where(a => a.EntidadId == provId && a.Operacion == "proveedor.bancarios-cambiados")
                .OrderBy(a => a.Timestamp)
                .ToListAsync();

            Assert.Equal(logsPrevios + 1, auditEntries.Count);
            // Se busca por usuario y no por Timestamp: el reloj de pruebas puede empatar las filas.
            var entry = Assert.Single(auditEntries, a => a.UsuarioId == usuarioTesoreriaId);
            Assert.Equal("proveedor.bancarios-cambiados", entry.Operacion);
            Assert.Equal("Proveedor", entry.Entidad);

            // Prohibido contener 18 dígitos seguidos
            Assert.DoesNotMatch(@"\d{18}", entry.Cambios);
            Assert.DoesNotMatch(@"\d{18}", entry.Resumen);

            // Contiene los últimos 4 anteriores y nuevos
            Assert.Contains("4567", entry.Cambios);
            Assert.Contains("5432", entry.Cambios);
            Assert.Contains("4567", entry.Resumen);
            Assert.Contains("5432", entry.Resumen);
        }

        // Repetir PATCH con los mismos valores -> 204 y NO crea segunda fila de auditoría
        var patchRepetido = await tesoreriaClient.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new
            {
                banco = "Citibanamex",
                clabe = clabeNueva,
                beneficiario = "Beneficiario Nuevo",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchRepetido.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var auditCount = await coreDb.AuditLog
                .CountAsync(a => a.EntidadId == provId && a.Operacion == "proveedor.bancarios-cambiados");
            Assert.Equal(logsPrevios + 1, auditCount);
        }
    }

    [Fact]
    public async Task G19c_Compras_Sin_Permisos_Bancarios_Responde_403_En_Get_Y_Patch()
    {
        var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

        // Cliente solo con gestión de proveedores (perfil Compras)
        var (comprasClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        // GET datos bancarios -> 403 Forbidden
        var getResp = await comprasClient.GetAsync($"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios");
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        // PATCH datos bancarios -> 403 Forbidden
        var patchResp = await comprasClient.PatchAsJsonAsync(
            $"{AdjuntosProveedorAmbiente.Base}/{provId}/datos-bancarios",
            new { banco = "Banco Prueba" });
        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
    }

    [Fact]
    public async Task G19_PatchGrueso_Catalogos_ConAdministrarSinBancariosEditar_Responde_403_SinPermiso()
    {
        var amb = new AdjuntosProveedorAmbiente(_factory);
        var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

        // catalogos.administrar abre el PATCH grueso, pero sin bancarios-editar no debe tocar la CLABE.
        var (cliente, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CompartidoCatalogosAdministrar,
            PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        var resp = await cliente.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{provId}",
            new { clabe = "987654321098765432" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var json = await AdjuntosProveedorAmbiente.LeerAsync(resp);
        Assert.Equal("PROVEEDOR_BANCARIOS_SIN_PERMISO", json.GetProperty("code").GetString());
    }
}
