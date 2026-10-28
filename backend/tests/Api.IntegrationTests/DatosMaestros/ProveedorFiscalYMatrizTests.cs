using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// 86e3pdmee · ADM-01 (cierre de auditoría): RFC del extranjero, régimen contra tipo de persona,
/// matriz por tipo de persona y origen, validación sin clasificar y mensaje con nombres de documento.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ProveedorFiscalYMatrizTests
{
    private const string Catalogo = "/api/v1/catalogos/proveedores";
    private readonly WebApplicationFactory<Program> _factory;

    public ProveedorFiscalYMatrizTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static readonly string?[] ExpedienteNacionalMoral =
        ["acta_constitutiva", "comprobante_domicilio", "constancia_situacion_fiscal", "contrato", "identificacion_representante_legal"];

    private static readonly string?[] ExpedienteExtranjeroFisico =
        ["comprobante_domicilio", "contrato", "identificacion_representante_legal"];

    private static string Clave() => $"FX{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static IEnumerable<string?> Codigos(JsonElement exp, string campo) =>
        exp.GetProperty(campo).EnumerateArray().Select(d => d.GetString());

    [Fact]
    public async Task Alta_de_extranjero_persona_fisica_sin_rfc_guarda_el_generico_y_la_clasificacion_en_el_mismo_POST()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();

        var alta = await admin.PostAsJsonAsync(Catalogo, new
        {
            clave = Clave(), razonSocial = "John Smith Glass Supplies", tipoPersona = (int)TipoPersonaProveedor.Fisica,
            origen = (int)OrigenProveedor.Extranjero, conceptoRetencion = "SIN_RETENCION",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var id = (await LeerAsync(alta)).GetProperty("id").GetGuid();

        var detalle = await LeerAsync(await admin.GetAsync($"{Base}/{id}"));
        Assert.Equal("XEXX010101000", detalle.GetProperty("rfc").GetString());
        Assert.Equal((int)OrigenProveedor.Extranjero, detalle.GetProperty("origen").GetInt32());
        Assert.Equal("SIN_RETENCION", detalle.GetProperty("conceptoRetencion").GetString());

        // El genérico no entra en la unicidad: otro extranjero sin RFC también se da de alta.
        var otro = await admin.PostAsJsonAsync(Catalogo, new
        {
            clave = Clave(), razonSocial = "Otra Extranjera LLC", tipoPersona = (int)TipoPersonaProveedor.Moral,
            origen = (int)OrigenProveedor.Extranjero,
        });
        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);

        // Un nacional sin RFC sigue siendo inválido.
        var nacional = await admin.PostAsJsonAsync(Catalogo, new
        {
            clave = Clave(), razonSocial = "Nacional Sin Rfc", tipoPersona = (int)TipoPersonaProveedor.Moral,
            origen = (int)OrigenProveedor.Nacional, regimenFiscalCodigo = "601",
        });
        Assert.Equal(HttpStatusCode.BadRequest, nacional.StatusCode);
    }

    [Fact]
    public async Task Alta_nacional_con_regimen_de_otra_naturaleza_se_rechaza_y_con_el_correcto_se_guarda()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var rfc = $"RG{Guid.NewGuid():N}"[..13].ToUpperInvariant();

        var mal = await admin.PostAsJsonAsync(Catalogo, new
        {
            clave = Clave(), razonSocial = "Fisica con 601", rfc, tipoPersona = (int)TipoPersonaProveedor.Fisica,
            origen = (int)OrigenProveedor.Nacional, regimenFiscalCodigo = "601",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mal.StatusCode);
        Assert.Equal("PROVEEDOR_REGIMEN_NO_APLICA", await CodigoAsync(mal));

        var bien = await admin.PostAsJsonAsync(Catalogo, new
        {
            clave = Clave(), razonSocial = "Fisica con 612", rfc, tipoPersona = (int)TipoPersonaProveedor.Fisica,
            origen = (int)OrigenProveedor.Nacional, regimenFiscalCodigo = "612",
        });
        Assert.Equal(HttpStatusCode.Created, bien.StatusCode);
    }

    [Theory]
    [InlineData(TipoPersonaProveedor.Fisica, "601")]
    [InlineData(TipoPersonaProveedor.Moral, "612")]
    public async Task Clasificar_con_regimen_que_no_corresponde_al_tipo_de_persona_responde_422(TipoPersonaProveedor tipo, string regimen)
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(tipo, EstatusCatalogo.EnRevision, clasificar: false);

        var put = await admin.PutAsJsonAsync($"{Base}/{prov}/clasificacion-fiscal",
            new { origen = OrigenProveedor.Nacional, regimenFiscalCodigo = regimen, conceptoRetencion = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.Equal("PROVEEDOR_REGIMEN_NO_APLICA", await CodigoAsync(put));
    }

    [Fact]
    public async Task Validar_sin_clasificar_responde_422_clasificacion_pendiente()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision, clasificar: false);

        var r = await admin.PostAsync($"{Base}/{prov}/validar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("PROVEEDOR_CLASIFICACION_PENDIENTE", await CodigoAsync(r));
    }

    [Fact]
    public async Task Validar_con_faltante_nombra_el_documento_y_con_excepcion_otorgada_valida_y_registra_la_version_de_la_matriz()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        var unMes = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd");
        await SubirOkAsync(admin, prov, TipoConstancia, vigenteHasta: unMes);
        await SubirOkAsync(admin, prov, TipoActa);
        await SubirOkAsync(admin, prov, TipoIdentificacion);
        await SubirOkAsync(admin, prov, TipoDomicilio, vigenteHasta: unMes);

        var falta = await admin.PostAsync($"{Base}/{prov}/validar", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, falta.StatusCode);
        var problema = await LeerAsync(falta);
        Assert.Equal("PROVEEDOR_EXPEDIENTE_INCOMPLETO", problema.GetProperty("code").GetString());
        var mensaje = problema.ToString();
        Assert.Contains("faltan: Contrato", mensaje);
        Assert.DoesNotContain("faltantes: [", mensaje);

        var excepcion = await admin.PostAsJsonAsync($"{Base}/{prov}/expediente/excepciones",
            new { tipoDocumentoId = TipoContrato, motivo = "Proveedor local sin contrato formal" });
        Assert.Equal(HttpStatusCode.Created, excepcion.StatusCode);

        var ok = await admin.PostAsync($"{Base}/{prov}/validar", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var detalle = await LeerAsync(await admin.GetAsync($"{Base}/{prov}"));
        var version = detalle.GetProperty("validadoConMatrizVersion").GetInt32();
        Assert.True(version > 0);
        using var scope = _factory.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var bitacora = await core.AuditLog.SingleAsync(a => a.AggregateRootId == prov && a.Operacion == "proveedor.validado");
        Assert.Equal(version, JsonDocument.Parse(bitacora.Cambios).RootElement.GetProperty("matrizVersion").GetInt32());
    }

    [Fact]
    public async Task Matriz_por_tipo_de_persona_y_origen_nacional_moral_completa_y_extranjero_fisico_tiene_la_suya()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();

        // Nacional persona moral con régimen: los cinco documentos de siempre.
        var pm = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        var expPm = await LeerAsync(await admin.GetAsync($"{Base}/{pm}/expediente"));
        Assert.Equal(
            ExpedienteNacionalMoral,
            Codigos(expPm, "faltantes").Order().ToArray());
        var unMes = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd");
        await SubirOkAsync(admin, pm, TipoConstancia, vigenteHasta: unMes);
        await SubirOkAsync(admin, pm, TipoContrato);
        await SubirOkAsync(admin, pm, TipoActa);
        await SubirOkAsync(admin, pm, TipoIdentificacion);
        await SubirOkAsync(admin, pm, TipoDomicilio, vigenteHasta: unMes);
        Assert.True((await LeerAsync(await admin.GetAsync($"{Base}/{pm}/expediente"))).GetProperty("completo").GetBoolean());

        // Extranjero persona física: sin constancia del SAT ni acta ni opinión 32-D (solo nacional).
        var pf = await amb.SeedProveedorAsync(TipoPersonaProveedor.Fisica, EstatusCatalogo.EnRevision, clasificar: false);
        var put = await admin.PutAsJsonAsync($"{Base}/{pf}/clasificacion-fiscal",
            new { origen = OrigenProveedor.Extranjero, regimenFiscalCodigo = (string?)null, conceptoRetencion = "SIN_RETENCION" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var expPf = await LeerAsync(await admin.GetAsync($"{Base}/{pf}/expediente"));
        Assert.Equal(
            ExpedienteExtranjeroFisico,
            Codigos(expPf, "faltantes").Order().ToArray());
        var aplicables = expPf.GetProperty("documentos").EnumerateArray().Select(d => d.GetProperty("codigo").GetString()).ToList();
        Assert.DoesNotContain("constancia_situacion_fiscal", aplicables);
        Assert.DoesNotContain("acta_constitutiva", aplicables);
        Assert.DoesNotContain("opinion_cumplimiento", aplicables);
    }

    [Fact]
    public async Task Cambiar_la_matriz_por_tipo_de_persona_cambia_el_expediente_y_sube_la_version()
    {
        await using var amb = new AdjuntosProveedorAmbiente(_factory);
        using var admin = await amb.SuperAdminAsync();
        var pf = await amb.SeedProveedorAsync(TipoPersonaProveedor.Fisica, EstatusCatalogo.EnRevision);

        var tipos = await LeerAsync(await admin.GetAsync($"{Base}/expediente/tipos"));
        var contrato = tipos.EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "contrato");
        Assert.True(contrato.GetProperty("obligatorioPersonaFisica").GetBoolean());
        var version = contrato.GetProperty("version").GetInt32();

        // El contrato deja de ser obligatorio para personas físicas (sigue para morales).
        var req = new HttpRequestMessage(HttpMethod.Put, $"{Base}/expediente/tipos/{TipoContrato}")
        {
            Content = JsonContent.Create(new
            {
                nombre = "Contrato", obligatorioPersonaMoral = true, obligatorioPersonaFisica = false,
                vigenciaMeses = (int?)null, soloPersonaMoral = false, aplicaOrigen = 0, activo = true,
            }),
        };
        req.Headers.Add("X-Expected-Version", version.ToString());
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(req)).StatusCode);
            var expPf = await LeerAsync(await admin.GetAsync($"{Base}/{pf}/expediente"));
            Assert.DoesNotContain("contrato", Codigos(expPf, "faltantes"));
            var pm = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
            var expPm = await LeerAsync(await admin.GetAsync($"{Base}/{pm}/expediente"));
            Assert.Contains("contrato", Codigos(expPm, "faltantes"));

            var despues = (await LeerAsync(await admin.GetAsync($"{Base}/expediente/tipos"))).EnumerateArray()
                .Single(t => t.GetProperty("codigo").GetString() == "contrato");
            Assert.Equal(version + 1, despues.GetProperty("version").GetInt32());
        }
        finally
        {
            // La matriz es global a la BD de pruebas: se restaura para no afectar otros tests.
            var actual = (await LeerAsync(await admin.GetAsync($"{Base}/expediente/tipos"))).EnumerateArray()
                .Single(t => t.GetProperty("codigo").GetString() == "contrato").GetProperty("version").GetInt32();
            var restaurar = new HttpRequestMessage(HttpMethod.Put, $"{Base}/expediente/tipos/{TipoContrato}")
            {
                Content = JsonContent.Create(new
                {
                    nombre = "Contrato", obligatorioPersonaMoral = true, obligatorioPersonaFisica = true,
                    vigenciaMeses = (int?)null, soloPersonaMoral = false, aplicaOrigen = 0, activo = true,
                }),
            };
            restaurar.Headers.Add("X-Expected-Version", actual.ToString());
            restaurar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            await admin.SendAsync(restaurar);
        }
    }
}
