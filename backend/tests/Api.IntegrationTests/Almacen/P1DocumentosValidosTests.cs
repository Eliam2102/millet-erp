using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

public sealed class P1DocumentosValidosTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(false, null)] [InlineData(true, null)]
    [InlineData(false, EstadoOrdenCompra.Cancelada)] [InlineData(true, EstadoOrdenCompra.Cancelada)]
    [InlineData(false, EstadoOrdenCompra.Borrador)] [InlineData(true, EstadoOrdenCompra.Borrador)]
    public async Task Oc_invalida_devuelve_422_sin_movimientos(bool packing, EstadoOrdenCompra? estado)
    {
        using var scope = factory.Services.CreateScope();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresa.Bypass();
        var oc = NuevaOc(estado ?? EstadoOrdenCompra.Borrador);
        try
        {
            if (estado is not null) { compras.OrdenesCompra.Add(oc); await compras.SaveChangesAsync(); }
            using var admin = await LoginAsync(factory);
            RegistrarRecepcionLineaInput[] lineas = [new(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, Guid.NewGuid())];
            using var response = packing
                ? await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new RegistrarRecepcionConPackingListCommand(oc.Id, new(2026, 10, 9), "p1.pdf", null, lineas))
                : await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", new RegistrarRecepcionConFacturaCommand(oc.Id, new(2026, 10, 9), Guid.NewGuid(), null, null, lineas));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = await Json(response);
            Assert.Equal(estado is null ? "RECEPCION_OC_NO_ENCONTRADA" : "RECEPCION_OC_NO_AUTORIZADA", problem.GetProperty("code").GetString());
            Assert.False(await almacen.Movimientos.IgnoreQueryFilters().AnyAsync(m => m.OcId == oc.Id));
        }
        finally
        {
            compras.ChangeTracker.Clear();
            await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.ordenes_compra WHERE id = {oc.Id}");
        }
    }

    [Fact]
    public async Task Rq_inexistente_devuelve_422_sin_movimiento()
    {
        using var scope = factory.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        using var admin = await LoginAsync(factory);
        var rqId = Guid.NewGuid();
        using var response = await admin.PostAsJsonAsync("/api/v1/almacen/salidas/", new RegistrarSalidaConRequisicionCommand(rqId, new(2026, 10, 9), null, null,
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, null, null, Guid.NewGuid())]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SALIDA_RQ_NO_ENCONTRADA", (await Json(response)).GetProperty("code").GetString());
        Assert.False(await almacen.Movimientos.IgnoreQueryFilters().AnyAsync(m => m.RqId == rqId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rq_cancelada_o_exceso_autorizado_devuelve_422_sin_movimiento(bool cancelada)
    {
        using var scope = factory.Services.CreateScope();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var rq = new Requisicion(Guid.NewGuid(), EmpresaBootstrapId,
            Millet.Compras.Domain.Folio.Parse($"FIX2026-{Random.Shared.Next(100000, 999999)}"), 2026,
            Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow);
        var lineaId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        rq.AgregarLinea(lineaId, articuloId, 10, "PZA", Money.Mxn(25));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new CubrimientoLinea(lineaId, 4, 6)], DateTimeOffset.UtcNow);
        rq.RegistrarRecepcion(lineaId, 6, DateTimeOffset.UtcNow);
        rq.RegistrarEntrega(lineaId, 5, DateTimeOffset.UtcNow);
        var linea = rq.Lineas.Single();
        Assert.Equal(6m, linea.CantidadDeCompra);
        Assert.Equal(linea.CantidadDeCompra, linea.CantidadRecibida);
        if (cancelada) typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!.SetValue(rq, EstadoRequisicion.Cancelada);
        try
        {
            compras.Requisiciones.Add(rq);
            await compras.SaveChangesAsync();
            var lectura = await new ComprasRequisicionReadAdapter(compras).ObtenerAsync(rq.Id, default);
            Assert.NotNull(lectura);
            Assert.Equal(rq.Estado.ToString(), lectura.Estado);
            Assert.Equal(linea.CantidadPendienteEntregar, lectura.Lineas.Single().CantidadDisponibleEntregar);
            Assert.Equal(5m, lectura.Lineas.Single().CantidadDisponibleEntregar);
            using var admin = await LoginAsync(factory);
            using var response = await admin.PostAsJsonAsync("/api/v1/almacen/salidas/", new RegistrarSalidaConRequisicionCommand(
                rq.Id, new(2026, 10, 9), null, null,
                [new(articuloId, lineaId, 6, null, null, null, null, Guid.NewGuid()),
                 new(articuloId, lineaId, 5, null, null, null, null, Guid.NewGuid())]));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(cancelada ? "SALIDA_RQ_NO_AUTORIZADA" : "SALIDA_EXCEDE_AUTORIZADO", (await Json(response)).GetProperty("code").GetString());
            Assert.False(await almacen.Movimientos.IgnoreQueryFilters().AnyAsync(m => m.RqId == rq.Id));
        }
        finally
        {
            compras.ChangeTracker.Clear();
            await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE id = {rq.Id}");
        }
    }

    internal static OrdenCompra NuevaOc(EstadoOrdenCompra estado)
    {
        var oc = new OrdenCompra(Guid.NewGuid(), EmpresaBootstrapId,
            Millet.Compras.Domain.Oc.Folio.Parse($"OC-FIX2026-{Random.Shared.Next(100000, 999999)}"), 2026,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 9),
            sinRequisicionPrevia: true, motivoSinRequisicion: "Fixture aislado P1");
        // Estado controlado del fixture; no simula autorizaciones ni modifica catálogos compartidos.
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, estado);
        return oc;
    }
}
