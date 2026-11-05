using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;

/// <summary>86e3yzemu: Tax ID extranjero sin duplicar, revisión por documento y excepción pendiente.</summary>
[Collection(ApiCompartida.Nombre)]
public class ProveedorTaxIdRevisionExcepcionTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public ProveedorTaxIdRevisionExcepcionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static async Task ExtranjeroAsync(HttpClient admin, Guid prov)
    {
        var put = await admin.PutAsJsonAsync($"{Base}/{prov}/clasificacion-fiscal",
            new { origen = OrigenProveedor.Extranjero, regimenFiscalCodigo = (string?)null, conceptoRetencion = "SIN_RETENCION" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
    }

    [Fact]
    public async Task Tax_ID_repetido_se_rechaza_y_el_listado_lo_encuentra()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var a = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        var b = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        await ExtranjeroAsync(admin, a); await ExtranjeroAsync(admin, b);
        var taxId = "TX-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/catalogos/proveedores/{a}", new { taxId })).StatusCode);
        var dup = await admin.PatchAsJsonAsync($"/api/v1/catalogos/proveedores/{b}", new { taxId = taxId.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("PROVEEDOR_TAXID_DUPLICADO", (await LeerAsync(dup)).GetProperty("code").GetString());

        var lista = await LeerAsync(await admin.GetAsync($"{Base}?q={taxId[3..8]}"));
        var item = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.Equal(taxId, item.GetProperty("taxId").GetString());
    }

    [Fact]
    public async Task Documento_revisado_muestra_quien_cuando_y_resultado()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        var adjunto = await SubirOkAsync(admin, prov, TipoActa);
        var ruta = $"{Base}/{prov}/expediente/documentos/{adjunto}/revisar";

        var sinObs = await admin.PostAsJsonAsync(ruta, new { resultado = 2 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinObs.StatusCode);
        var ok = await admin.PostAsJsonAsync(ruta, new { resultado = 1 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        var acta = exp.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("tipoDocumentoId").GetGuid() == TipoActa).GetProperty("actual");
        Assert.Equal(1, acta.GetProperty("resultadoRevision").GetInt32());
        Assert.False(string.IsNullOrEmpty(acta.GetProperty("revisadoPorNombre").GetString()));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, acta.GetProperty("revisadoEn").ValueKind);
    }

    [Fact]
    public async Task Excepcion_pendiente_la_resuelve_otro_usuario_con_su_nombre_y_fecha()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        var pide = await admin.PostAsJsonAsync($"{Base}/{prov}/expediente/excepciones",
            new { tipoDocumentoId = TipoContrato, motivo = "Proveedor local sin contrato formal" });
        var id = (await LeerAsync(pide)).GetProperty("id").GetGuid();
        var ruta = $"{Base}/{prov}/expediente/excepciones/{id}/resolver";

        var propia = await admin.PostAsJsonAsync(ruta, new { otorgar = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
        Assert.Equal("ADJUNTO_EXCEPCION_MISMO_USUARIO", (await LeerAsync(propia)).GetProperty("code").GetString());

        var (cxp, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresValidar);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cxp.PostAsJsonAsync(ruta, new { otorgar = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await cxp.PostAsJsonAsync(ruta, new { otorgar = false, motivo = "Sin contrato no se paga" })).StatusCode);

        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        var contrato = exp.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("codigo").GetString() == "contrato");
        Assert.Equal(0, contrato.GetProperty("estado").GetInt32()); // sigue faltando
        var e = contrato.GetProperty("excepcion");
        Assert.Equal(2, e.GetProperty("estado").GetInt32()); // Negada
        Assert.Equal("Usuario Adjuntos Proveedor", e.GetProperty("resueltaPorNombre").GetString());
    }
}
