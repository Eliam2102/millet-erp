using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

/// <summary>M-204 · reclasificar es una solicitud; la firma otra persona con la facultad y el flujo muestra el rastro.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ReclasificacionSegundaFirmaTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;
    private static readonly Guid Financiamiento = Guid.Parse("0000000b-1001-0000-0000-000000000008");

    private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string url, object? body = null, int? version = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (version is not null) req.Headers.Add("X-Expected-Version", version.ToString());
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task Solicitud_pendiente_no_cambia_el_flujo_quien_la_pidio_no_firma_sin_facultad_403_y_otra_persona_la_aplica_con_rastro()
    {
        await using var amb = new AdjuntosProveedorAmbiente(factory);
        var comunes = new[] { PermisosCanonicos.TesoreriaMovimientosRegistrar, PermisosCanonicos.TesoreriaMovimientosVer,
            PermisosCanonicos.TesoreriaReportesVer, PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales,
            PermisosCanonicos.TesoreriaDocumentosLeerTodasSucursales };
        var firma = comunes.Append(PermisosCanonicos.TesoreriaMovimientosAutorizarReclasificacion).ToArray();
        var (a, aId) = await amb.ClienteConPermisosAsync(firma);
        var (b, bId) = await amb.ClienteConPermisosAsync(firma);
        var (sinFacultad, _) = await amb.ClienteConPermisosAsync(comunes);

        Guid cuentaId, movId; int version; Guid? conceptoOriginal;
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var n = Guid.NewGuid().ToString("N");
            var cuenta = new CuentaBancaria(Empresa, "BBVA Reclasifica", n[..12], null, "MXN");
            db.CuentasBancarias.Add(cuenta);
            var mov = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, Guid.NewGuid(), 12_500m, hoy, $"RECL-{n[..8]}",
                null, "Operación sin documento", Guid.NewGuid(), DateTimeOffset.UtcNow);
            db.MovimientosBancarios.Add(mov);
            await db.SaveChangesAsync();
            cuentaId = cuenta.Id; movId = mov.Id; version = mov.Version; conceptoOriginal = mov.ConceptoId;
        }
        var baseUrl = $"/api/v1/tesoreria/movimientos/{movId}";

        // 1) A solicita: queda pendiente y el concepto vigente no cambia.
        var solicitud = await PostAsync(a, $"{baseUrl}/reclasificar", new { conceptoId = Financiamiento, motivo = "préstamo" }, version);
        Assert.Equal(HttpStatusCode.NoContent, solicitud.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            Assert.Equal(conceptoOriginal, (await db.MovimientosBancarios.AsNoTracking().SingleAsync(m => m.Id == movId)).ConceptoId);
            Assert.Equal(EstadoReclasificacion.Pendiente, (await db.MovimientoReclasificaciones.AsNoTracking().SingleAsync(r => r.MovimientoId == movId)).Estado);
        }
        var lista = await a.GetFromJsonAsync<JsonElement>($"/api/v1/tesoreria/movimientos?cuentaBancariaId={cuentaId}");
        Assert.Equal("préstamo", lista.GetProperty("items")[0].GetProperty("reclasificacionPendiente").GetProperty("motivo").GetString());

        // 2) A no puede autorizar su propia solicitud; sin la facultad, 403.
        var propia = await PostAsync(a, $"{baseUrl}/reclasificacion/autorizar");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
        Assert.Equal("MOV_RECLASIFICACION_MISMO_USUARIO", await AdjuntosProveedorAmbiente.CodigoAsync(propia));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(sinFacultad, $"{baseUrl}/reclasificacion/autorizar")).StatusCode);

        // 3) B autoriza: se aplica el concepto y queda el rastro.
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(b, $"{baseUrl}/reclasificacion/autorizar")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            Assert.Equal(Financiamiento, (await db.MovimientosBancarios.AsNoTracking().SingleAsync(m => m.Id == movId)).ConceptoId);
            var r = await db.MovimientoReclasificaciones.AsNoTracking().SingleAsync(x => x.MovimientoId == movId);
            Assert.Equal((EstadoReclasificacion.Aprobada, aId, bId, conceptoOriginal), (r.Estado, r.SolicitadoPor, r.ResueltoPor, r.ConceptoAnteriorId));
        }

        // 4) El reporte muestra la reclasificación con su rastro.
        var reporte = await a.GetFromJsonAsync<JsonElement>(
            $"/api/v1/tesoreria/reportes/flujo-efectivo?desde={hoy:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&cuentaBancariaId={cuentaId}");
        var fila = reporte.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("tipo").GetString() == "reclasificacion");
        Assert.Equal(-12_500m, fila.GetProperty("neto").GetDecimal());
        Assert.Contains("préstamo", fila.GetProperty("rastro").GetString());
    }
}
