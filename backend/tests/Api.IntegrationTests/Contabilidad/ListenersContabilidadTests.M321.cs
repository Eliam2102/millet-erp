using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Cierre;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-321 (86e4084j7) · incidencia posterior ligada a una póliza autorizada: aparece como pendiente en el cierre del periodo y al ligarla
/// a su póliza de ajuste queda resuelta. Las pólizas se siembran por el puerto con cuentas FIX. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private const string IncidenciasUrl = Base + "/incidencias-posteriores";

    private async Task<Guid> SembrarPolizaAsync(Escenario e, string llave)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
        var r = await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica("AS", Hoy(), "FIX póliza M-321",
            new DocumentoOrigenPoliza("contabilidad", "fix_m321", Guid.NewGuid(), "FIX-M321", null), llave,
            [new PartidaRequest(e.Cuentas["BANCOS"], "FIX cargo", 100m, 0m, e.Sucursal),
             new PartidaRequest(e.Cuentas["UTILIDAD_CAMBIARIA_NO_REALIZADA"], "FIX abono", 0m, 100m, e.Sucursal)],
            OrigenMovimiento.Automatica, EmpresaBootstrapId), CancellationToken.None);
        return r.Id;
    }

    private async Task<string> EstadoPasoIncidenciasAsync(Escenario e)
    {
        Guid periodoId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var (anio, numero) = RegistroPolizas.Periodo(Hoy(), null);
            periodoId = await db.Periodos.Where(p => p.Anio == anio && p.Numero == numero).Select(p => p.Id).SingleAsync();
        }
        var checklist = await Json(await e.Admin.GetAsync($"{Base}/cierre/periodos/{periodoId}/checklist"));
        var paso = checklist.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == PasosCierre.IncidenciasPosteriores);
        return paso.GetProperty("estado").GetString()!;
    }

    [Fact]
    public async Task M321_la_incidencia_abierta_bloquea_el_cierre_y_al_ligar_su_poliza_de_ajuste_queda_resuelta()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var original = await SembrarPolizaAsync(e, $"fix.m321:{e.Suf}:1");
            var ajuste = await SembrarPolizaAsync(e, $"fix.m321:{e.Suf}:2");

            var registrada = await Send(e.Admin, HttpMethod.Post, IncidenciasUrl,
                new { PolizaId = original, Hallazgo = "Importe duplicado en la póliza", Impacto = "Banco sobrestimado", ImporteImpacto = 100m });
            registrada.EnsureSuccessStatusCode();
            var id = Id(await Json(registrada));
            (await EstadoPasoIncidenciasAsync(e)).Should().Be("Pendiente");

            var misma = await Send(e.Admin, HttpMethod.Post, $"{IncidenciasUrl}/{id}/resolver", new { PolizaAjusteId = original });
            misma.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);

            var resuelta = await Send(e.Admin, HttpMethod.Post, $"{IncidenciasUrl}/{id}/resolver", new { PolizaAjusteId = ajuste, Nota = "Ajuste registrado" });
            resuelta.EnsureSuccessStatusCode();
            (await Json(resuelta)).GetProperty("estado").GetString().Should().Be("Resuelta");
            var otra = await Send(e.Admin, HttpMethod.Post, $"{IncidenciasUrl}/{id}/resolver", new { PolizaAjusteId = ajuste });
            otra.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
            (await EstadoPasoIncidenciasAsync(e)).Should().Be("Cumple");
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.incidencias_posteriores WHERE hallazgo = 'Importe duplicado en la póliza'");
            await LimpiarAsync(e);
        }
    }
}
