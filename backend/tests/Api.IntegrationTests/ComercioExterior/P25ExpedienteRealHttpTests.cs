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
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;

[Collection(ApiCompartida.Nombre)]
public sealed class P25ExpedienteRealHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";

    [Fact]
    public async Task Nueve_facturas_reemplazo_individual_pdf_compartido_y_baja_conservan_historia()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await escenario.ClienteAsync(true);
        using var creado = await c.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var id = (await Body(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var factura = await escenario.TipoAsync("IMP_FACTURA");
        var packing = await escenario.TipoAsync("IMP_PACKING");
        var etag = creado.Headers.ETag!.Tag;
        Guid primero = default, documento = default;
        for (var n = 0; n < 9; n++)
        {
            var archivo = await escenario.AdjuntoAsync(id, factura);
            using var r = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = factura, adjuntoId = archivo }, etag);
            r.EnsureSuccessStatusCode(); etag = r.Headers.ETag!.Tag;
            var documentos = (await Body(r)).GetProperty("documentos");
            if (n == 0) { primero = archivo; documento = documentos[0].GetProperty("id").GetGuid(); }
            Assert.Equal(n + 1, documentos.EnumerateArray().Count(d => d.GetProperty("estado").GetInt32() == 1));
        }
        using var compartir = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = packing, adjuntoId = primero }, etag);
        compartir.EnsureSuccessStatusCode(); etag = compartir.Headers.ETag!.Tag;
        var nuevo = await escenario.AdjuntoAsync(id, factura);
        using var reemplazo = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = factura, adjuntoId = nuevo, reemplazaA = documento, motivo = "Corrección ficticia P25" }, etag);
        reemplazo.EnsureSuccessStatusCode(); etag = reemplazo.Headers.ETag!.Tag;
        var docs = (await Body(reemplazo)).GetProperty("documentos").EnumerateArray().ToArray();
        Assert.Equal(9, docs.Count(d => d.GetProperty("tipoDocumentoId").GetGuid() == factura && d.GetProperty("estado").GetInt32() == 1));
        Assert.Equal(1, docs.Count(d => d.GetProperty("tipoDocumentoId").GetGuid() == packing && d.GetProperty("estado").GetInt32() == 1));
        using var ajenoCreado = await c.PostAsJsonAsync(Base, escenario.Importacion()); ajenoCreado.EnsureSuccessStatusCode();
        var otroId = (await Body(ajenoCreado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var ajeno = await escenario.AdjuntoAsync(otroId, factura);
        using var rechazo = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = factura, adjuntoId = ajeno }, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
        using var noAplica = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = packing, noAplica = true, motivo = "Muestra FedEx sin packing" }, etag);
        noAplica.EnsureSuccessStatusCode(); etag = noAplica.Headers.ETag!.Tag;
        Assert.DoesNotContain((await Body(noAplica)).GetProperty("faltantes").EnumerateArray(), d => d.GetProperty("id").GetGuid() == packing);
        using var retirar = await Send(c, $"{Base}/{id}/documentos/{(await Body(reemplazo)).GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("adjuntoId").ValueKind == JsonValueKind.String && d.GetProperty("adjuntoId").GetGuid() == nuevo).GetProperty("id").GetGuid()}/retirar", new { motivo = "Archivo no corresponde" }, etag);
        retirar.EnsureSuccessStatusCode();
        Assert.Equal(12, (await Body(retirar)).GetProperty("documentos").GetArrayLength());
    }

    [Fact]
    public async Task Xml_y_M_original_se_guardan_y_binario_xml_invalido_se_rechazan()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await escenario.ClienteAsync(true);
        using var creado = await c.PostAsJsonAsync(Base, escenario.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Body(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var tipo = await escenario.TipoAsync("IMP_VALIDACION_M");
        foreach (var (nombre, mime, texto, aceptado) in new[] {
            ("M1234567.012", "application/octet-stream", "500|MUESTRA|\r\n501|DATOS|\r\n800|FIN|\r\n", true),
            ("cove.xml", "application/xml", "<?xml version=\"1.0\"?><cove>MUESTRA</cove>", true),
            ("cove.xml", "application/xml", "<cove>", false),
            ("cove.xml", "application/xml", "<!DOCTYPE cove [<!ENTITY x SYSTEM 'file:///no-existe'>]><cove>&x;</cove>", false),
            ("M1234567.012", "application/octet-stream", "MZ\0binario", false) })
        {
            using var form = new MultipartFormDataContent();
            using var archivo = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(texto));
            archivo.Headers.ContentType = new(mime); form.Add(archivo, "archivo", nombre); form.Add(new StringContent(tipo.ToString()), "tipoDocumentoId");
            using var result = await c.PostAsync($"{Base}/{id}/adjuntos", form);
            Assert.True(result.IsSuccessStatusCode == aceptado, await result.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(ModalidadTransporte.Aereo)]
    [InlineData(ModalidadTransporte.Paqueteria)]
    public async Task Tres_OC_guia_sin_contenedor_recepcion_reporte_y_cierre_fisico(ModalidadTransporte modalidad)
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var c = await escenario.ClienteAsync(true, permisosAlmacen: true);
        var ordenes = new List<Guid>(); var lineas = new List<Guid>(); Guid articulo, ubicacion;
        using (var scope = escenario.Scope())
        {
            var sp = scope.ServiceProvider; using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var modelo = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == DemoSesionSeedHostedService.Id("DEMO-CE-OC-IMPORTACION"));
            var lm = modelo.Lineas.Single(); articulo = lm.ArticuloId;
            for (var n = 0; n < 3; n++)
            {
                var oc = n == 0 ? await db.OrdenesCompra.SingleAsync(o => o.Id == escenario.OcId) : new OrdenCompra(Guid.NewGuid(), modelo.EmpresaId, Folio.Parse("OC-PV2026-" + Random.Shared.Next(100000, 999999)), 2026, modelo.ProveedorId, modelo.SucursalDestinoId, modelo.CondicionesPagoId, modelo.UsoPrincipalId, escenario.Responsable, escenario.Responsable, new(2026, 10, 15), moneda: "USD", tipoCambio: 18, sinRequisicionPrevia: true, esImportacion: true, motivoSinRequisicion: "Muestra P25");
                db.Entry(oc).Property(o => o.Estado).CurrentValue = EstadoOrdenCompra.Borrador;
                if (n > 0) { oc.ActualizarInformacionImportacion(modelo.InformacionImportacion); db.OrdenesCompra.Add(oc); }
                var linea = Guid.NewGuid(); oc.AgregarLineaManual(linea, articulo, 2, lm.UnidadMedida, 100, lm.DepartamentoSolicitanteId, fraccionArancelaria: "70071999");
                db.Entry(oc).Property(o => o.Estado).CurrentValue = EstadoOrdenCompra.Autorizada;
                ordenes.Add(oc.Id); lineas.Add(linea);
            }
            await db.SaveChangesAsync();
            ubicacion = await sp.GetRequiredService<AlmacenDbContext>().AsignacionesArticuloUbicacion.Where(x => x.ArticuloId == articulo).Select(x => x.UbicacionId).FirstAsync();
        }
        using var creado = await c.PostAsJsonAsync(Base, new { tipo = 1, ocId = ordenes[0], ocIds = ordenes, referencia = "Tres OC de muestra P25", responsableUsuarioId = escenario.Responsable, clientes = Array.Empty<string>() });
        Assert.True(creado.IsSuccessStatusCode, await creado.Content.ReadAsStringAsync());
        var id = (await Body(creado)).GetProperty("expediente").GetProperty("id").GetGuid(); var etag = creado.Headers.ETag!.Tag;
        foreach (var tipo in new[] { TipoParticipante.AgenteAduanal, modalidad == ModalidadTransporte.Aereo ? TipoParticipante.Aerolinea : TipoParticipante.TransportistaTerrestre })
        {
            using var result = await Send(c, $"{Base}/{id}/participantes", new { nombre = "Participante ficticio P25", tipo, papel = PapelParticipante.Captura }, etag);
            result.EnsureSuccessStatusCode(); etag = result.Headers.ETag!.Tag;
        }
        using var unidad = await Send(c, $"{Base}/{id}/contenedores", new { blGuia = "GUIA-P25-MUESTRA", modalidad }, etag);
        unidad.EnsureSuccessStatusCode(); etag = unidad.Headers.ETag!.Tag;
        var cid = (await Body(unidad)).GetProperty("contenedores")[0].GetProperty("id").GetGuid();
        foreach (var linea in lineas)
        {
            using var result = await Send(c, $"{Base}/{id}/contenedores/{cid}/partidas-importacion", new { ocLineaId = linea, cantidad = 2 }, etag);
            result.EnsureSuccessStatusCode(); etag = result.Headers.ETag!.Tag;
        }
        using var embarque = await Send(c, $"{Base}/{id}/contenedores/{cid}/embarcar", new { fecha = "2026-10-15" }, etag);
        embarque.EnsureSuccessStatusCode();
        for (var n = 0; n < ordenes.Count; n++)
        {
            using var recibido = await c.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new RegistrarRecepcionConPackingListCommand(ordenes[n], new(2026, 10, 15), "P25/packing.pdf", "Recepción ficticia P25", [new(articulo, lineas[n], 2, null, null, ubicacion, FraccionArancelaria: "70071999")], Importacion: new("261630826000777", "GUIA-P25-MUESTRA", null, "16", "AF")));
            Assert.True(recibido.IsSuccessStatusCode, await recibido.Content.ReadAsStringAsync());
        }
        using (var scope = escenario.Scope())
        {
            var sp = scope.ServiceProvider; using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var almacen = sp.GetRequiredService<AlmacenDbContext>();
            var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var evento in await almacen.OutboxEntries.Where(x => x.EventType == "almacen.oc_recepcion.registrada.v1").ToListAsync())
            {
                var datos = JsonSerializer.Deserialize<RecepcionCePayload>(evento.Payload, json)!;
                if (!ordenes.Contains(datos.OrdenCompraId)) continue;
                var command = new RecibirRecepcionCeCommand(evento.Id, datos);
                await sp.GetRequiredService<IMediator>().Send(command); await sp.GetRequiredService<IMediator>().Send(command);
            }
        }
        using var actual = await c.GetAsync($"{Base}/{id}"); actual.EnsureSuccessStatusCode();
        var detalle = await Body(actual);
        Assert.Equal(3, detalle.GetProperty("recepcionesImportacion").GetArrayLength());
        Assert.Empty(detalle.GetProperty("incidencias").EnumerateArray());
        using var reporte = await c.GetAsync("/api/v1/comercio-exterior/relacion-iva?anio=2026&mes=10"); reporte.EnsureSuccessStatusCode();
        var filas = (await Body(reporte)).GetProperty("filas").EnumerateArray().Where(f => f.GetProperty("expediente").GetString() == detalle.GetProperty("expediente").GetProperty("folio").GetString()).ToArray();
        Assert.Equal(3, filas.Length); Assert.All(filas, f => { Assert.Equal("AF", f.GetProperty("clave").GetString()); Assert.Equal(JsonValueKind.Null, f.GetProperty("contenedor").ValueKind); });
        using var cierre = await Send(c, $"{Base}/{id}/cerrar-fisico", new { }, actual.Headers.ETag!.Tag);
        Assert.True(cierre.IsSuccessStatusCode, await cierre.Content.ReadAsStringAsync());
        etag = cierre.Headers.ETag!.Tag;
        Guid soporte = default;
        foreach (var requisito in detalle.GetProperty("requisitos").EnumerateArray())
        {
            var tipo = requisito.GetProperty("id").GetGuid();
            var condicional = requisito.GetProperty("condicional").GetBoolean();
            var archivo = condicional ? (Guid?)null : await escenario.AdjuntoAsync(id, tipo);
            if (archivo is Guid a) soporte = a;
            using var documento = await Send(c, $"{Base}/{id}/documentos", new { tipoDocumentoId = tipo, adjuntoId = archivo, noAplica = condicional, motivo = condicional ? "No aplica a la muestra por guía" : null, numeroPedimento = requisito.GetProperty("codigo").GetString() == "IMP_PEDIMENTO" ? "261630826000777" : null }, etag);
            documento.EnsureSuccessStatusCode(); etag = documento.Headers.ETag!.Tag;
        }
        foreach (var (clave, _) in HitoExpediente.Catalogo.Where(h => h.Clave is not ("solicitado" or "cerrado")))
        {
            using var hito = await Send(c, $"{Base}/{id}/hitos/{clave}/real", new { fechaReal = "2026-10-15", responsableUsuarioId = escenario.Responsable, evidenciaAdjuntoId = soporte, aduanaEntrada = "16" }, etag);
            Assert.True(hito.IsSuccessStatusCode, await hito.Content.ReadAsStringAsync()); etag = hito.Headers.ETag!.Tag;
        }
        using var revision = await Send(c, $"{Base}/{id}/revisar-documentos", new { }, etag);
        revision.EnsureSuccessStatusCode(); etag = revision.Headers.ETag!.Tag;
        foreach (var dimension in new[] { "documental", "financiero" })
        {
            using var dimensionCerrada = await Send(c, $"{Base}/{id}/cerrar-{dimension}", new { }, etag);
            Assert.True(dimensionCerrada.IsSuccessStatusCode, await dimensionCerrada.Content.ReadAsStringAsync()); etag = dimensionCerrada.Headers.ETag!.Tag;
        }
        using var aprobado = await Send(c, $"{Base}/{id}/cerrar", new { }, etag);
        Assert.True(aprobado.IsSuccessStatusCode, await aprobado.Content.ReadAsStringAsync());
        Assert.Equal(3, (await Body(aprobado)).GetProperty("expediente").GetProperty("estado").GetInt32());

    }

    private static async Task<JsonElement> Body(HttpResponseMessage r) { using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return d.RootElement.Clone(); }
    private static async Task<HttpResponseMessage> Send(HttpClient c, string ruta, object body, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await c.SendAsync(request);
    }
}
