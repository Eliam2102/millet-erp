using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Domain.Adjuntos;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;

/// <summary>
/// Criterios de aceptación de la ficha F1-ADM-11 G1.2 sobre el expediente documental de proveedor:
/// G1.2-a (403 sin permiso), G1.2-b (vigencia por fecha), G1.2-c (baja con motivo y bitácora) y CA2.2
/// (expediente completo).
/// </summary>
public class ProveedorAdjuntosCriteriosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public ProveedorAdjuntosCriteriosTests(WebApplicationFactory<Program> factory) => _base = factory;

    [Fact]
    public async Task G12a_Sin_Permiso_Ver_Responde_403_En_Listar_Contenido_Enlace_Y_Expediente()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);

        // Rol con acceso al módulo (gestionar) pero SIN adjuntos-ver.
        var (sinVer, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.GetAsync($"{Base}/{prov}/adjuntos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/contenido")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.GetAsync($"{Base}/{prov}/expediente")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinVer.GetAsync("/api/v1/adjuntos/tipos?entidad=proveedor")).StatusCode);
    }

    [Fact]
    public async Task G12a_Permisos_Se_Evaluan_Por_Operacion()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);

        // Solo ver: lista y descarga, pero no sube, no da de baja, no incluye bajas ni lee bitácora.
        var (soloVer, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer);
        Assert.Equal(HttpStatusCode.OK, (await soloVer.GetAsync($"{Base}/{prov}/adjuntos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await soloVer.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/contenido")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SubirAsync(soloVer, prov, TipoContrato)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVer.GetAsync($"{Base}/{prov}/adjuntos?incluirBajas=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVer.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/bitacora")).StatusCode);
        var baja = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/{prov}/adjuntos/{adjuntoId}")
        {
            Content = JsonContent.Create(new { motivo = "Intento sin permiso de baja" }),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVer.SendAsync(baja)).StatusCode);
        Assert.Equal(1, await amb.ContarAdjuntosAsync(prov));
    }

    [Fact]
    public async Task G12b_Constancia_Vigente_Al_31Oct_Aparece_Vencida_El_1Nov()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync(); // login con el reloj real, antes de moverlo
        var prov = await amb.SeedProveedorAsync();

        // 31-oct 12:00 hora de México (UTC-6, sin horario de verano).
        amb.Reloj.UtcNow = new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);
        var adjuntoId = await SubirOkAsync(admin, prov, TipoConstancia, vigenteHasta: "2026-10-31");

        var el31 = await EstadoAsync(admin, prov, adjuntoId);
        Assert.NotEqual(EstadoAdjunto.Vencido, el31);
        Assert.Equal(EstadoAdjunto.PorVencer, el31);

        amb.Reloj.UtcNow = new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(EstadoAdjunto.Vencido, await EstadoAsync(admin, prov, adjuntoId));

        // El expediente lo refleja: la constancia vencida deja de contar como vigente.
        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.False(exp.GetProperty("completo").GetBoolean());
        Assert.Contains("constancia_situacion_fiscal",
            exp.GetProperty("vencidos").EnumerateArray().Select(e => e.GetString()));
        var doc = exp.GetProperty("documentos").EnumerateArray()
            .Single(d => d.GetProperty("codigo").GetString() == "constancia_situacion_fiscal");
        Assert.Equal(EstadoExpedienteDocumentoJson.Vencido, doc.GetProperty("estado").Deserialize<EstadoExpedienteDocumentoJson>(Json));
    }

    [Fact]
    public async Task G12b_Vigencia_Pasada_Al_Subir_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        amb.Reloj.UtcNow = new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

        var r = await SubirAsync(admin, prov, TipoConstancia, vigenteHasta: "2026-10-31");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("ADJUNTO_VIGENCIA_PASADA", await CodigoAsync(r));
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));
        Assert.Empty(amb.Blob.Blobs);
    }

    [Fact]
    public async Task G12b_Sin_Vigencia_Explicita_Se_Calcula_Con_Los_Meses_Del_Tipo()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        amb.Reloj.UtcNow = new DateTimeOffset(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);

        var constancia = await LeerAsync(await SubirAsync(admin, prov, TipoConstancia)); // 3 meses
        var contrato = await LeerAsync(await SubirAsync(admin, prov, TipoContrato));     // sin vigencia

        Assert.Equal("2027-01-15", constancia.GetProperty("vigenteHasta").GetString());
        Assert.Equal(JsonValueKind.Null, contrato.GetProperty("vigenteHasta").ValueKind);
        Assert.Equal(EstadoAdjunto.SinVigencia, contrato.GetProperty("estado").Deserialize<EstadoAdjunto>(Json));
    }

    [Fact]
    public async Task G12c_Baja_Con_Motivo_Queda_En_Bitacora_Y_El_Blob_Sigue_Fisicamente()
    {
        // Filesystem local real (no el doble en memoria) para comprobar la conservación física.
        var raiz = Path.Combine(Path.GetTempPath(), $"millet-adj-test-{Guid.NewGuid():N}");
        try
        {
            var amb = new AdjuntosProveedorAmbiente(_base, s => s.Configure<Millet.Compartido.Infrastructure.Blob.AdjuntosBlobStorageOptions>(
                o => o.RootPath = raiz), blobReal: true);
            var admin = await amb.SuperAdminAsync();
            var prov = await amb.SeedProveedorAsync();
            var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);
            Assert.Single(Directory.GetFiles(raiz, "*", SearchOption.AllDirectories));

            const string motivo = "Documento sustituido por una version vigente";
            var antes = DateTimeOffset.UtcNow.AddSeconds(-5);
            var baja = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/{prov}/adjuntos/{adjuntoId}")
            {
                Content = JsonContent.Create(new { motivo }),
            };
            var respBaja = await admin.SendAsync(baja);
            Assert.Equal(HttpStatusCode.OK, respBaja.StatusCode);
            var dto = await LeerAsync(respBaja);
            Assert.Equal(EstadoAdjunto.Baja, dto.GetProperty("estado").Deserialize<EstadoAdjunto>(Json));
            Assert.Equal(motivo, dto.GetProperty("bajaMotivo").GetString());
            var bajaPorId = dto.GetProperty("bajaPorId").GetGuid();

            // Quién, cuándo y por qué en la bitácora del adjunto.
            var bit = await admin.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/bitacora");
            Assert.Equal(HttpStatusCode.OK, bit.StatusCode);
            var items = (await LeerAsync(bit)).GetProperty("items").EnumerateArray().ToList();
            var entradaBaja = items.Single(i =>
                i.GetProperty("operacion").GetString() == "actualizar"
                && i.GetProperty("cambios").GetString()!.Contains(motivo));
            Assert.Equal(bajaPorId, entradaBaja.GetProperty("usuarioId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(entradaBaja.GetProperty("actorNombre").GetString()));
            Assert.True(entradaBaja.GetProperty("timestamp").GetDateTimeOffset() >= antes);
            Assert.Contains(items, i => i.GetProperty("operacion").GetString() == "crear");

            // El archivo físico sigue ahí; el adjunto ya no se descarga ni se lista por defecto.
            Assert.Single(Directory.GetFiles(raiz, "*", SearchOption.AllDirectories));
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/contenido")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null)).StatusCode);
            Assert.Empty((await LeerAsync(await admin.GetAsync($"{Base}/{prov}/adjuntos"))).EnumerateArray());
            var conBajas = (await LeerAsync(await admin.GetAsync($"{Base}/{prov}/adjuntos?incluirBajas=true"))).EnumerateArray().ToList();
            Assert.Single(conBajas);
            Assert.Equal(motivo, conBajas[0].GetProperty("bajaMotivo").GetString());
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public async Task G12c_Baja_Sin_Motivo_Responde_400_Repetida_422_Y_El_Blob_Se_Conserva()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);

        HttpRequestMessage Baja(string? motivo) => new(HttpMethod.Delete, $"{Base}/{prov}/adjuntos/{adjuntoId}")
        {
            Content = JsonContent.Create(new { motivo }),
        };

        // Motivo ausente o corto: validación de FluentValidation (400, como el resto del API).
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(Baja(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(Baja("abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Baja("Motivo valido de baja"))).StatusCode);

        var segunda = await admin.SendAsync(Baja("Segunda baja del mismo documento"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);
        Assert.Equal("ADJUNTO_YA_DADO_DE_BAJA", await CodigoAsync(segunda));

        Assert.Empty(amb.Blob.Eliminaciones);
        Assert.Single(amb.Blob.Blobs);
    }

    [Fact]
    public async Task Ca22_Cinco_Documentos_En_Persona_Moral_Dejan_El_Expediente_Completo()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral);

        var vacio = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.False(vacio.GetProperty("completo").GetBoolean());
        Assert.Equal(5, vacio.GetProperty("faltantes").GetArrayLength());

        foreach (var tipo in new[] { TipoConstancia, TipoContrato, TipoActa, TipoIdentificacion, TipoDomicilio })
        {
            await SubirOkAsync(admin, prov, tipo);
        }

        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.True(exp.GetProperty("completo").GetBoolean());
        Assert.Equal(0, exp.GetProperty("faltantes").GetArrayLength());
        Assert.Equal(5, exp.GetProperty("documentos").GetArrayLength());
    }

    [Fact]
    public async Task Ca22_Sin_Acta_Constitutiva_La_Persona_Moral_No_Esta_Completa()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral);

        foreach (var tipo in new[] { TipoConstancia, TipoContrato, TipoIdentificacion, TipoDomicilio })
        {
            await SubirOkAsync(admin, prov, tipo);
        }

        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.False(exp.GetProperty("completo").GetBoolean());
        Assert.Equal("acta_constitutiva", Assert.Single(exp.GetProperty("faltantes").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Ca22_Persona_Fisica_Se_Completa_Sin_Acta_Constitutiva()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Fisica);

        foreach (var tipo in new[] { TipoConstancia, TipoContrato, TipoIdentificacion, TipoDomicilio })
        {
            await SubirOkAsync(admin, prov, tipo);
        }

        var exp = await LeerAsync(await admin.GetAsync($"{Base}/{prov}/expediente"));
        Assert.True(exp.GetProperty("completo").GetBoolean());
        Assert.Equal(4, exp.GetProperty("documentos").GetArrayLength());
    }

    [Fact]
    public async Task Catalogo_De_Tipos_De_Proveedor_Trae_Los_Cinco_Tipos_Y_Rechaza_Entidad_Desconocida()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();

        var tipos = await LeerAsync(await admin.GetAsync("/api/v1/adjuntos/tipos?entidad=proveedor"));
        Assert.True(tipos.GetArrayLength() >= 5);
        Assert.Contains("acta_constitutiva", tipos.EnumerateArray().Select(t => t.GetProperty("codigo").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/adjuntos/tipos?entidad=inexistente")).StatusCode);
    }

    [Fact]
    public async Task Descarga_Por_Enlace_Temporal_Entrega_El_Archivo_Sin_Autenticacion()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);

        var enlace = await LeerAsync(await admin.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null));
        var url = enlace.GetProperty("url").GetString()!;
        Assert.StartsWith("/api/v1/adjuntos/descargas/", url);
        Assert.True(enlace.GetProperty("expiraEn").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        using var anonimo = amb.Factory.CreateClient();
        var r = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf, await r.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("documento.pdf", r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Respuestas_No_Exponen_La_Ubicacion_Del_Blob()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);

        var meta = await (await admin.GetAsync($"{Base}/{prov}/adjuntos/{adjuntoId}")).Content.ReadAsStringAsync();
        var lista = await (await admin.GetAsync($"{Base}/{prov}/adjuntos")).Content.ReadAsStringAsync();
        var exp = await (await admin.GetAsync($"{Base}/{prov}/expediente")).Content.ReadAsStringAsync();
        foreach (var cuerpo in new[] { meta, lista, exp })
        {
            Assert.DoesNotContain("blobRef", cuerpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("blobUrl", cuerpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(amb.Blob.Blobs.Keys.Single(), cuerpo);
        }
    }

    // El expediente serializa su estado como número; se lee con este espejo para no depender del namespace.
    private enum EstadoExpedienteDocumentoJson { Faltante = 0, Vigente = 1, PorVencer = 2, Vencido = 3 }
}
