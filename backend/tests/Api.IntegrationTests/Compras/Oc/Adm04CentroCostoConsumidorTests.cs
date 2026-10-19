using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// ADM-04 · El consumidor (Compras) respeta la vigencia del centro de costo
/// cuando éste se desactiva DESPUÉS de capturar el documento (R-ADM-10):
/// no se acepta en documentos nuevos (duplicar, OC desde requisición) y el
/// documento original sigue legible. Por HTTP y contra el catálogo real; las
/// variantes con puerto simulado viven en <c>P2_CecoHeredado_*</c>.
/// </summary>
public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task Adm04_Duplicar_OC_con_centro_desactivado_despues_retorna_422_CECO_INVALIDO()
    {
        var client = await CreateSuperAdminClientAsync();
        var ccId = await CrearDim3ActivaAdm04Async();
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ValidBody() with
        {
            SinRequisicionPrevia = true, MotivoSinRequisicion = "ADM-04 duplicar con centro desactivado",
        }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/lineas", LineaManualBody(ccId))).StatusCode);

        // Sólo se duplican OC canceladas o rechazadas; el origen se cancela por BD.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            db.Entry(oc).Property(nameof(OrdenCompra.Estado)).CurrentValue = EstadoOrdenCompra.Cancelada;
            await db.SaveChangesAsync();
        }
        await DesactivarDim3Adm04Async(ccId);

        var dup = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/duplicar", new
        {
            SucursalCodigo = "MID", FolioAnio = (short)2026,
            FechaDocumento = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup.StatusCode);
        Assert.Equal("CECO_INVALIDO", (await ReadJsonAsync(dup)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Adm04_OC_desde_requisicion_con_centro_desactivado_despues_retorna_422_CECO_INVALIDO()
    {
        var client = await CreateSuperAdminClientAsync();
        var ccId = await CrearDim3ActivaAdm04Async();
        var rqId = await SembrarRqAutorizadaAdm04Async(ccId);
        await DesactivarDim3Adm04Async(ccId);

        // Crear la OC directamente desde la requisición.
        var desdeRq = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rqId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, desdeRq.StatusCode);
        Assert.Equal("CECO_INVALIDO", (await ReadJsonAsync(desdeRq)).GetProperty("code").GetString());

        // Agregar las líneas de la requisición a una OC ya creada.
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ValidBody())))
            .GetProperty("id").GetGuid();
        var agregar = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/lineas/desde-requisicion",
            new { RequisicionId = rqId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, agregar.StatusCode);
        Assert.Equal("CECO_INVALIDO", (await ReadJsonAsync(agregar)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Adm04_OC_existente_sigue_legible_con_clave_y_nombre_tras_desactivar_su_centro()
    {
        var client = await CreateSuperAdminClientAsync();
        var ccId = await CrearDim3ActivaAdm04Async();
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ValidBody() with
        {
            SinRequisicionPrevia = true, MotivoSinRequisicion = "ADM-04 historia legible",
        }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/lineas", LineaManualBody(ccId))).StatusCode);
        string clave;
        using (var scope = _factory.Services.CreateScope())
            clave = (await scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>().Dim3s
                .AsNoTracking().SingleAsync(d => d.Id == ccId)).Clave;

        await DesactivarDim3Adm04Async(ccId);

        var detalle = await client.GetAsync($"{EndpointBase}/{ocId}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var linea = (await ReadJsonAsync(detalle)).GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal(ccId, linea.GetProperty("centroCostoId").GetGuid());
        Assert.Equal(clave, linea.GetProperty("centroCostoClave").GetString());
        Assert.False(string.IsNullOrWhiteSpace(linea.GetProperty("centroCostoNombre").GetString()));
    }

    // --------- Helpers ADM-04 ---------

    private async Task<Guid> CrearDim3ActivaAdm04Async()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var sufijo = $"{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-{sufijo}"));
        var dim2Id = Guid.Parse("0000000c-0004-0000-0000-000000000045");
        var dim3 = await mediator.Send(new CrearDim3Command(dim2Id, $"A04{sufijo[..4]}", $"ADM-04 {sufijo}", grupo3.Id));
        return dim3.Id;
    }

    private async Task DesactivarDim3Adm04Async(Guid dim3Id)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        var version = (await db.Dim3s.AsNoTracking().SingleAsync(d => d.Id == dim3Id)).Version;
        await mediator.Send(new CambiarEstatusDim3Command(dim3Id, version, Activar: false));
    }

    /// <summary>Requisición autorizada, con todo por comprar y sin OC comprometida.</summary>
    private async Task<Guid> SembrarRqAutorizadaAdm04Async(Guid centroCostoId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = new Requisicion(Guid.NewGuid(), EmpresaInicialId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026,
            Clasificacion.MateriaPrima, SucursalIdFija, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Prioridad.Normal, DateTimeOffset.UtcNow, null);
        var l = rq.AgregarLinea(Guid.NewGuid(), Guid.Parse("00000007-0001-0000-0000-000000000001"), 10, "PZA",
            Money.Mxn(100), centroCostoId: centroCostoId);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new CubrimientoLinea(l.Id, 0, 10)], DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        return rq.Id;
    }
}
