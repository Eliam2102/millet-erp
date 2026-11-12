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
    public async Task Recepcion_real_nueve_de_diez_genera_incidencia_y_bloquea_cierre() => await VerificarRecepcion(9, false, false);
    [Theory]
    [InlineData(9, false, false)]
    [InlineData(10, false, false)]
    [InlineData(15, true, false)]
    [InlineData(15, true, true)]
    public async Task Partida_repartida_se_concilia_con_recepcion_real(decimal cantidad, bool variosExpedientes, bool predecesorCerrado) => await VerificarRecepcion(cantidad, variosExpedientes, true, predecesorCerrado);
    private static async Task VerificarRecepcion(decimal cantidad, bool variosExpedientes, bool repartir, bool predecesorCerrado = false)
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
            oc.AgregarLineaManual(linea, articulo, variosExpedientes ? 20 : 10, modelo.UnidadMedida, 100, modelo.DepartamentoSolicitanteId, fraccionArancelaria: "70071999");
            compras.Entry(oc).Property(x => x.Estado).CurrentValue = EstadoOrdenCompra.Autorizada;
            await compras.SaveChangesAsync();
            var c = Guid.NewGuid(); e.GuardarContenedor(c, new("P20-CONT", null, null, "P20-BL", null, null, null, null, ModalidadTransporte.Maritimo));
            e.AsignarPartidaImportacion(c, linea, 1, repartir ? 6 : 10, 10, modelo.UnidadMedida, escenario.Responsable);
            Guid? segundo = null;
            if (repartir)
            {
                segundo = Guid.NewGuid();
                e.GuardarContenedor(segundo.Value, new("P22-CONT-2", null, null, "P22-BL-2", null, null, null, null, ModalidadTransporte.Maritimo));
                e.AsignarPartidaImportacion(segundo.Value, linea, 1, 4, 10, modelo.UnidadMedida, escenario.Responsable);
            }
            e.AgregarParticipante(Guid.NewGuid(), "Agente ficticio P20", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
            e.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia P20", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
            e.RegistrarEmbarque(c, new(2026, 10, 15), new(2026, 10, 15));
            if (segundo is Guid otroContenedor) e.RegistrarEmbarque(otroContenedor, new(2026, 10, 15), new(2026, 10, 15));
            await ce.SaveChangesAsync();
            var almacen = sp.GetRequiredService<AlmacenDbContext>();
            ubicacion = await almacen.AsignacionesArticuloUbicacion.Where(x => x.ArticuloId == articulo).Select(x => x.UbicacionId).FirstAsync();
        }
        Guid? segundoExpediente = null;
        var contenedorExtra = Guid.NewGuid();
        if (variosExpedientes)
        {
            using var segundo = await cliente.PostAsJsonAsync("/api/v1/comercio-exterior/expedientes", escenario.Importacion());
            segundo.EnsureSuccessStatusCode();
            segundoExpediente = (await segundo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();
            using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = sp.GetRequiredService<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>();
            var primero = await ce.Expedientes.Include(x => x.Hitos).SingleAsync(x => x.Id == expediente);
            var e = await ce.Expedientes.Include(x => x.Hitos).SingleAsync(x => x.Id == segundoExpediente);
            // El segundo se creó después, pero arribó primero: el arribo debe prevalecer.
            ce.Entry(primero.Hitos.Single(x => x.Clave == "arribado")).Property(x => x.FechaReal).CurrentValue = new DateOnly(2026, 10, 15);
            ce.Entry(e.Hitos.Single(x => x.Clave == "arribado")).Property(x => x.FechaReal).CurrentValue = new DateOnly(2026, 10, 14);
            var c = Guid.NewGuid(); e.GuardarContenedor(c, new("P22-OTRO", null, null, "P22-OTRO-BL", null, null, null, null, ModalidadTransporte.Maritimo));
            e.AsignarPartidaImportacion(c, linea, 1, 10, 20, "PZA", escenario.Responsable);
            e.AgregarParticipante(Guid.NewGuid(), "Agente ficticio", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
            e.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
            e.RegistrarEmbarque(c, new(2026, 10, 13), new(2026, 10, 15));
            e.GuardarContenedor(contenedorExtra, new("P22-EXTRA", null, null, "P22-EXTRA-BL", null, null, null, null, ModalidadTransporte.Maritimo));
            if (predecesorCerrado)
            {
                e.ReconciliarRecepcion(e.PartidasImportacion.Single().Id, 10, new(2026, 10, 15), new(2026, 10, 19), escenario.Responsable, DateTimeOffset.UtcNow);
                ce.Entry(e).Property(x => x.Estado).CurrentValue = EstadoExpediente.Cerrado;
                var cancelado = new Expediente(Guid.NewGuid(), e.EmpresaId, "CE-CANCELADO", TipoExpediente.Importacion, e.SucursalId, "Muestra cancelada", ocId, e.IncotermId, "USD", escenario.Responsable, []);
                var guia = Guid.NewGuid();
                cancelado.GuardarContenedor(guia, new("P22-CANCELADO", null, null, "BL-CANCELADO", null, null, null, null, ModalidadTransporte.Maritimo));
                cancelado.AsignarPartidaImportacion(guia, linea, 1, 10, 20, "PZA", escenario.Responsable);
                cancelado.AgregarParticipante(Guid.NewGuid(), "Agente ficticio", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
                cancelado.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
                cancelado.RegistrarEmbarque(guia, new(2026, 10, 13), new(2026, 10, 15));
                cancelado.Cancelar("Ensayo de expediente cancelado");
                ce.Expedientes.Add(cancelado);
            }
            await ce.SaveChangesAsync();
        }
        if (!predecesorCerrado && segundoExpediente is Guid destino)
        {
            using var estado = await cliente.GetAsync($"/api/v1/comercio-exterior/expedientes/{destino}/contenedores"); estado.EnsureSuccessStatusCode();
            var contenedores = await estado.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0m, Assert.Single(contenedores.GetProperty("partidasOc").EnumerateArray()).GetProperty("remanente").GetDecimal());
            using var asignar = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/comercio-exterior/expedientes/{destino}/contenedores/{contenedorExtra}/partidas-importacion")
            { Content = JsonContent.Create(new { ocLineaId = linea, cantidad = 1 }) };
            asignar.Headers.TryAddWithoutValidation("If-Match", estado.Headers.ETag!.Tag);
            asignar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var exceso = await cliente.SendAsync(asignar);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, exceso.StatusCode);
            Assert.Equal("CE_CANTIDAD_EXCEDE_OC", (await exceso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using var recepcion = await cliente.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new RegistrarRecepcionConPackingListCommand(
            ocId, new(2026, 10, 15), "P20/packing.pdf", "Recepción ficticia P20", [new(articulo, linea, cantidad, null, null, ubicacion, FraccionArancelaria: "70071999")],
            Importacion: new("261630826000777", "P20-BL", "P20-CONT", "16", "A1")));
        Assert.True(recepcion.IsSuccessStatusCode, await recepcion.Content.ReadAsStringAsync());
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
        if (cantidad == 10)
        {
            Assert.Empty(actual.GetProperty("incidencias").EnumerateArray());
            return;
        }
        var incidencia = Assert.Single(actual.GetProperty("incidencias").EnumerateArray());
        Assert.True(incidencia.GetProperty("automatica").GetBoolean());
        Assert.Equal(variosExpedientes ? 5m : 1m, incidencia.GetProperty("diferenciaCantidad").GetDecimal());
        Assert.Contains("Asignado a", incidencia.GetProperty("descripcion").GetString(), StringComparison.Ordinal);
        if (segundoExpediente is Guid otro)
        {
            using var consultaOtro = await cliente.GetAsync($"/api/v1/comercio-exterior/expedientes/{otro}"); consultaOtro.EnsureSuccessStatusCode();
            Assert.Empty((await consultaOtro.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("incidencias").EnumerateArray());
            using var scope = escenario.Scope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = scope.ServiceProvider.GetRequiredService<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>();
            var partidas = await ce.Set<PartidaImportacion>().Where(p => p.OcLineaId == linea).ToListAsync();
            Assert.Equal(10m, partidas.Where(p => p.ExpedienteId == otro).Sum(p => p.CantidadRecibida));
            Assert.Equal(5m, partidas.Where(p => p.ExpedienteId == expediente).Sum(p => p.CantidadRecibida));
            if (predecesorCerrado)
            {
                var cancelado = await ce.Expedientes.Include(e => e.PartidasImportacion).SingleAsync(e => e.Folio == "CE-CANCELADO");
                Assert.Null(cancelado.PartidasImportacion.Single().CantidadRecibida);
                Assert.Equal(EstadoExpediente.Cerrado, (await ce.Expedientes.SingleAsync(e => e.Id == otro)).Estado);
            }
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/comercio-exterior/expedientes/{expediente}/cerrar-fisico") { Content = JsonContent.Create(new { }) };
        request.Headers.TryAddWithoutValidation("If-Match", consulta.Headers.ETag!.Tag);
        using var cierre = await cliente.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
        Assert.Equal("CE_CIERRE_PENDIENTE", (await cierre.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
