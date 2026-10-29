using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>HTTP y PostgreSQL desechable. Limpia los registros propios y usa el catálogo existente para áreas.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class PrubObservacionesEndpointsTests(PrubObservacionesEndpointsTests.Servidor factory) : IClassFixture<PrubObservacionesEndpointsTests.Servidor>
{
    public sealed class Servidor : WebApplicationFactory<Program>
    {
        public WebApplicationFactory<Program> Empresa { get; }
        public Servidor() => Empresa = this.ConEmpresa(EmpresaBootstrapId);
    }

    [Fact]
    public async Task Proveedor_en_revision_conserva_motivo_y_no_se_puede_usar_en_compras()
    {
        Sobre.Value = EmpresaBootstrapId;
        var app = factory.Empresa;
        using var client = await LoginAsync(app);
        var id = Guid.CreateVersion7();
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                db.Proveedores.Add(new Proveedor(id, "FIX-" + Sufijo(), "Proveedor PRU-B ficticio", "AAA010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo));
                await db.SaveChangesAsync();
            }
            var res = await client.PostAsJsonAsync($"/api/v1/catalogos/proveedores/{id}/poner-revision", new { motivo = "Verificar expediente bancario" });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("Verificar expediente bancario", (await Json(res)).GetProperty("motivoRevision").GetString());
            using var verificar = app.Services.CreateScope();
            var proveedor = await verificar.ServiceProvider.GetRequiredService<CompartidoDbContext>().Proveedores.SingleAsync(p => p.Id == id);
            Assert.Equal(EstatusCatalogo.EnRevision, proveedor.Estatus);
            var activos = await client.GetAsync("/api/v1/catalogos/proveedores?estatus=0&limit=200");
            Assert.DoesNotContain((await Json(activos)).GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
            var nuevaOrden = await client.PostAsJsonAsync("/api/v1/compras/ordenes", new {
                proveedorId = id, sucursalDestinoId = Guid.NewGuid(), sucursalCodigo = "MID", folioAnio = 2026,
                condicionesPagoId = Guid.NewGuid(), usoPrincipalId = Guid.NewGuid(), fechaDocumento = "2026-10-11",
                sinRequisicionPrevia = true, motivoSinRequisicion = "Prueba ficticia de proveedor en revisión",
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, nuevaOrden.StatusCode);
            Assert.Equal("PROVEEDOR_EN_REVISION", await Code(nuevaOrden));
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            await db.Proveedores.Where(p => p.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Areas_revisoras_del_endpoint_corresponden_a_departamentos_activos()
    {
        Sobre.Value = EmpresaBootstrapId;
        var app = factory.Empresa;
        using var client = await LoginAsync(app);
        var res = await client.GetAsync("/api/v1/cuentas-por-pagar/catalogos/dependencias-revisoras");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var ids = await db.Departamentos.Where(d => d.Estatus == EstatusCatalogo.Activo).Select(d => d.Id).ToListAsync();
        var reales = (await Json(res)).EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(ids.Order(), reales.Order());
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Beneficiario_de_credito_se_valida_al_crear_y_en_el_selector(bool permitido)
    {
        var port = new Beneficiarios(permitido);
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        await using var app = enEmpresa.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
            s.RemoveAll<IBeneficiarioCreditoReadPort>(); s.AddSingleton<IBeneficiarioCreditoReadPort>(port);
        }));
        using var client = await LoginAsync(app);
        var referencia = "FIX-PRUB-" + Sufijo();
        try
        {
            var listado = await client.GetAsync("/api/v1/cuentas-por-cobrar/autorizaciones/beneficiarios");
            Assert.Equal(permitido ? 1 : 0, (await Json(listado)).GetArrayLength());
            var res = await client.PostAsJsonAsync("/api/v1/cuentas-por-cobrar/autorizaciones", new { beneficiarioUsuarioId = port.Id, motivo = "Pago en tránsito", clienteOPedidoRef = referencia, vigenciaHoras = 24 });
            Assert.Equal(permitido ? HttpStatusCode.Created : HttpStatusCode.UnprocessableEntity, res.StatusCode);
            if (!permitido) Assert.Equal("AC_BENEFICIARIO_SIN_PERMISO", await Code(res));
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().AutorizacionesCredito.Where(a => a.ClienteOPedidoRef == referencia).ExecuteDeleteAsync();
        }
    }
    private sealed class Beneficiarios(bool permitido) : IBeneficiarioCreditoReadPort
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<bool> PuedeDecidirAsync(Guid id, Guid empresa, CancellationToken ct) => Task.FromResult(permitido && id == Id);
        public Task<IReadOnlyList<BeneficiarioCreditoDto>> ListarAsync(Guid empresa, CancellationToken ct) => Task.FromResult<IReadOnlyList<BeneficiarioCreditoDto>>(permitido ? [new(Id, "Beneficiario PRU-B", "prub@example.invalid")] : []);
    }
}
