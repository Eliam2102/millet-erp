using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;
using Millet.SharedKernel.Domain;
using Millet.Catalogos.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-02 (R1): registrar cotizaciones y ver el comparativo normalizado.
public partial class OrdenesCompraEndpointsTests
{
    private static readonly Guid TipoDocCotizacion = Guid.Parse("00000011-0006-0000-0000-000000000004");

    [Fact]
    public async Task Cotizaciones_RegistrarDos_ComparativoNormalizado()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();

        object Cuerpo(string moneda, string unidad, decimal factor, decimal precio) => new
        {
            ProveedorId = ProveedorActivoId, FolioProveedor = "COT-1", Moneda = moneda, FechaCotizacion = hoy,
            VigenteHasta = hoy.AddDays(10), CondicionesCredito = "30 días",
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = unidad, FactorAUnidadRq = factor, PrecioUnitario = precio, DiasEntrega = 5 } },
        };

        var mxn = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", Cuerpo("MXN", "HOJA", 1m, 500m));
        Assert.Equal(HttpStatusCode.Created, mxn.StatusCode);
        var usd = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", Cuerpo("USD", "M2", 0.4m, 10m));
        Assert.Equal(HttpStatusCode.Created, usd.StatusCode);
        var usdId = (await ReadJsonAsync(usd)).GetProperty("id").GetGuid();

        using var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent("%PDF-1.4\n%cotizacion\n"u8.ToArray());
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", "cotizacion.pdf");
        contenido.Add(new StringContent(TipoDocCotizacion.ToString()), "tipoDocumentoId");
        var subida = await client.PostAsync($"/api/v1/compras/cotizaciones/{usdId}/adjuntos", contenido);
        Assert.True(subida.StatusCode == HttpStatusCode.Created, await subida.Content.ReadAsStringAsync());

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        var tc = json.GetProperty("tiposCambio").EnumerateArray().Single().GetProperty("valorEnMxn").GetDecimal();
        var cotizaciones = json.GetProperty("cotizaciones").EnumerateArray().ToList();
        Assert.Equal(2, cotizaciones.Count);
        var cMxn = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "MXN");
        var cUsd = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "USD");
        Assert.Equal(500.00m, cMxn.GetProperty("lineas")[0].GetProperty("precioMxnPorUnidadRq").GetDecimal());
        var esperadoUsd = Math.Round(25m * tc, 2, MidpointRounding.ToEven);
        Assert.Equal(esperadoUsd, cUsd.GetProperty("lineas")[0].GetProperty("precioMxnPorUnidadRq").GetDecimal());
        Assert.Equal(esperadoUsd < 500m, cUsd.GetProperty("lineas")[0].GetProperty("esMenorPrecio").GetBoolean());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, cUsd.GetProperty("archivoAdjuntoId").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cMxn.GetProperty("archivoAdjuntoId").ValueKind);
        Assert.All(cotizaciones, c => Assert.True(c.GetProperty("vigente").GetBoolean()));
    }

    [Fact]
    public async Task Cotizaciones_SinPermiso_403_SinEfectos()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var client = _factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos"));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", new
        {
            ProveedorId = ProveedorActivoId, Moneda = "MXN", FechaCotizacion = hoy, VigenteHasta = hoy.AddDays(5),
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = "HOJA", FactorAUnidadRq = 1m, PrecioUnitario = 500m, DiasEntrega = 5 } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Equal(0, await db.Cotizaciones.CountAsync(c => c.RequisicionId == rqId));
    }

    /// <summary>RQ EnSurtido con una línea por comprar (4 HOJA) y sin OC comprometida.</summary>
    private async Task<(Guid RqId, Guid LineaId)> SembrarRqCotizable()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = new Requisicion(Guid.NewGuid(), EmpresaInicialId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026,
            Clasificacion.MateriaPrima, SucursalIdFija, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
        var l = rq.AgregarLinea(Guid.NewGuid(), Guid.Parse("00000007-0001-0000-0000-000000000001"), 4, "HOJA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new CubrimientoLinea(l.Id, 0, 4)], DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        return (rq.Id, l.Id);
    }

    private async Task<DateOnly> RegistrarTipoCambioHoyAsync(string codigoMoneda, decimal valor)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var hoy = FechasInicio.Hoy(scope.ServiceProvider.GetRequiredService<IClock>());
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var moneda = await db.Monedas.AsNoTracking().SingleAsync(m => m.Codigo == codigoMoneda);
        if (!await db.TiposCambio.AnyAsync(t => t.MonedaId == moneda.Id && t.Fecha == hoy))
        {
            db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), moneda.Id, hoy, valor));
            await db.SaveChangesAsync();
        }
        return hoy;
    }
}
