using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc.EnviarAAutorizacion;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// Campos de comercio exterior de la OC (supuestos Vilo, PLATFORM-TODO(F1-COM-06)).
public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task InformacionImportacion_Patch_Persiste_CamposComercioExterior()
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearOcImportacionAsync(client);

        var patch = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/informacion-importacion",
            new
            {
                IncotermId = Guid.NewGuid(),
                PaisOrigen = "US",
                NumeroContenedor = "CONT-CE-1",
                CodigoRuta = "RUTA-CE",
                SemanaEmbarque = "S42",
                NumeroPedimento = (string?)null,
                TipoOperacion = (int?)1,
                NumeroBl = "BL-CE-001",
                TipoTransporte = (int?)0,
                AgenteAduanalNaviera = "Agencia CE",
                PuertoAduanaEntrada = "Manzanillo",
                FechaEstimadaArribo = (DateOnly?)new DateOnly(2026, 11, 3),
                CertificadoOrigen = true,
            });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{id}"));
        Assert.Equal(1, detalle.GetProperty("infoImportTipoOperacion").GetInt32());
        Assert.Equal("BL-CE-001", detalle.GetProperty("infoImportNumeroBl").GetString());
        Assert.Equal(0, detalle.GetProperty("infoImportTipoTransporte").GetInt32());
        Assert.Equal("Agencia CE", detalle.GetProperty("infoImportAgenteAduanalNaviera").GetString());
        Assert.Equal("Manzanillo", detalle.GetProperty("infoImportPuertoAduanaEntrada").GetString());
        Assert.Equal("2026-11-03", detalle.GetProperty("infoImportFechaEstimadaArribo").GetString());
        Assert.True(detalle.GetProperty("infoImportCertificadoOrigen").GetBoolean());
    }

    [Fact]
    public async Task NumeroPedimento_PostAutorizacion_Persiste_BlEtaYAgente_Y_InfoQuedaBloqueada()
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearOcImportacionAsync(client);

        // Lleva la OC a Autorizada por BD (el flujo de autorización no es lo que se prueba).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var oc = await db.OrdenesCompra.FirstAsync(o => o.Id == id);
            db.Entry(oc).Property(nameof(OrdenCompra.Estado)).CurrentValue = EstadoOrdenCompra.Autorizada;
            await db.SaveChangesAsync();
        }

        var pedimento = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/numero-pedimento",
            new
            {
                NumeroPedimento = "PED-2026-77",
                NumeroBl = "BL-POST-1",
                FechaEstimadaArribo = (DateOnly?)new DateOnly(2026, 12, 1),
                AgenteAduanalNaviera = "Agente Post",
            });
        Assert.Equal(HttpStatusCode.NoContent, pedimento.StatusCode);

        var info = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/informacion-importacion",
            new { IncotermId = Guid.NewGuid(), PaisOrigen = "US", NumeroContenedor = "C", CodigoRuta = "R", SemanaEmbarque = "S" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, info.StatusCode);

        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{id}"));
        Assert.Equal("PED-2026-77", detalle.GetProperty("infoImportNumeroPedimento").GetString());
        Assert.Equal("BL-POST-1", detalle.GetProperty("infoImportNumeroBl").GetString());
        Assert.Equal("2026-12-01", detalle.GetProperty("infoImportFechaEstimadaArribo").GetString());
        Assert.Equal("Agente Post", detalle.GetProperty("infoImportAgenteAduanalNaviera").GetString());
    }

    [Fact]
    public async Task DuplicarOc_Importacion_CopiaCamposComercioExterior_Y_FraccionDeLinea()
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearOcImportacionAsync(client);

        var info = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/informacion-importacion",
            new
            {
                IncotermId = Guid.NewGuid(),
                PaisOrigen = "US",
                NumeroContenedor = "CONT-DUP",
                CodigoRuta = "RUTA-DUP",
                SemanaEmbarque = "S43",
                TipoOperacion = (int?)0,
                NumeroBl = "BL-DUP",
                CertificadoOrigen = true,
            });
        Assert.Equal(HttpStatusCode.NoContent, info.StatusCode);

        var lineaResp = await client.PostAsJsonAsync($"{EndpointBase}/{id}/lineas", LineaManualBody(CentroCostoSeedId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);
        var lineaId = (await ReadJsonAsync(lineaResp)).GetProperty("lineaId").GetGuid();

        var fraccion = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/lineas/{lineaId}",
            new { FraccionArancelaria = "84719000" });
        Assert.Equal(HttpStatusCode.NoContent, fraccion.StatusCode);

        // Solo se duplican OCs Canceladas/Rechazadas: llevamos el origen a Cancelada por BD.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var oc = await db.OrdenesCompra.FirstAsync(o => o.Id == id);
            db.Entry(oc).Property(nameof(OrdenCompra.Estado)).CurrentValue = EstadoOrdenCompra.Cancelada;
            await db.SaveChangesAsync();
        }

        var dupResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{id}/duplicar",
            new
            {
                SucursalCodigo = "MID",
                FolioAnio = (short)2026,
                FechaDocumento = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            });
        Assert.Equal(HttpStatusCode.Created, dupResp.StatusCode);
        var nuevaId = (await ReadJsonAsync(dupResp)).GetProperty("ordenCompraNuevaId").GetGuid();

        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{nuevaId}"));
        Assert.True(detalle.GetProperty("esImportacion").GetBoolean());
        Assert.Equal("BL-DUP", detalle.GetProperty("infoImportNumeroBl").GetString());
        Assert.True(detalle.GetProperty("infoImportCertificadoOrigen").GetBoolean());
        var lineaDuplicada = detalle.GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal("84719000", lineaDuplicada.GetProperty("fraccionArancelaria").GetString());
    }

    [Fact]
    public async Task Enviar_Importacion_SinIncotermOPais_Lanza_CodigoEstable()
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearOcImportacionAsync(client);
        var lineaResp = await client.PostAsJsonAsync($"{EndpointBase}/{id}/lineas", LineaManualBody(CentroCostoSeedId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.Include(o => o.Adjuntos).SingleAsync(o => o.Id == id);
        foreach (var tipo in await db.TiposDocumentoOc.Where(t => t.Clave == "cotizacion" || t.Clave == "ficha_tecnica").ToListAsync())
            oc.AdjuntarDocumento(Guid.NewGuid(), tipo.Id, "DEMO-CE.pdf", "blob://DEMO/CE", "application/pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid());
        await db.SaveChangesAsync();

        var handler = new EnviarAAutorizacionOcHandler(
            db,
            sp.GetRequiredService<CompartidoDbContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPublisher>(),
            new Millet.Compras.Application.Oc.EvaluadorNivelOc(db, sp.GetRequiredService<IClock>()));
        var incoterm = oc.InfoImportIncotermId;
        var pais = oc.InfoImportPaisOrigen;
        try
        {
            // ck_oc_import_campos impide persistir el dato incompleto: se simula
            // solo en el agregado rastreado que lee el handler (sin SaveChanges).
            db.Entry(oc).Property(nameof(OrdenCompra.InfoImportIncotermId)).CurrentValue = null;
            var sinIncoterm = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(id), default));
            Assert.Equal("IMPORT_INCOTERM_REQUERIDO", sinIncoterm.Code);

            db.Entry(oc).Property(nameof(OrdenCompra.InfoImportIncotermId)).CurrentValue = incoterm;
            db.Entry(oc).Property(nameof(OrdenCompra.InfoImportPaisOrigen)).CurrentValue = null;
            var sinPais = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(id), default));
            Assert.Equal("IMPORT_PAIS_REQUERIDO", sinPais.Code);
            Assert.Equal(EstadoOrdenCompra.Borrador, oc.Estado);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.OrdenesCompra.Where(o => o.Id == id).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("8471ABCD")]
    [InlineData("123456789")]
    public async Task ActualizarLinea_FraccionInvalida_Retorna_400(string fraccion)
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearOcImportacionAsync(client);
        var lineaResp = await client.PostAsJsonAsync($"{EndpointBase}/{id}/lineas", LineaManualBody(CentroCostoSeedId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);
        var lineaId = (await ReadJsonAsync(lineaResp)).GetProperty("lineaId").GetGuid();

        var resp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/lineas/{lineaId}",
            new { FraccionArancelaria = fraccion });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ck_oc_import_campos exige incoterm, país y contenedor cuando es_importacion = true,
    // así que la OC nace no-importación, captura la info y después activa la bandera.
    private async Task<Guid> CrearOcImportacionAsync(HttpClient client)
    {
        var body = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration comercio exterior",
        };
        var resp = await client.PostAsJsonAsync(EndpointBase, body);
        resp.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();

        var info = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{id}/informacion-importacion",
            new { IncotermId = Guid.NewGuid(), PaisOrigen = "US", NumeroContenedor = "CONT-CE", CodigoRuta = "RUTA-CE", SemanaEmbarque = "S41" });
        Assert.Equal(HttpStatusCode.NoContent, info.StatusCode);

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.FirstAsync(o => o.Id == id);
        db.Entry(oc).Property(nameof(OrdenCompra.EsImportacion)).CurrentValue = true;
        await db.SaveChangesAsync();
        return id;
    }
}
