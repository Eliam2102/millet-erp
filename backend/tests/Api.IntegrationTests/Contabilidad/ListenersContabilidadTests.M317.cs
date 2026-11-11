using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Revaluacion;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-317 (86e4084j4) · vista previa, anulación y comprobación de la revaluación de bancos y clientes. La póliza de revaluación del mes
/// se siembra por el puerto de pólizas con las cuentas FIX (la anulación no depende de la moneda). Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private const string RevaluacionUrl = Base + "/revaluacion-cierre";

    private async Task<Guid> SembrarRevaluacionAsync(Escenario e, string llave)
    {
        using var scope = factory.Services.CreateScope();
        var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
        var docId = CalculadoraRevaluacionCierre.DocumentoId(EmpresaBootstrapId, Hoy().Year, Hoy().Month);
        var r = await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica("AS", Hoy(), "FIX revaluación",
            new DocumentoOrigenPoliza(CalculadoraRevaluacionCierre.Modulo, CalculadoraRevaluacionCierre.TipoRevaluacion, docId, "REV-FIX", null), llave,
            [new PartidaRequest(e.Cuentas["BANCOS"], "FIX ajuste", 100m, 0m, e.Sucursal),
             new PartidaRequest(e.Cuentas["UTILIDAD_CAMBIARIA_NO_REALIZADA"], "FIX utilidad", 0m, 100m, e.Sucursal)],
            OrigenMovimiento.Automatica, EmpresaBootstrapId), CancellationToken.None);
        return r.Id;
    }

    private static async Task<HttpResponseMessage> AnularRevaluacionAsync(Escenario e, string motivo) =>
        await Send(e.Admin, HttpMethod.Post, RevaluacionUrl + "/anular", new { Anio = Hoy().Year, Mes = Hoy().Month, Motivo = motivo });

    [Fact]
    public async Task M317_la_vista_previa_no_crea_polizas_y_anular_exige_motivo_y_revaluacion_vigente()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var antes = await Contar(factory.Services, "polizas");
            var vista = await Send(e.Admin, HttpMethod.Get, $"{RevaluacionUrl}?anio={Hoy().Year}&mes={Hoy().Month}");
            vista.EnsureSuccessStatusCode();
            (await Json(vista)).GetProperty("ejecutada").GetBoolean().Should().BeFalse();
            (await Contar(factory.Services, "polizas")).Should().Be(antes);

            var sinMotivo = await AnularRevaluacionAsync(e, "no");
            sinMotivo.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
            var sinVigente = await AnularRevaluacionAsync(e, "Motivo de prueba de la anulación");
            sinVigente.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
            (await Code(sinVigente)).Should().Be("REVALUACION_NO_EJECUTADA");
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task M317_anular_se_niega_con_la_reversa_corrida_y_procede_sin_ella_dejando_el_mes_libre()
    {
        Escenario? e = null;
        var docId = CalculadoraRevaluacionCierre.DocumentoId(EmpresaBootstrapId, Hoy().Year, Hoy().Month);
        try
        {
            e = await PrepararAsync();
            var original = await SembrarRevaluacionAsync(e, $"fix.m317:{e.Suf}:1");

            // Reversa registrada (la del día 1): ya no se puede anular.
            using (var scope = factory.Services.CreateScope())
            {
                var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
                await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica("AS", Hoy(), "FIX reversa",
                    new DocumentoOrigenPoliza(CalculadoraRevaluacionCierre.Modulo, CalculadoraRevaluacionCierre.TipoReversa, original, "REV-FIX", null),
                    $"fix.m317:{e.Suf}:rev",
                    [new PartidaRequest(e.Cuentas["BANCOS"], "FIX reversa", 0m, 100m, e.Sucursal),
                     new PartidaRequest(e.Cuentas["UTILIDAD_CAMBIARIA_NO_REALIZADA"], "FIX reversa", 100m, 0m, e.Sucursal)],
                    OrigenMovimiento.Automatica, EmpresaBootstrapId), CancellationToken.None);
            }
            var negada = await AnularRevaluacionAsync(e, "Motivo de prueba de la anulación");
            negada.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
            (await Code(negada)).Should().Be("REVALUACION_REVERSA_YA_CORRIDA");

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE origen_documento_id = {0}", original);
            }

            var ok = await AnularRevaluacionAsync(e, "Motivo de prueba de la anulación");
            ok.EnsureSuccessStatusCode();
            (await Json(ok)).GetProperty("ejecutada").GetBoolean().Should().BeFalse();
            (await PolizasAsync(docId)).Should().Contain(p => p.Id == original && p.Estado == EstadoPoliza.Anulada);
            (await PolizasAsync(docId)).Should().Contain(p => p.PolizaAnuladaId == original);

            // Sin revaluación vigente otra vez: el mes queda libre para registrar otra.
            var otra = await SembrarRevaluacionAsync(e, $"fix.m317:{e.Suf}:2");
            otra.Should().NotBe(original);
        }
        finally { await BorrarPolizasDeAsync(docId); await LimpiarAsync(e); }
    }
}
