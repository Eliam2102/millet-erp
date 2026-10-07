using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Adjuntos;
using static Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente;

namespace Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;

/// <summary>Casos negativos y de integridad del servicio de adjuntos sobre proveedor (F1-ADM-11 G1.2).</summary>
public class ProveedorAdjuntosNegativosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public ProveedorAdjuntosNegativosTests(WebApplicationFactory<Program> factory) => _base = factory;

    private static readonly byte[] CabeceraExe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00];

    private static async Task AssertRechazoAsync(
        AdjuntosProveedorAmbiente amb, Guid prov, HttpResponseMessage r, HttpStatusCode status, string codigo)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(codigo, await CodigoAsync(r));
        // Rechazo antes de tocar el storage: sin blob y sin fila.
        Assert.Equal(0, amb.Blob.Subidas);
        Assert.Empty(amb.Blob.Blobs);
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));
    }

    [Fact]
    public async Task Exe_Responde_422_Sin_Tocar_El_Storage()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        var r = await SubirAsync(admin, prov, TipoContrato, CabeceraExe, "malicioso.exe", "application/octet-stream");

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_FORMATO_NO_PERMITIDO");
    }

    [Fact]
    public async Task Mime_Falso_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        // Extensión .pdf pero MIME de ejecutable.
        var r = await SubirAsync(admin, prov, TipoContrato, Pdf, "factura.pdf", "application/x-msdownload");

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_FORMATO_NO_PERMITIDO");
    }

    [Fact]
    public async Task Pdf_Sin_Firma_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        // Ejecutable renombrado a .pdf con MIME application/pdf: la firma %PDF delata el engaño.
        var r = await SubirAsync(admin, prov, TipoContrato, CabeceraExe, "renombrado.pdf", "application/pdf");

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_FORMATO_NO_PERMITIDO");
    }

    [Fact]
    public async Task Mayor_A_10_MB_Responde_422_Para_Proveedor()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var grande = new byte[10 * 1024 * 1024 + 1];
        Pdf.CopyTo(grande, 0);

        var r = await SubirAsync(admin, prov, TipoContrato, grande, "grande.pdf", "application/pdf");

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_TAMANO_EXCEDIDO");
    }

    [Fact]
    public async Task Exactamente_10_MB_Se_Acepta()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var limite = new byte[10 * 1024 * 1024];
        Pdf.CopyTo(limite, 0);

        var r = await SubirAsync(admin, prov, TipoContrato, limite, "limite.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact]
    public async Task Xml_Binario_Responde_422_Y_Xml_Valido_Se_Acepta()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        var binario = await SubirAsync(admin, prov, TipoContrato, CabeceraExe, "datos.xml", "application/xml");
        await AssertRechazoAsync(amb, prov, binario, HttpStatusCode.UnprocessableEntity, "ADJUNTO_FORMATO_NO_PERMITIDO");

        var valido = await SubirAsync(
            admin, prov, TipoContrato, "<?xml version=\"1.0\"?><cfdi:Comprobante/>"u8.ToArray(), "cfdi.xml", "application/xml");
        Assert.Equal(HttpStatusCode.Created, valido.StatusCode);
    }

    [Fact]
    public async Task Archivo_Vacio_O_Ausente_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        var vacio = await SubirAsync(admin, prov, TipoContrato, [], "vacio.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, vacio.StatusCode);

        var form = new MultipartFormDataContent { { new StringContent(TipoContrato.ToString()), "tipoDocumentoId" } };
        var sinArchivo = await admin.PostAsync($"{Base}/{prov}/adjuntos", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinArchivo.StatusCode);
        Assert.Equal("ARCHIVO_REQUERIDO", await CodigoAsync(sinArchivo));
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));
    }

    [Fact]
    public async Task Tipo_Solo_Persona_Moral_En_Persona_Fisica_Responde_422()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(TipoPersonaProveedor.Fisica);

        var r = await SubirAsync(admin, prov, TipoActa);

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_TIPO_NO_APLICA");
    }

    [Fact]
    public async Task Tipo_De_Documento_Inexistente_Responde_404_Sin_Blob()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        var r = await SubirAsync(admin, prov, Guid.NewGuid());

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.NotFound, "ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Proveedor_Inexistente_Responde_404()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();

        var r = await SubirAsync(admin, Guid.NewGuid(), TipoContrato);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("PROVEEDOR_NO_ENCONTRADO", await CodigoAsync(r));
        Assert.Equal(0, amb.Blob.Subidas);
    }

    [Fact]
    public async Task Proveedor_Inactivo_No_Admite_Subida_Pero_Si_Lectura()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync(estatus: EstatusCatalogo.Inactivo);

        var r = await SubirAsync(admin, prov, TipoContrato);

        await AssertRechazoAsync(amb, prov, r, HttpStatusCode.UnprocessableEntity, "ADJUNTO_ENTIDAD_NO_ADMITE_SUBIDA");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Base}/{prov}/adjuntos")).StatusCode);
    }

    [Fact]
    public async Task Adjunto_De_Otro_Proveedor_Responde_404_En_Todas_Las_Operaciones_Idor()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var duenio = await amb.SeedProveedorAsync();
        var otro = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, duenio, TipoContrato);

        var ruta = $"{Base}/{otro}/adjuntos/{adjuntoId}";
        var respuestas = new[]
        {
            await admin.GetAsync(ruta),
            await admin.GetAsync($"{ruta}/contenido"),
            await admin.PostAsync($"{ruta}/enlace", null),
            await admin.GetAsync($"{ruta}/bitacora"),
            await admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, ruta)
            {
                Content = JsonContent.Create(new { motivo = "Intento sobre un adjunto ajeno" }),
            }),
        };
        foreach (var r in respuestas)
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("ADJUNTO_NO_ENCONTRADO", await CodigoAsync(r));
        }

        // El adjunto del dueño sigue intacto (sin baja).
        Assert.NotEqual(EstadoAdjunto.Baja, await EstadoAsync(admin, duenio, adjuntoId));
    }

    [Fact]
    public async Task Token_Manipulado_O_Basura_Responde_401_Con_Codigo()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);
        var url = (await LeerAsync(await admin.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null)))
            .GetProperty("url").GetString()!;

        using var anonimo = amb.Factory.CreateClient();
        var manipulado = url[..^2] + (url[^2] == 'A' ? "B" : "A") + url[^1];
        foreach (var candidata in new[] { manipulado, "/api/v1/adjuntos/descargas/token-que-no-existe" })
        {
            var r = await anonimo.GetAsync(candidata);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("ADJUNTO_ENLACE_INVALIDO", await CodigoAsync(r));
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Token_Expirado_Responde_401()
    {
        var amb = new AdjuntosProveedorAmbiente(
            _base, s => s.Configure<AdjuntoEnlaceOptions>(o => o.TtlSegundos = 1));
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);
        var url = (await LeerAsync(await admin.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null)))
            .GetProperty("url").GetString()!;

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        using var anonimo = amb.Factory.CreateClient();
        var r = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("ADJUNTO_ENLACE_INVALIDO", await CodigoAsync(r));
    }

    [Fact]
    public async Task Enlace_Emitido_Antes_De_La_Baja_Responde_404_Despues()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var adjuntoId = await SubirOkAsync(admin, prov, TipoContrato);
        var url = (await LeerAsync(await admin.PostAsync($"{Base}/{prov}/adjuntos/{adjuntoId}/enlace", null)))
            .GetProperty("url").GetString()!;

        var baja = await admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Base}/{prov}/adjuntos/{adjuntoId}")
        {
            Content = JsonContent.Create(new { motivo = "Documento erroneo, se baja" }),
        });
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);

        using var anonimo = amb.Factory.CreateClient();
        var r = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("ADJUNTO_NO_ENCONTRADO", await CodigoAsync(r));
    }

    [Fact]
    public async Task Sin_Idempotency_Key_Responde_400_Y_No_Sube_Nada()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        // Cliente sin el handler que inyecta la llave automáticamente.
        using var crudo = amb.Factory.CreateClient();
        crudo.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        var r = await crudo.SendAsync(PeticionSubida(prov, TipoContrato, Pdf, "sin-llave.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", await CodigoAsync(r));
        Assert.Equal(0, amb.Blob.Subidas);
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));
    }

    [Fact]
    public async Task Reintento_Con_La_Misma_Llave_No_Duplica_Fila_Ni_Blob()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        var llave = Guid.NewGuid().ToString("D");

        var r1 = await admin.SendAsync(PeticionSubida(prov, TipoContrato, Pdf, "idem.pdf", "application/pdf", idempotencyKey: llave));
        var r2 = await admin.SendAsync(PeticionSubida(prov, TipoContrato, Pdf, "idem.pdf", "application/pdf", idempotencyKey: llave));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal((await LeerAsync(r1)).GetProperty("id").GetGuid(), (await LeerAsync(r2)).GetProperty("id").GetGuid());
        Assert.Equal(1, await amb.ContarAdjuntosAsync(prov));
        Assert.Single(amb.Blob.Blobs);
        Assert.Equal(1, amb.Blob.Subidas);
    }

    [Fact]
    public async Task Fallo_Del_Blob_No_Deja_Huerfanos_Ni_Fila()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();
        amb.Blob.FallarAlSubir = true;

        var r = await SubirAsync(admin, prov, TipoContrato);

        Assert.True((int)r.StatusCode >= 500, $"Se esperaba 5xx y fue {(int)r.StatusCode}.");
        Assert.Equal(1, amb.Blob.Subidas);
        Assert.Empty(amb.Blob.Blobs); // el blob parcial se compensó
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));

        // Recuperado el storage, el mismo documento se sube normalmente.
        amb.Blob.FallarAlSubir = false;
        Assert.Equal(HttpStatusCode.Created, (await SubirAsync(admin, prov, TipoContrato)).StatusCode);
        Assert.Equal(1, await amb.ContarAdjuntosAsync(prov));
        Assert.Single(amb.Blob.Blobs);
    }

    [Fact]
    public async Task Fallo_De_BD_Despues_Del_Blob_Compensa_El_Blob_Y_No_Deja_Fila()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        // Tipo propio, retirado justo después de subir el blob: el INSERT viola la FK y la BD rechaza la fila.
        var tipoEfimero = Guid.NewGuid();
        using (var scope = amb.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            db.AdjuntoTiposDocumento.Add(new AdjuntoTipoDocumento(
                tipoEfimero, "proveedor", $"efimero_{tipoEfimero:N}", "Tipo efimero de prueba", 99, false, null, false));
            await db.SaveChangesAsync();
        }
        amb.Blob.DespuesDeSubir = async () =>
        {
            using var scope = amb.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            await db.AdjuntoTiposDocumento.Where(t => t.Id == tipoEfimero).ExecuteDeleteAsync();
        };

        var r = await SubirAsync(admin, prov, tipoEfimero);

        Assert.True((int)r.StatusCode >= 500, $"Se esperaba 5xx y fue {(int)r.StatusCode}.");
        Assert.Equal(1, amb.Blob.Subidas);
        Assert.Single(amb.Blob.Eliminaciones);
        Assert.Empty(amb.Blob.Blobs);
        Assert.Equal(0, await amb.ContarAdjuntosAsync(prov));
    }

    [Fact]
    public async Task Nombre_Con_Ruta_Se_Neutraliza_Al_Subir_Y_En_Content_Disposition()
    {
        var amb = new AdjuntosProveedorAmbiente(_base);
        var admin = await amb.SuperAdminAsync();
        var prov = await amb.SeedProveedorAsync();

        var r = await SubirAsync(admin, prov, TipoContrato, Pdf, "..\\..\\etc/passwd.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var dto = await LeerAsync(r);
        var nombre = dto.GetProperty("nombreArchivo").GetString()!;
        Assert.DoesNotContain("/", nombre);
        Assert.DoesNotContain("..\\", nombre);

        var contenido = await admin.GetAsync($"{Base}/{prov}/adjuntos/{dto.GetProperty("id").GetGuid()}/contenido");
        Assert.Equal(HttpStatusCode.OK, contenido.StatusCode);
        var cd = string.Join(";", contenido.Content.Headers.GetValues("Content-Disposition"));
        Assert.DoesNotContain("../", cd);
        Assert.DoesNotContain("\r", cd);
        Assert.DoesNotContain("\n", cd);
    }
}
