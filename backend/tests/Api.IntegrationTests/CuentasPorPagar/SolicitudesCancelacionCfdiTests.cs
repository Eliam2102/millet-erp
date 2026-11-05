using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>P8.7: registrar y decidir solicitudes de cancelación de CFDI por API. Datos FIX, se limpian al final.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class SolicitudesCancelacionCfdiTests(WebApplicationFactory<Program> factory)
{
    private const string Cfdis = "/api/v1/cuentas-por-pagar/cfdis";

    [Fact]
    public async Task Registrar_rechazar_y_aceptar_dejan_registro_y_evidencia_sin_tocar_el_estatus_SAT()
    {
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        await using var app = enEmpresa.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEvidenciaBlobStorage>();
            s.AddSingleton<IEvidenciaBlobStorage>(new BlobFalso());
        }));
        using var admin = await LoginAsync(app);
        var ahora = DateTimeOffset.UtcNow;
        CfdiRecibido Nuevo(string folio) => CfdiRecibido.Ingresar(EmpresaBootstrapId, UuidCfdi.Parse(Guid.NewGuid().ToString()),
            RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"), TipoCfdi.Ingreso, folio, null, ahora,
            116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora, "blob-fix", null, "hash-fix",
            estatusSat: EstatusSatCfdi.Vigente);
        var rechazado = Nuevo("FIX-P87-R");
        var aceptado = Nuevo("FIX-P87-A");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            db.AddRange(rechazado, aceptado);
            await db.SaveChangesAsync();
        }

        try
        {
            // CA8.16: registro con responsable, área y plazo SAT visible.
            var creada = await admin.PostAsJsonAsync($"{Cfdis}/{rechazado.Id}/solicitudes-cancelacion", new { area = 1, notas = "FIX pidió el proveedor" });
            Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
            var sol = await Json(creada);
            Assert.Equal((int)EstadoSolicitudCancelacion.Pendiente, sol.GetProperty("estado").GetInt32());
            Assert.NotEqual(Guid.Empty, sol.GetProperty("responsableId").GetGuid());
            Assert.True(sol.GetProperty("plazoSatVence").GetDateTimeOffset() > sol.GetProperty("fechaRecepcion").GetDateTimeOffset());

            // Segunda pendiente sobre el mismo CFDI: 422.
            var duplicada = await admin.PostAsJsonAsync($"{Cfdis}/{rechazado.Id}/solicitudes-cancelacion", new { area = 2 });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicada.StatusCode);
            Assert.Equal("SOLICITUD_CANCELACION_PENDIENTE", await Code(duplicada));

            // P8.7-a: rechazo con evidencia; el CFDI sigue vigente.
            var rechazo = await admin.PostAsync($"{Cfdis}/{rechazado.Id}/solicitudes-cancelacion/{sol.GetProperty("id").GetGuid()}/decidir",
                Form(false, "FIX ya está pagada", true));
            Assert.Equal(HttpStatusCode.OK, rechazo.StatusCode);
            var rj = await Json(rechazo);
            Assert.Equal((int)EstadoSolicitudCancelacion.Rechazada, rj.GetProperty("estado").GetInt32());
            Assert.Equal("acuse.pdf", rj.GetProperty("evidenciaNombre").GetString());
            var detalle = await Json(await admin.GetAsync($"{Cfdis}/{rechazado.Id}"));
            Assert.Equal((int)EstatusSatCfdi.Vigente, detalle.GetProperty("estatusSat").GetInt32());

            // Decidir sin comentario: 400/422 y no cambia nada.
            var otra = await Json(await admin.PostAsJsonAsync($"{Cfdis}/{aceptado.Id}/solicitudes-cancelacion", new { area = 3 }));
            var sinComentario = await admin.PostAsync($"{Cfdis}/{aceptado.Id}/solicitudes-cancelacion/{otra.GetProperty("id").GetGuid()}/decidir",
                Form(true, " ", false));
            Assert.True(sinComentario.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);

            // Aceptar es decisión interna: el estatus SAT no cambia (solo lo cambia la sincronización del SAT).
            var acepta = await admin.PostAsync($"{Cfdis}/{aceptado.Id}/solicitudes-cancelacion/{otra.GetProperty("id").GetGuid()}/decidir",
                Form(true, "FIX acuse del SAT", false));
            Assert.Equal(HttpStatusCode.OK, acepta.StatusCode);
            Assert.Equal((int)EstadoSolicitudCancelacion.Aceptada, (await Json(acepta)).GetProperty("estado").GetInt32());
            Assert.Equal((int)EstatusSatCfdi.Vigente, (await Json(await admin.GetAsync($"{Cfdis}/{aceptado.Id}"))).GetProperty("estatusSat").GetInt32());

            var lista = await Json(await admin.GetAsync($"{Cfdis}/{aceptado.Id}/solicitudes-cancelacion"));
            Assert.Single(lista.EnumerateArray());
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var ids = new[] { rechazado.Id, aceptado.Id };
            await db.SolicitudesCancelacionCfdi.Where(s => ids.Contains(s.CfdiRecibidoId)).ExecuteDeleteAsync();
            await db.CfdisRecibidos.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
        }
    }

    private static MultipartFormDataContent Form(bool aceptar, string comentario, bool conArchivo)
    {
        var form = new MultipartFormDataContent { { new StringContent(aceptar ? "true" : "false"), "aceptar" }, { new StringContent(comentario), "comentario" } };
        if (conArchivo) form.Add(new ByteArrayContent([1, 2, 3]), "archivo", "acuse.pdf");
        return form;
    }

    private sealed class BlobFalso : IEvidenciaBlobStorage
    {
        public Task<string> GuardarAsync(Guid documentoId, Guid evidenciaId, DateTimeOffset ahora, string nombreArchivo,
            string contentType, Stream contenido, CancellationToken cancellationToken) => Task.FromResult($"fix/{documentoId}/{nombreArchivo}");
    }
}
