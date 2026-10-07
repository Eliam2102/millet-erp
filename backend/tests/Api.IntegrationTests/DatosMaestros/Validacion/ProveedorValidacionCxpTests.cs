using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros.Validacion;

/// <summary>
/// Pruebas de integración para la ficha F1-ADM-05 / G1.1:
/// - Alta de proveedor nace en 'EnRevision' (G1.1-a).
/// - 403 Forbidden para usuarios sin permiso 'datos_maestros.proveedores.validar' (G1.1-c).
/// - 422 Expediente incompleto si faltan documentos obligatorios (CA2.2).
/// - 200 OK y transición a 'Activo' cuando el expediente está completo (CA2.1/CA2.2).
/// - 200 OK y transición a 'Inactivo' con motivo al rechazar (G1.1-e).
/// </summary>
public class ProveedorValidacionCxpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public ProveedorValidacionCxpTests(WebApplicationFactory<Program> factory) => _base = factory;

    [Fact]
    public async Task G11a_Alta_Proveedor_Nace_En_Estado_EnRevision()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();

        var clave = $"PRV{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var payload = new
        {
            clave = clave,
            razonSocial = $"Proveedor EnRevision {clave} SA de CV",
            rfc = $"REV{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            tipoPersona = 0, // Moral
            condicionesPagoDias = 30
        };

        var response = await admin.PostAsJsonAsync("/api/v1/catalogos/proveedores", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await LeerAsync(response);
        var proveedorId = json.GetProperty("id").GetGuid();

        // Consultamos el detalle del proveedor
        var getResponse = await admin.GetAsync($"{Base}/{proveedorId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getJson = await LeerAsync(getResponse);
        Assert.Equal((int)EstatusCatalogo.EnRevision, getJson.GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task G11c_Sin_Permiso_Validar_Responde_403_En_Validar_Y_Rechazar()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        // Usuario con permisos de Compras/Gestión de proveedores pero SIN datos_maestros.proveedores.validar
        var (comprasClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar,
            PermisosCanonicos.CompartidoCatalogosAdministrar);

        var resValidar = await comprasClient.PostAsync($"{Base}/{prov}/validar", null);
        Assert.Equal(HttpStatusCode.Forbidden, resValidar.StatusCode);

        var resRechazar = await comprasClient.PostAsJsonAsync($"{Base}/{prov}/rechazar", new { motivo = "Motivo de prueba sin permiso" });
        Assert.Equal(HttpStatusCode.Forbidden, resRechazar.StatusCode);
    }

    [Fact]
    public async Task G11d_Validar_Con_Expediente_Incompleto_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        var (cxpClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar,
            PermisosCanonicos.DatosMaestrosProveedoresValidar);

        // Expediente vacío -> faltan 5 documentos obligatorios para persona moral
        var response = await cxpClient.PostAsync($"{Base}/{prov}/validar", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await LeerAsync(response);
        Assert.Equal("PROVEEDOR_EXPEDIENTE_INCOMPLETO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task G11d_Validar_Con_Expediente_Completo_Pasa_A_Activo()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        var admin = await amb.SuperAdminAsync();

        // Subimos los 5 tipos obligatorios de persona moral
        var unMes = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd");
        await SubirOkAsync(admin, prov, TipoConstancia, vigenteHasta: unMes);
        await SubirOkAsync(admin, prov, TipoContrato);
        await SubirOkAsync(admin, prov, TipoActa);
        await SubirOkAsync(admin, prov, TipoIdentificacion);
        await SubirOkAsync(admin, prov, TipoDomicilio, vigenteHasta: unMes);

        // Verificamos que el expediente ahora está completo
        var expRes = await admin.GetAsync($"{Base}/{prov}/expediente");
        Assert.Equal(HttpStatusCode.OK, expRes.StatusCode);
        var expJson = await LeerAsync(expRes);
        Assert.True(expJson.GetProperty("completo").GetBoolean());

        // Validamos con usuario de CxP
        var (cxpClient, cxpUserId) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar,
            PermisosCanonicos.DatosMaestrosProveedoresValidar);

        var response = await cxpClient.PostAsync($"{Base}/{prov}/validar", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verificamos estatus Activo y auditoría en el detalle
        var detalleRes = await cxpClient.GetAsync($"{Base}/{prov}");
        Assert.Equal(HttpStatusCode.OK, detalleRes.StatusCode);
        var detJson = await LeerAsync(detalleRes);

        Assert.Equal((int)EstatusCatalogo.Activo, detJson.GetProperty("estatus").GetInt32());
        Assert.NotNull(detJson.GetProperty("validadoEn").GetString());
    }

    [Fact]
    public async Task G11e_Rechazar_Con_Motivo_Valido_Pasa_A_Inactivo()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        var (cxpClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar,
            PermisosCanonicos.DatosMaestrosProveedoresValidar);

        var motivo = "Expediente incompleto y CSF no corresponde al RFC registrado.";
        var response = await cxpClient.PostAsJsonAsync($"{Base}/{prov}/rechazar", new { motivo });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verificamos estatus Inactivo y motivo en el detalle
        var detalleRes = await cxpClient.GetAsync($"{Base}/{prov}");
        Assert.Equal(HttpStatusCode.OK, detalleRes.StatusCode);
        var detJson = await LeerAsync(detalleRes);

        Assert.Equal((int)EstatusCatalogo.Inactivo, detJson.GetProperty("estatus").GetInt32());
        Assert.Equal(motivo, detJson.GetProperty("motivoRechazo").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    public async Task G11e_Rechazar_Con_Motivo_Invalido_Responde_400_O_422(string motivoInvalido)
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        var (cxpClient, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar,
            PermisosCanonicos.DatosMaestrosProveedoresValidar);

        var response = await cxpClient.PostAsJsonAsync($"{Base}/{prov}/rechazar", new { motivo = motivoInvalido });
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
