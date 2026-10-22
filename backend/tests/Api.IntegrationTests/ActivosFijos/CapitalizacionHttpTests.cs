using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Api.Seed;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
/// <summary>PostgreSQL y puerto contable reales. Solo el documento proveedor se fija para aislar la prueba del timbrado.</summary>
public sealed class CapitalizacionHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Alta_y_nota_generan_polizas_reales_sin_duplicar_y_periodo_no_abierto_rechaza(bool periodoCerrado)
    {
        var factura = Guid.NewGuid(); var nota = Guid.NewGuid();
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(s =>
            s.Replace(ServiceDescriptor.Singleton<ICapitalizacionProveedorReadPort>(new DocumentoFixture(factura, nota))));
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var id = Guid.NewGuid(); var empresa = DemoSesionSeedHostedService.EmpresaId;
        using (var scope = e.Scope())
        {
            var sp = scope.ServiceProvider; using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var contab = sp.GetRequiredService<ContabilidadDbContext>();
            var activoCuenta = new CuentaContable(Guid.NewGuid(), "FIX-AF-A", "FIX Activo", null, 2, NaturalezaCuenta.Deudora, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null) { EmpresaId = empresa };
            var puente = new CuentaContable(Guid.NewGuid(), "FIX-AF-P", "FIX Puente", null, 2, NaturalezaCuenta.Acreedora, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null) { EmpresaId = empresa };
            contab.Cuentas.AddRange(activoCuenta, puente);
            var ejercicio = new EjercicioContable(Guid.NewGuid(), 2027) { EmpresaId = empresa }; contab.Ejercicios.Add(ejercicio);
            foreach (var p in ejercicio.GenerarPeriodos()) { p.EmpresaId = empresa; if (p.Numero == 1 && !periodoCerrado) p.Abrir(null, null, e.Responsable, "FIX AF", DateTimeOffset.UtcNow); contab.Periodos.Add(p); }
            var centros = sp.GetRequiredService<CentrosCostoDbContext>();
            var centro = new Dim1(Guid.NewGuid(), "FIXAF", "FIX ubicación AF"); centros.Dim1s.Add(centro); await centros.SaveChangesAsync();
            contab.UbicacionesSucursal.Add(new UbicacionSucursal(Guid.NewGuid(), centro.Id, e.SucursalId) { EmpresaId = empresa }); await contab.SaveChangesAsync();
            var db = sp.GetRequiredService<ActivosFijosDbContext>();
            var clase = await db.Clases.SingleAsync(x => x.EmpresaId == empresa && x.Clave == "154");
            clase.Editar(clase.Nombre, 10, activoCuenta.Id, null, null, puente.Id, true);
            var a = new Activo(id, empresa, "FIX-AF-500K", new DatosActivo("FIX Montacargas", clase.Id, null, null, null, new DateOnly(2027,1,1), new DateOnly(2027,1,15), 0, "MXN", 1,
                new CustodiaActivo(e.Responsable, e.SucursalId, "FIX ubicación", centro.Id, null), TipoImportacionActivo.No, null, false, null), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
            a.ConciliarFactura(factura, 500000, "MXN", 1); db.Activos.Add(a); await db.SaveChangesAsync();
        }
        async Task<HttpResponseMessage> Accion(string accion, object cuerpo)
        {
            using var get = await cliente.GetAsync($"/api/v1/activos-fijos/activos/{id}"); get.EnsureSuccessStatusCode();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/activos-fijos/activos/{id}/{accion}") { Content = JsonContent.Create(cuerpo) };
            req.Headers.TryAddWithoutValidation("If-Match", get.Headers.ETag!.Tag); req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await cliente.SendAsync(req);
        }
        using var alta = await Accion("capitalizar", new {});
        Assert.Equal(periodoCerrado ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK, alta.StatusCode);
        if (!periodoCerrado)
        {
            using var repetida = await Accion("capitalizar", new {}); repetida.EnsureSuccessStatusCode();
            var cuerpo = new { ajuste = new { documentoId = nota, facturaId = factura, fecha = "2027-01-20", importe = 20000, descripcion = "FIX devolución parcial" } };
            using var nc = await Accion("nota-credito", cuerpo); nc.EnsureSuccessStatusCode();
            using var repetidaNc = await Accion("nota-credito", cuerpo); repetidaNc.EnsureSuccessStatusCode();
        }
        using var ver = e.Scope(); using var lectura = ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var af = await ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.Include(x=>x.Historial).SingleAsync(x=>x.Id==id);
        var polizas = await ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Include(x=>x.Partidas).Where(x=>x.OrigenModulo=="ActivosFijos").ToListAsync();
        if (periodoCerrado) { Assert.Equal(EstadoActivo.PorCapitalizar, af.Estado); Assert.Null(af.PolizaAltaId); Assert.Empty(polizas); }
        else { Assert.Equal(EstadoActivo.Activo, af.Estado); Assert.Equal(480000, af.ValorEnLibros); Assert.Single(af.Historial); Assert.Equal(2, polizas.Count); Assert.All(polizas, p=>Assert.Equal(p.Partidas.Sum(x=>x.Cargo),p.Partidas.Sum(x=>x.Abono))); Assert.Single(polizas, p=>p.LlaveIdempotencia==$"AF-CAP-{id}"); }
    }
    private sealed class DocumentoFixture(Guid factura, Guid nota) : ICapitalizacionProveedorReadPort
    {
        public Task<DocumentoCapitalizacion?> ObtenerAsync(Guid documentoId, Guid facturaId, bool notaCredito, CancellationToken ct)
            => Task.FromResult<DocumentoCapitalizacion?>(notaCredito && documentoId == nota && facturaId == factura ? new(nota, factura, 20000) : null);
    }
}
