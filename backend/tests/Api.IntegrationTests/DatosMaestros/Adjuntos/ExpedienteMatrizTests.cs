using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;

/// <summary>86e3pdmee · ADM-01: matriz por tipo de proveedor y excepción con permiso y motivo.</summary>
[Collection(ApiCompartida.Nombre)]
public class ExpedienteMatrizTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public ExpedienteMatrizTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static IEnumerable<string> Codigos(System.Text.Json.JsonElement exp) =>
        exp.GetProperty("documentos").EnumerateArray().Select(d => d.GetProperty("codigo").GetString()!);

    [Fact]
    public async Task Extranjero_no_pide_constancia_y_la_excepcion_completa_el_expediente()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);

        var put = await admin.PutAsJsonAsync($"{Base}/{prov}/clasificacion-fiscal",
            new { origen = OrigenProveedor.Extranjero, regimenFiscalCodigo = (string?)null, conceptoRetencion = "SIN_RETENCION" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.DoesNotContain("constancia_situacion_fiscal", Codigos(exp));
        Assert.Contains("contrato", exp.GetProperty("faltantes").EnumerateArray().Select(f => f.GetString()));

        // Sin contrato (proveedor chico): excepción con motivo, el contrato deja de faltar.
        var sinMotivo = await admin.PostAsJsonAsync($"{Base}/{prov}/expediente/excepciones", new { tipoDocumentoId = TipoContrato, motivo = "corto" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinMotivo.StatusCode);
        var excepcion = await admin.PostAsJsonAsync($"{Base}/{prov}/expediente/excepciones",
            new { tipoDocumentoId = TipoContrato, motivo = "Proveedor local chico sin contrato formal" });
        Assert.Equal(HttpStatusCode.Created, excepcion.StatusCode);
        exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.DoesNotContain("contrato", exp.GetProperty("faltantes").EnumerateArray().Select(f => f.GetString()));
        var contrato = exp.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("codigo").GetString() == "contrato");
        Assert.Equal(4, contrato.GetProperty("estado").GetInt32()); // Exceptuado
    }

    [Fact]
    public async Task Configurar_la_matriz_requiere_permiso()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        var (cxp, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresGestionar);
        var tipos = await LeerAsync(await cxp.GetAsync($"{Base}/expediente/tipos"));
        Assert.Contains("opinion_cumplimiento", tipos.EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));

        var alta = await cxp.PostAsJsonAsync($"{Base}/expediente/tipos",
            new { codigo = "fix_doc", nombre = "FIX documento", obligatorioPersonaMoral = false, obligatorioPersonaFisica = false, vigenciaMeses = (int?)null, soloPersonaMoral = false, aplicaOrigen = 0, activo = true });
        Assert.Equal(HttpStatusCode.Forbidden, alta.StatusCode);
    }
}
