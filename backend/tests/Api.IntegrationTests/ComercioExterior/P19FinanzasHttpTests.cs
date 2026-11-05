using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.IntegrationTests.ComercioExterior;
[Collection(ApiCompartida.Nombre)]
public sealed class P19FinanzasHttpTests
{
    [Fact]
    public async Task Solicitud_sin_archivo_de_calculo_se_rechaza()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await e.ClienteAsync(true);
        using var creado = await client.PostAsJsonAsync("/api/v1/comercio-exterior/expedientes", e.Importacion());
        creado.EnsureSuccessStatusCode();
        var body = await creado.Content.ReadFromJsonAsync<JsonElement>(); var id = body.GetProperty("expediente").GetProperty("id").GetGuid();
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var maestro = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
        var agente = await maestro.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-ACT");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/comercio-exterior/expedientes/{id}/anticipos")
        { Content = JsonContent.Create(new { agenteId = agente.Id, pedimento = "P19-PED", impuestos = new { iva = 50000, igi = 0, prv = 0 }, importe = 50000, fecha = "2026-10-01" }) };
        request.Headers.Add("If-Match", creado.Headers.ETag!.Tag); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var respuesta = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("CE_EVIDENCIA_REQUERIDA", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task Factura_unica_tambien_se_protege_por_indice_entre_expedientes()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        var empresa = Millet.Api.Seed.DemoSesionSeedHostedService.EmpresaId;
        var c = new ConceptoGasto(empresa, "P19-" + Guid.NewGuid().ToString()[..8], "Prueba P19"); db.ConceptosGasto.Add(c);
        var factura = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        Expediente Crear(string folio) => new(Guid.NewGuid(), empresa, folio, TipoExpediente.Importacion, e.SucursalId, "P19", Guid.NewGuid(), e.Incoterm, "MXN", e.Responsable, []);
        var primero = Crear("CE-P19-" + Guid.NewGuid().ToString()[..8]); var segundo = Crear("CE-P19-" + Guid.NewGuid().ToString()[..8]);
        var datos = new DatosGasto(c.Id, proveedor, factura.ToString(), "MXN", 100, new(2026, 10, 1), FacturaProveedorId: factura);
        primero.GuardarGasto(Guid.NewGuid(), datos, c, 1, "MXN"); db.Expedientes.Add(primero); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ValidacionesFinancieras.FacturaUnicaAsync(db, empresa, factura, Guid.NewGuid(), default));
        Assert.Contains(primero.Folio, error.Message, StringComparison.Ordinal);
        segundo.GuardarGasto(Guid.NewGuid(), datos, c, 1, "MXN"); db.Expedientes.Add(segundo);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
