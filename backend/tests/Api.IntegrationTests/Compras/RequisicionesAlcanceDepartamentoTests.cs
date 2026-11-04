using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// F-RQOC-01 (H-01): la requisitante sin <c>ver-todos-departamentos</c> no ve en la bandeja ni en el
/// detalle las RQ de otro departamento; sí las de su departamento y las que ella pidió.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class RequisicionesAlcanceDepartamentoTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = ContabTestKit.EmpresaBootstrapId;
    private static readonly Guid Sucursal = Guid.Parse("00000005-0003-0000-0000-000000000001");

    [Fact]
    public async Task Requisitante_solo_ve_rq_de_su_departamento_o_propias()
    {
        var requisitante = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.ComprasRequisicionesLeer, "compras.requisiciones.leer-todas-sucursales");
        var supervisor = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.ComprasRequisicionesLeer, "compras.requisiciones.leer-todas-sucursales",
            PermisosCanonicos.ComprasRequisicionesVerTodosDepartamentos);
        var miDepto = Guid.NewGuid(); var otroDepto = Guid.NewGuid(); var otro = Guid.NewGuid();
        var ids = new List<Guid>();
        try
        {
            Guid usuarioId;
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                usuarioId = ContabTestKit.UsuarioIdDe(requisitante);
                var yo = await identidad.Usuarios.SingleAsync(u => u.Id == usuarioId);
                yo.AsignarDepartamento(miDepto);
                await identidad.SaveChangesAsync();

                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                Requisicion Rq(Guid depto, Guid requisitanteId) => new(Guid.CreateVersion7(), Empresa,
                    Folio.Parse($"RQD2026-{Random.Shared.Next(100000, 999999)}"), 2026, Clasificacion.OrdenCompra, Sucursal,
                    depto, null, requisitanteId, requisitanteId, Prioridad.Normal, DateTimeOffset.UtcNow);
                var deMiDepto = Rq(miDepto, otro); var ajena = Rq(otroDepto, otro); var mia = Rq(otroDepto, usuarioId);
                compras.AddRange(deMiDepto, ajena, mia);
                await compras.SaveChangesAsync();
                ids.AddRange([deMiDepto.Id, ajena.Id, mia.Id]);
            }

            var bandeja = await requisitante.GetStringAsync("/api/v1/compras/requisiciones?limit=200");
            Assert.Contains(ids[0].ToString(), bandeja);
            Assert.Contains(ids[2].ToString(), bandeja);
            Assert.DoesNotContain(ids[1].ToString(), bandeja);
            using (var detalle = await requisitante.GetAsync($"/api/v1/compras/requisiciones/{ids[1]}"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, detalle.StatusCode);
                Assert.Contains("DEPARTAMENTO_NO_ASOCIADO", await detalle.Content.ReadAsStringAsync());
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await requisitante.GetAsync($"/api/v1/compras/requisiciones/{ids[1]}/historico")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await requisitante.GetAsync($"/api/v1/compras/requisiciones/{ids[0]}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await requisitante.GetAsync($"/api/v1/compras/requisiciones/{ids[2]}")).StatusCode);

            // Con ver-todos-departamentos no hay restricción.
            Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/v1/compras/requisiciones/{ids[1]}")).StatusCode);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones
                    .Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();
            }
            await ContabTestKit.LimpiarClienteAsync(requisitante);
            await ContabTestKit.LimpiarClienteAsync(supervisor);
        }
    }
}
