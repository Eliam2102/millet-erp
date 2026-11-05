using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;
[Collection(ApiCompartida.Nombre)]
public sealed class P20RecepcionHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [Fact]
    public async Task Recepcion_real_nueve_de_diez_genera_incidencia_y_bloquea_cierre()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var cliente = await escenario.ClienteAsync(true, permisosAlmacen: true);
        using var creado = await cliente.PostAsJsonAsync("/api/v1/comercio-exterior/expedientes", escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var detalle = await creado.Content.ReadFromJsonAsync<JsonElement>();
        var expediente = detalle.GetProperty("expediente").GetProperty("id").GetGuid();
        Guid ocId, linea, articulo, ubicacion;
        using (var scope = escenario.Scope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = sp.GetRequiredService<ComprasDbContext>();
            var ce = sp.GetRequiredService<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>();
            var e = await ce.Expedientes.SingleAsync(x => x.Id == expediente); ocId = e.OcId!.Value;
            var origen = await compras.OrdenesCompra.Include(x => x.Lineas).SingleAsync(x => x.Id == DemoSesionSeedHostedService.Id("DEMO-CE-OC-IMPORTACION"));
            var modelo = origen.Lineas.Single(); articulo = modelo.ArticuloId; linea = Guid.NewGuid();
            var oc = await compras.OrdenesCompra.SingleAsync(x => x.Id == ocId);
            compras.Entry(oc).Property(x => x.Estado).CurrentValue = EstadoOrdenCompra.Borrador;
            oc.AgregarLineaManual(linea, articulo, 10, modelo.UnidadMedida, 100, modelo.DepartamentoSolicitanteId, fraccionArancelaria: "70071999");
            compras.Entry(oc).Property(x => x.Estado).CurrentValue = EstadoOrdenCompra.Autorizada;
            await compras.SaveChangesAsync();
            var c = Guid.NewGuid(); e.GuardarContenedor(c, new("P20-CONT", null, null, "P20-BL", null, null, null, null, ModalidadTransporte.Maritimo));
            e.AsignarPartidaImportacion(c, linea, 1, 10, 10, modelo.UnidadMedida, escenario.Responsable);
            e.AgregarParticipante(Guid.NewGuid(), "Agente ficticio P20", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
            e.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia P20", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
            e.RegistrarEmbarque(c, new(2026, 10, 15), new(2026, 10, 15));
            await ce.SaveChangesAsync();
            var almacen = sp.GetRequiredService<AlmacenDbContext>();
            ubicacion = await almacen.AsignacionesArticuloUbicacion.Where(x => x.ArticuloId == articulo).Select(x => x.UbicacionId).FirstAsync();
        }
        using var recepcion = await cliente.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new RegistrarRecepcionConPackingListCommand(
            ocId, new(2026, 10, 15), "P20/packing.pdf", "Recepción ficticia P20", [new(articulo, linea, 9, null, null, ubicacion, FraccionArancelaria: "70071999")],
            Importacion: new("264730826000777", "P20-BL", "P20-CONT", "47", "A1")));
        recepcion.EnsureSuccessStatusCode();
        // Entrega del evento real del outbox, sin workers ni Service Bus en el gate.
        using (var scope = escenario.Scope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var almacen = sp.GetRequiredService<AlmacenDbContext>();
            var evento = (await almacen.OutboxEntries.Where(x => x.EventType == "almacen.oc_recepcion.registrada.v1").ToListAsync())
                .Single(x => JsonSerializer.Deserialize<RecepcionCePayload>(x.Payload, JsonOptions)!.OrdenCompraId == ocId);
            var command = new RecibirRecepcionCeCommand(evento.Id, JsonSerializer.Deserialize<RecepcionCePayload>(evento.Payload, JsonOptions)!);
            await sp.GetRequiredService<IMediator>().Send(command);
            await sp.GetRequiredService<IMediator>().Send(command);
        }
        using var consulta = await cliente.GetAsync($"/api/v1/comercio-exterior/expedientes/{expediente}"); consulta.EnsureSuccessStatusCode();
        var actual = await consulta.Content.ReadFromJsonAsync<JsonElement>();
        var incidencia = Assert.Single(actual.GetProperty("incidencias").EnumerateArray());
        Assert.True(incidencia.GetProperty("automatica").GetBoolean());
        Assert.Equal(1m, incidencia.GetProperty("diferenciaCantidad").GetDecimal());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/comercio-exterior/expedientes/{expediente}/cerrar-fisico") { Content = JsonContent.Create(new { }) };
        request.Headers.TryAddWithoutValidation("If-Match", consulta.Headers.ETag!.Tag);
        using var cierre = await cliente.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
        Assert.Equal("CE_CIERRE_PENDIENTE", (await cierre.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
