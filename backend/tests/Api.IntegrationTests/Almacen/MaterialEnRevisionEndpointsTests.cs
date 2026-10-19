using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Blob;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-04 bandeja «Material en revisión» por HTTP con Postgres real: la línea con daño aparece con pendiente 2,
/// un operativo sin sucursal asociada no la ve ni su evidencia (403), el admin sube evidencia G1.2 y la línea del
/// rack disponible no es una línea de revisión (404). Reusa el fixture de <see cref="CuarentenaRecepcionTests"/>.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class MaterialEnRevisionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TipoFotoDano = Guid.Parse("00000011-0006-0000-0000-000000000006");
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private static readonly DateOnly Fecha = new(2027, 4, 12);

    private readonly WebApplicationFactory<Program> _factory;

    public MaterialEnRevisionEndpointsTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IBlobStoragePort>(new BlobGrabador())));

    [Fact]
    public async Task Bandeja_alcance_por_sucursal_evidencia_y_404()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        var operativo = await ContabTestKit.ClienteConPermisosAsync(_factory,
            PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasCapturar);
        Guid lineaRevisionId = default;
        try
        {
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                await CuarentenaRecepcionTests.HandlerRecepcion(db, new CuarentenaRecepcionTests.CapturaEventos(), ctx).Handle(
                    new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null,
                        [new RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, 10m, null, null, ctx.RackId,
                            CantidadDanada: 2m, MotivoDano: "Hojas rotas")]),
                    CancellationToken.None);
                db.ChangeTracker.Clear();
                lineaRevisionId = await db.LineasMovimiento.Where(l => l.UbicacionId == ctx.RackRevisionId).Select(l => l.Id).SingleAsync();
                var lineaDisponibleId = await db.LineasMovimiento.Where(l => l.UbicacionId == ctx.RackId).Select(l => l.Id).SingleAsync();
                var rutaAdjuntos = $"/api/v1/almacen/revision/lineas/{lineaRevisionId}/adjuntos";

                // (1) El admin ve la línea con pendiente 2.
                var lista = await ContabTestKit.Json(await admin.GetAsync("/api/v1/almacen/revision"));
                var fila = lista.EnumerateArray().Single(x => x.GetProperty("lineaRevisionId").GetGuid() == lineaRevisionId);
                Assert.Equal(2m, fila.GetProperty("cantidadPendiente").GetDecimal());
                Assert.Equal("Hojas rotas", fila.GetProperty("motivoDano").GetString());

                // (2) El operativo sin la sucursal no la ve y su evidencia es 403.
                var listaOp = await operativo.GetStringAsync("/api/v1/almacen/revision");
                Assert.DoesNotContain(lineaRevisionId.ToString(), listaOp);
                Assert.Equal(HttpStatusCode.Forbidden, (await operativo.GetAsync(rutaAdjuntos)).StatusCode);

                // (3) El admin sube una evidencia y aparece en el GET.
                using var contenido = new MultipartFormDataContent();
                var archivo = new ByteArrayContent(Png);
                archivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                contenido.Add(archivo, "archivo", "dano.png");
                contenido.Add(new StringContent(TipoFotoDano.ToString()), "tipoDocumentoId");
                using var subida = await admin.PostAsync(rutaAdjuntos, contenido);
                Assert.True(subida.StatusCode == HttpStatusCode.Created, await subida.Content.ReadAsStringAsync());
                var adjuntoId = (await ContabTestKit.Json(subida)).GetProperty("id").GetGuid();
                var adjuntos = await ContabTestKit.Json(await admin.GetAsync(rutaAdjuntos));
                Assert.Contains(adjuntos.EnumerateArray(), a => a.GetProperty("id").GetGuid() == adjuntoId);

                // (4) La línea del rack disponible no es de revisión.
                using var noEncontrada = await admin.GetAsync($"/api/v1/almacen/revision/lineas/{lineaDisponibleId}/adjuntos");
                Assert.Equal(HttpStatusCode.NotFound, noEncontrada.StatusCode);
                Assert.Equal("REVISION_LINEA_NO_ENCONTRADA", await ContabTestKit.Code(noEncontrada));
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally
        {
            if (lineaRevisionId != default)
            {
                using var scope = _factory.Services.CreateScope();
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                await almacen.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM compartido.adjuntos WHERE tipo_entidad = 'linea_revision' AND entidad_id = {lineaRevisionId}");
            }
            await ContabTestKit.LimpiarClienteAsync(operativo);
        }
    }
}
