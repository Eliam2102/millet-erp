using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// H-U29 · la reapertura que resuelve una incidencia se elige de la lista del periodo (no se pega su identificador). Se siembra una
/// reapertura del periodo de hoy directo en la base y el GET la devuelve con su evidencia; un periodo inválido responde 400. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task HU29_lista_las_reaperturas_del_periodo_para_elegirlas()
    {
        Escenario? e = null;
        Guid? reaperturaId = null;
        try
        {
            e = await PrepararAsync();
            var (anio, numero) = RegistroPolizas.Periodo(Hoy(), null);
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                var periodo = await db.Periodos.SingleAsync(p => p.EmpresaId == EmpresaBootstrapId && p.Anio == anio && p.Numero == numero);
                var ahora = DateTimeOffset.UtcNow;
                var r = new ReaperturaPeriodo(Guid.CreateVersion7(), periodo, null, null, null, null, $"FIX evidencia HU29 {e.Suf}",
                    ahora.AddDays(2), 0, "FIX contador", ahora) { EmpresaId = EmpresaBootstrapId };
                db.Reaperturas.Add(r);
                await db.SaveChangesAsync();
                reaperturaId = r.Id;
            }

            var lista = await Json(await e.Admin.GetAsync($"{Base}/periodos/reaperturas?anio={anio}&numero={numero}"));
            var fila = lista.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == reaperturaId);
            fila.GetProperty("evidenciaReferencia").GetString().Should().Be($"FIX evidencia HU29 {e.Suf}");
            fila.GetProperty("periodo").GetString().Should().Be(PeriodoContable.Etiqueta(anio, numero));
            fila.GetProperty("vigente").GetBoolean().Should().BeTrue();

            var invalido = await e.Admin.GetAsync($"{Base}/periodos/reaperturas?anio={anio}&numero=14");
            invalido.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
        finally
        {
            if (reaperturaId is Guid id)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reaperturas_periodo WHERE id = {0}", id);
            }
            await LimpiarAsync(e);
        }
    }
}
