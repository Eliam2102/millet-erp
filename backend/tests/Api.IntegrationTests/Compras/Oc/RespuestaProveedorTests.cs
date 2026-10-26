using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-11: respuesta del proveedor sobre la OC (confirmación/rechazo, parcialidades, evidencia G1.2).
public partial class OrdenesCompraEndpointsTests
{
    private static readonly byte[] PdfMinimo = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

    // Fecha de la OC = hoy local - 3; la respuesta se fecha hoy local - 1 (válida contra "no futura" y "no anterior a la OC").
    private async Task<(Guid OcId, Guid LineaId, DateOnly FechaRespuesta, HttpClient Client)> SembrarOcRespuestaAsync(bool borrador = false)
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false, borrador: borrador);
        var hoy = FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var fechaOc = hoy.AddDays(-3);
            await db.OrdenesCompra.Where(o => o.Id == ocId).ExecuteUpdateAsync(s => s.SetProperty(o => o.FechaDocumento, fechaOc));
        }
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        var lineaId = detalle.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        return (ocId, lineaId, hoy.AddDays(-1), client);
    }

    private static MultipartFormDataContent CuerpoRespuesta(int tipo, DateOnly fechaRespuesta, object[] compromisos, string? motivo = null) => RespuestaMultipart(new
    {
        Tipo = tipo,
        FechaRespuesta = fechaRespuesta,
        Evidencia = "Correo del proveedor E2E",
        ContactoProveedor = "Ventas E2E",
        Motivo = motivo,
        Compromisos = compromisos,
    });

    private static MultipartFormDataContent RespuestaMultipart(object? datos = null, bool conArchivo = true)
    {
        var form = new MultipartFormDataContent("DEMO-ficticio-respuesta-boundary");
        if (datos is not null) form.Add(new StringContent(JsonSerializer.Serialize(datos)), "datos");
        if (conArchivo)
        {
            var archivo = new ByteArrayContent(PdfMinimo);
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(archivo, "archivo", "DEMO-ficticio.pdf");
        }
        return form;
    }

    [Fact]
    public async Task Cond_SoloEvidencia_RegistraArchivoSinInventarConfirmacionNiFecha()
    {
        var (ocId, _, _, client) = await SembrarOcRespuestaAsync();
        using var form = RespuestaMultipart();
        var response = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var respuesta = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, respuesta.GetProperty("tipo").ValueKind);
        Assert.Equal(JsonValueKind.Null, respuesta.GetProperty("fechaRespuesta").ValueKind);
        Assert.Equal(0, respuesta.GetProperty("compromisos").GetArrayLength());
        var archivos = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/respuestas-proveedor/{respuesta.GetProperty("id").GetGuid()}/adjuntos"));
        Assert.Equal(1, archivos.GetArrayLength());
        Assert.Equal(4, (await DetalleOcAsync(client, ocId)).GetProperty("estadoComercialProveedor").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cond_SinEvidenciaValida_NoRegistraRespuesta(bool archivoInvalido)
    {
        var (ocId, _, _, client) = await SembrarOcRespuestaAsync();
        using var form = RespuestaMultipart(conArchivo: false);
        if (archivoInvalido)
        {
            var archivo = new ByteArrayContent("archivo DEMO ficticio sin firma PDF"u8.ToArray());
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(archivo, "archivo", "DEMO.pdf");
        }
        var response = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        if (!archivoInvalido) Assert.Contains("Adjunta el archivo de evidencia", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, (await DetalleOcAsync(client, ocId)).GetProperty("respuestasProveedor").GetArrayLength());
    }

    private static object Compromiso(Guid lineaId, decimal cantidad, DateOnly fecha) => new
    {
        LineaOrdenCompraId = lineaId,
        Cantidad = cantidad,
        FechaPrometida = fecha,
    };

    private static async Task<JsonElement> DetalleOcAsync(HttpClient client, Guid ocId)
        => await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));

    [Fact]
    public async Task Com11_Confirmacion_MuestraConfirmadaFechaResponsableYEvidencia_SinRecepcion()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var prometida = fechaResp.AddDays(5);
        var response = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, prometida)]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creada = await ReadJsonAsync(response);
        Assert.Equal(1, creada.GetProperty("numero").GetInt32());
        Assert.True(creada.GetProperty("vigente").GetBoolean());

        var detalle = await DetalleOcAsync(client, ocId);
        var usuarioId = (await ReadJsonAsync(await client.GetAsync("/api/auth/me"))).GetProperty("userId").GetGuid();
        Assert.Equal(1, detalle.GetProperty("estadoComercialProveedor").GetInt32());
        Assert.Equal(prometida.ToString("yyyy-MM-dd"), detalle.GetProperty("fechaCompromisoProveedor").GetString());
        var r = detalle.GetProperty("respuestasProveedor")[0];
        Assert.Equal(usuarioId, r.GetProperty("registradaPorId").GetGuid());
        Assert.Equal("Correo del proveedor E2E", r.GetProperty("evidencia").GetString());
        Assert.Equal(0, detalle.GetProperty("subEstadoRecepcion").GetInt32());
        Assert.Equal(0m, detalle.GetProperty("lineas")[0].GetProperty("cantidadRecibida").GetDecimal());
        Assert.Equal(3, detalle.GetProperty("estado").GetInt32());
    }

    [Fact]
    public async Task Com11_Rechazo_ConMotivo_NoBorraLaOrden_YConservaHistoria()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]))).EnsureSuccessStatusCode();
        var rechazo = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(2, fechaResp, [], "No hay existencia E2E"));
        Assert.Equal(HttpStatusCode.Created, rechazo.StatusCode);

        var detalle = await DetalleOcAsync(client, ocId);
        Assert.Equal(3, detalle.GetProperty("estadoComercialProveedor").GetInt32());
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("fechaCompromisoProveedor").ValueKind);
        Assert.Equal(3, detalle.GetProperty("estado").GetInt32());
        var respuestas = detalle.GetProperty("respuestasProveedor");
        Assert.Equal(2, respuestas.GetArrayLength());
        Assert.Contains(respuestas.EnumerateArray(), x => x.GetProperty("numero").GetInt32() == 1 && !x.GetProperty("vigente").GetBoolean());

        var sinMotivo = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(2, fechaResp, []));
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
    }

    [Fact]
    public async Task Com11_SinClasificar_Y_LuegoConfirmacion_ActualizaEstadoComercial()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        using var evidencia = RespuestaMultipart();
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor", evidencia)).EnsureSuccessStatusCode();
        Assert.Equal(4, (await DetalleOcAsync(client, ocId)).GetProperty("estadoComercialProveedor").GetInt32());

        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]))).EnsureSuccessStatusCode();
        var detalle = await DetalleOcAsync(client, ocId);
        Assert.Equal(1, detalle.GetProperty("estadoComercialProveedor").GetInt32());
        Assert.Equal(2, detalle.GetProperty("respuestasProveedor").GetArrayLength());
    }

    [Fact]
    public async Task Com11_MismaIdempotencyKey_RegistraUnaSolaRespuesta()
    {
        var (ocId, _, _, client) = await SembrarOcRespuestaAsync();
        var key = Guid.NewGuid().ToString("D");
        async Task<HttpResponseMessage> Enviar()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{ocId}/respuestas-proveedor")
            { Content = RespuestaMultipart() };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.Created, (await Enviar()).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Enviar()).StatusCode);
        Assert.Equal(1, (await DetalleOcAsync(client, ocId)).GetProperty("respuestasProveedor").GetArrayLength());
    }

    [Fact]
    public async Task Com11_NuevaFechaPrometida_ConservaElCompromisoAnterior()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var f1 = fechaResp.AddDays(5);
        var f2 = fechaResp.AddDays(9);
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, f1)]))).EnsureSuccessStatusCode();
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, f2)]))).EnsureSuccessStatusCode();

        var detalle = await DetalleOcAsync(client, ocId);
        var respuestas = detalle.GetProperty("respuestasProveedor");
        Assert.Equal(2, respuestas.GetArrayLength());
        var primera = respuestas.EnumerateArray().Single(x => x.GetProperty("numero").GetInt32() == 1);
        Assert.Equal(f1.ToString("yyyy-MM-dd"), primera.GetProperty("compromisos")[0].GetProperty("fechaPrometida").GetString());
        Assert.Equal(f2.ToString("yyyy-MM-dd"), detalle.GetProperty("fechaCompromisoProveedor").GetString());
    }

    [Fact]
    public async Task Com11_CantidadDistinta_NoCambiaLaOrden_YMarcaDiferencia()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 8m, fechaResp.AddDays(5))]))).EnsureSuccessStatusCode();

        var detalle = await DetalleOcAsync(client, ocId);
        Assert.True(detalle.GetProperty("respuestasProveedor")[0].GetProperty("tieneDiferencias").GetBoolean());
        Assert.Equal(2, detalle.GetProperty("estadoComercialProveedor").GetInt32());
        Assert.Equal(10m, detalle.GetProperty("lineas")[0].GetProperty("cantidad").GetDecimal());
    }

    [Fact]
    public async Task Com11_SinPermiso_403_SinEfectos()
    {
        var (ocId, lineaId, fechaResp, _) = await SembrarOcRespuestaAsync();
        var sinPermiso = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);
        var usuarioId = await UsuarioIdDeAsync(sinPermiso);
        try
        {
            var response = await sinPermiso.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
                CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            Assert.False(await db.RespuestasProveedorOc.AnyAsync(r => r.OrdenCompraId == ocId));
        }
        finally
        {
            await LimpiarUsuarioAsync(usuarioId);
        }
    }

    [Fact]
    public async Task Com11_Parcialidades_SinDiferencia()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var f2 = fechaResp.AddDays(12);
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 6m, fechaResp.AddDays(5)), Compromiso(lineaId, 4m, f2)])))
            .EnsureSuccessStatusCode();

        var detalle = await DetalleOcAsync(client, ocId);
        var r = detalle.GetProperty("respuestasProveedor")[0];
        Assert.False(r.GetProperty("tieneDiferencias").GetBoolean());
        Assert.Equal(2, r.GetProperty("compromisos").GetArrayLength());
        Assert.Equal(f2.ToString("yyyy-MM-dd"), detalle.GetProperty("fechaCompromisoProveedor").GetString());
    }

    [Fact]
    public async Task Com11_OcEnBorrador_422()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync(borrador: true);
        var response = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]));
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_RESPUESTA_ESTADO_INVALIDO");
    }

    [Fact]
    public async Task Com11_FechaPrometidaAnteriorALaRespuesta_422()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var response = await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(-1))]));
        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest,
            $"Estado inesperado: {response.StatusCode}");
        Assert.Contains("FECHA_PROMETIDA", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Com11_Bandeja_MuestraEstadoComercial()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        (await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]))).EnsureSuccessStatusCode();

        var lista = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}?page=1&pageSize=200"));
        var items = lista.TryGetProperty("items", out var i) ? i : lista;
        var item = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ocId);
        Assert.Equal(1, item.GetProperty("estadoComercialProveedor").GetInt32());

        // Filtro server-side por estado comercial.
        var confirmadas = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}?estadoComercialProveedor=1&page=1&pageSize=200"));
        Assert.Contains(confirmadas.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == ocId);
        var rechazadas = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}?estadoComercialProveedor=3&page=1&pageSize=200"));
        Assert.DoesNotContain(rechazadas.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == ocId);
    }

    [Fact]
    public async Task Com11_Evidencia_SubeYSeLista_ConAlcanceDeLaOc()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var creada = await ReadJsonAsync(await client.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
            CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))])));
        var respId = creada.GetProperty("id").GetGuid();
        try
        {
            var tipos = await ReadJsonAsync(await client.GetAsync("/api/v1/adjuntos/tipos?entidad=oc_respuesta_proveedor"));
            var tipo = tipos.EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "evidencia_respuesta");
            var ruta = $"{EndpointBase}/respuestas-proveedor/{respId}/adjuntos";

            using var form = new MultipartFormDataContent("DEMO-ficticio-respuesta-boundary");
            var archivo = new ByteArrayContent(PdfMinimo);
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(archivo, "archivo", "evidencia.pdf");
            form.Add(new StringContent(tipo.GetProperty("id").GetGuid().ToString()), "tipoDocumentoId");
            using var subida = await client.PostAsync(ruta, form);
            Assert.True(subida.StatusCode == HttpStatusCode.Created, await subida.Content.ReadAsStringAsync());
            var adjuntoId = (await ReadJsonAsync(subida)).GetProperty("id").GetGuid();

            var lista = await ReadJsonAsync(await client.GetAsync(ruta));
            Assert.Contains(lista.EnumerateArray(), a => a.GetProperty("id").GetGuid() == adjuntoId);

            // La evidencia es historial inmutable: la baja se rechaza y el archivo sigue visible.
            using var baja = new HttpRequestMessage(HttpMethod.Delete, $"{ruta}/{adjuntoId}")
            {
                Content = JsonContent.Create(new { Motivo = "Se subió el archivo equivocado" }),
            };
            await AssertCodigoP2(await client.SendAsync(baja), HttpStatusCode.UnprocessableEntity, "RESPUESTA_EVIDENCIA_INMUTABLE");
            var trasBaja = await ReadJsonAsync(await client.GetAsync(ruta));
            Assert.Contains(trasBaja.EnumerateArray(), a => a.GetProperty("id").GetGuid() == adjuntoId);

            using var noExiste = await client.GetAsync($"{EndpointBase}/respuestas-proveedor/{Guid.NewGuid()}/adjuntos");
            Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compartido.adjuntos WHERE tipo_entidad = 'oc_respuesta_proveedor' AND entidad_id = {respId}");
        }
    }

    [Fact]
    public async Task Com11_Reintento_ConMismaIdempotencyKey_NoDuplica()
    {
        var (ocId, lineaId, fechaResp, client) = await SembrarOcRespuestaAsync();
        var key = Guid.NewGuid().ToString("D");
        for (var n = 0; n < 2; n++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{ocId}/respuestas-proveedor")
            {
                Content = CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]),
            };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var detalle = await DetalleOcAsync(client, ocId);
        Assert.Equal(1, detalle.GetProperty("respuestasProveedor").GetArrayLength());
    }

    [Fact]
    public async Task Com11_OtraSucursal_403()
    {
        var (ocId, lineaId, fechaResp, _) = await SembrarOcRespuestaAsync();
        var operativo = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor);
        var usuarioId = await UsuarioIdDeAsync(operativo);
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                // El helper lo asocia a la sucursal de la OC; aquí debe quedar sin esa asociación.
                await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().UsuarioSucursales
                    .Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            }
            var response = await operativo.PostAsync($"{EndpointBase}/{ocId}/respuestas-proveedor",
                CuerpoRespuesta(1, fechaResp, [Compromiso(lineaId, 10m, fechaResp.AddDays(5))]));
            await AssertCodigoP2(response, HttpStatusCode.Forbidden, "SUCURSAL_NO_ASOCIADA");
        }
        finally
        {
            await LimpiarUsuarioAsync(usuarioId);
        }
    }
}
