using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MediatR;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Workers;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-07: incidencias del proveedor por línea de OC (C3.5-c a C3.5-f).
public partial class OrdenesCompraEndpointsTests
{
    private async Task<(Guid OcId, Guid LineaId, HttpClient Client)> SembrarOcIncidenciaAsync()
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false);
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        return (ocId, detalle.GetProperty("lineas")[0].GetProperty("id").GetGuid(), client);
    }

    private static object CuerpoIncidencia(Guid lineaId, decimal cantidad, DateOnly fecha, Guid responsableId) => new
    {
        LineaOrdenCompraId = lineaId,
        Tipo = 1,
        Cantidad = cantidad,
        FechaCompromiso = fecha,
        ResponsableId = responsableId,
        Descripcion = "Proveedor E2E entregó incompleto",
    };

    private async Task RecibirAsync(Guid ocId, Guid lineaId, decimal acumulado)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var empresaId = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra
            .Where(o => o.Id == ocId).Select(o => o.EmpresaId).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<IPublisher>()
            .Publish(new RecepcionMaterialEnOcEvent(ocId, lineaId, empresaId, acumulado, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Com07_Recibir_6_deja_incidencia_abierta_y_el_remanente_la_cierra_en_el_historial()
    {
        var (ocId, lineaId, client) = await SembrarOcIncidenciaAsync();
        var usuarioId = await UsuarioIdDeAsync(client);
        var hoy = FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        await RecibirAsync(ocId, lineaId, 6m);

        var response = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/incidencias-proveedor",
            CuerpoIncidencia(lineaId, 4m, hoy.AddDays(3), usuarioId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var incidenciaId = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();

        var reprog = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/incidencias-proveedor/{incidenciaId}/reprogramar",
            new { FechaCompromiso = hoy.AddDays(7), Motivo = "Proveedor sin material" });
        Assert.Equal(HttpStatusCode.OK, reprog.StatusCode);

        var detalle = await DetalleOcAsync(client, ocId);
        var inc = detalle.GetProperty("incidencias")[0];
        Assert.True(inc.GetProperty("abierta").GetBoolean());
        Assert.Equal(hoy.AddDays(7).ToString("yyyy-MM-dd"), inc.GetProperty("fechaCompromisoVigente").GetString());
        Assert.Equal(2, inc.GetProperty("compromisos").GetArrayLength());
        Assert.Equal(6m, detalle.GetProperty("lineas")[0].GetProperty("cantidadRecibida").GetDecimal());

        await RecibirAsync(ocId, lineaId, 10m);

        inc = (await DetalleOcAsync(client, ocId)).GetProperty("incidencias")[0];
        Assert.False(inc.GetProperty("abierta").GetBoolean());
        Assert.True(inc.GetProperty("cerradaPorRecepcion").GetBoolean());

        var proveedorId = detalle.GetProperty("proveedorId").GetGuid();
        var historial = await ReadJsonAsync(await client.GetAsync(
            $"{EndpointBase}/incidencias-proveedor?proveedorId={proveedorId}"));
        var item = historial.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == incidenciaId);
        Assert.False(item.GetProperty("abierta").GetBoolean());
        Assert.True(item.GetProperty("cumplidaATiempo").GetBoolean());
        Assert.Equal(1, item.GetProperty("reprogramaciones").GetInt32());
    }

    [Fact]
    public async Task Com07_Excede_pendiente_422_y_no_escribe()
    {
        var (ocId, lineaId, client) = await SembrarOcIncidenciaAsync();
        var usuarioId = await UsuarioIdDeAsync(client);
        var response = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/incidencias-proveedor",
            CuerpoIncidencia(lineaId, 11m, FechaContable.HoyLocal(DateTimeOffset.UtcNow).AddDays(2), usuarioId));
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_INCIDENCIA_EXCEDE_PENDIENTE");
        Assert.Equal(0, (await DetalleOcAsync(client, ocId)).GetProperty("incidencias").GetArrayLength());
    }

    [Fact]
    public async Task Com07_Sin_permiso_403_sin_efectos()
    {
        var (ocId, lineaId, client) = await SembrarOcIncidenciaAsync();
        var lector = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);
        var lectorId = await UsuarioIdDeAsync(lector);
        try
        {
            var response = await lector.PostAsJsonAsync($"{EndpointBase}/{ocId}/incidencias-proveedor",
                CuerpoIncidencia(lineaId, 2m, FechaContable.HoyLocal(DateTimeOffset.UtcNow).AddDays(2), lectorId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, (await DetalleOcAsync(client, ocId)).GetProperty("incidencias").GetArrayLength());
        }
        finally
        {
            await LimpiarUsuarioAsync(lectorId);
        }
    }

    [Fact]
    public async Task Com07_Revision_diaria_avisa_una_vez_por_compromiso_vencido()
    {
        var (ocId, lineaId, client) = await SembrarOcIncidenciaAsync();
        var usuarioId = await UsuarioIdDeAsync(client);
        var hoy = FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        var creada = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/incidencias-proveedor",
            CuerpoIncidencia(lineaId, 2m, hoy, usuarioId));
        var incidenciaId = (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ayer = hoy.AddDays(-1);
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Set<Millet.Compras.Domain.Oc.CompromisoIncidenciaOc>()
                .Where(c => c.IncidenciaProveedorOcId == incidenciaId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FechaCompromiso, ayer));
        }

        var worker = new IncidenciaVencidaWorker(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<IOptionsMonitor<IncidenciaVencidaOptions>>(),
            NullLogger<IncidenciaVencidaWorker>.Instance);
        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().AvisosIncidenciaProveedorOc
                .CountAsync(a => a.IncidenciaProveedorOcId == incidenciaId));
        }
        var inc = (await DetalleOcAsync(client, ocId)).GetProperty("incidencias")[0];
        Assert.True(inc.GetProperty("vencida").GetBoolean());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, inc.GetProperty("avisoVencidaEnviadoEn").ValueKind);
    }
}
