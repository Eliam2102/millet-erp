using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ComercioExterior;

[Collection(ApiCompartida.Nombre)]
public sealed class SelectorFacturasServiciosHttpTests
{
    [Fact]
    public async Task Selector_incluye_servicio_tras_N1_y_excluye_orden_normal_pendiente_N2_antes_de_paginar()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        Guid id, pendienteNormalId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var original = await db.OrdenesCompra.Include(o => o.Lineas).FirstAsync(o => o.Id == DemoSesionSeedHostedService.Id("DEMO-CE-P6/ORDEN"));
            var oc = new OrdenCompra(Guid.NewGuid(), original.EmpresaId, Folio.Parse("OC-DEMO2026-000999"), 2026,
                original.ProveedorId, original.SucursalDestinoId, original.CondicionesPagoId, original.UsoPrincipalId,
                original.CompradorTitularId, original.EncargadoComprasId, new(2026, 11, 11), sinRequisicionPrevia: true,
                motivoSinRequisicion: "Prueba ficticia de firmas CE", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99", esServicioComercioExterior: true);
            oc.AgregarLineaManual(Guid.NewGuid(), original.Lineas.First().ArticuloId, 1, "SRV", 100, original.Lineas.First().DepartamentoSolicitanteId, esServicio: true);
            oc.RecalcularImpuestos(); oc.EnviarAAutorizacion(e.Ahora, requiereNivel2: false);
            db.OrdenesCompra.Add(oc); id = oc.Id;
            var normal = new OrdenCompra(Guid.NewGuid(), original.EmpresaId, Folio.Parse("OC-DEMO2026-000998"), 2026,
                original.ProveedorId, original.SucursalDestinoId, original.CondicionesPagoId, original.UsoPrincipalId,
                original.CompradorTitularId, original.EncargadoComprasId, new(2026, 11, 11), sinRequisicionPrevia: true,
                motivoSinRequisicion: "Prueba ficticia normal", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
            normal.AgregarLineaManual(Guid.NewGuid(), original.Lineas.First().ArticuloId, 1, "SRV", 100, original.Lineas.First().DepartamentoSolicitanteId, esServicio: true);
            normal.RecalcularImpuestos(); normal.EnviarAAutorizacion(e.Ahora, requiereNivel2: true);
            normal.Autorizar(Guid.NewGuid(), Millet.Compras.Domain.NivelAutorizacion.Nivel1, DemoSesionSeedHostedService.JefeComprasDemoId, e.Ahora, "Prueba ficticia");
            db.OrdenesCompra.Add(normal); pendienteNormalId = normal.Id; await db.SaveChangesAsync();
        }
        string[] genericos = [PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1, PermisosCanonicos.ComprasOrdenesAutorizarNivel2];
        using var sinCe = await e.ClienteAsync(false, extra: genericos);
        using var n1 = await e.ClienteAsync(false, extra: [..genericos, PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1, PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2]);
        async Task<ListarOrdenesCompraResponse> Elegibles() => (await n1.GetFromJsonAsync<ListarOrdenesCompraResponse>(
            "/api/v1/compras/ordenes?paraCapturaFactura=true&q=OC-DEMO2026-00099&pageSize=1"))!;
        var antes = await Elegibles(); Assert.Empty(antes.Items); Assert.Equal(0, antes.TotalCount);
        async Task<HttpResponseMessage> Firma(HttpClient c, int nivel) => await c.PostAsJsonAsync($"/api/v1/compras/ordenes/{id}/autorizaciones", new { nivel, notas = "Prueba ficticia CE" });
        using var denegada = await Firma(sinCe, 1); Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
        using var primera = await Firma(n1, 1); Assert.True(primera.IsSuccessStatusCode, await primera.Content.ReadAsStringAsync());
        var elegibles = await Elegibles(); Assert.Equal(1, elegibles.TotalCount);
        Assert.Equal(id, Assert.Single(elegibles.Items).Id); Assert.True(elegibles.Items[0].EsServicioComercioExterior);
        Assert.DoesNotContain(elegibles.Items, o => o.Id == pendienteNormalId);

    }
}
