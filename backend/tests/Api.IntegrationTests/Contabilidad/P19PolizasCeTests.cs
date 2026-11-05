using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
namespace Millet.Api.IntegrationTests.Contabilidad;
[Collection(ApiCompartida.Nombre)]
public sealed class P19PolizasCeTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData("costo", 2000, 1000)]
    [InlineData("costo", 0, 3000)]
    [InlineData("pago", 50000, 0)]
    [InlineData("devolucion", 3000, 0)]
    public async Task Evento_ce_genera_una_poliza_cuadrada_y_reentrega_no_duplica(string caso, decimal inventario, decimal gasto)
    {
        using var client = await LoginAsync(factory);
        var fecha = Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        await AsegurarPeriodosAbiertosAsync(client, fecha);
        var suf = Sufijo();
        using var sucursalResp = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"P19{suf}", Nombre = $"FIX CE {suf}" });
        sucursalResp.EnsureSuccessStatusCode(); var sucursal = (await Json(sucursalResp)).GetProperty("id").GetGuid();
        var padre = (await CrearCuenta(client, Codigo(suf, "1"), "FIX CE padre")).GetProperty("id").GetGuid();
        var cuentas = new Dictionary<string, Guid>();
        foreach (var (rol, numero) in new[] { ("INVENTARIO", "1.1"), ("GASTO_IMPORTACION", "1.2"), ("GASTOS_IMPORTACION_POR_APLICAR", "1.3"), ("BANCOS", "1.4"), ("ANTICIPO_AGENTE_ADUANAL", "1.5") })
            cuentas[rol] = (await CrearCuenta(client, Codigo(suf, numero), $"FIX {rol}", padre)).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            foreach (var (rol, cuenta) in cuentas)
            {
                var r = RolCuenta.Crear(Guid.NewGuid(), rol, "FIX CE por sucursal", cuenta, sucursal, "MXN", new(2000, 1, 1));
                r.EmpresaId = EmpresaBootstrapId; db.RolesCuenta.Add(r);
            }
            await db.SaveChangesAsync();
        }
        var tipo = caso == "costo" ? NormalizadorEventos.CostoImportacionAplicado : caso == "pago" ? NormalizadorEventos.PagoProveedorAplicado : NormalizadorEventos.DevolucionAnticipoAgente;
        var expediente = Guid.NewGuid(); var evento = Guid.NewGuid();
        var payload = new { EmpresaId = EmpresaBootstrapId, OcurridoEn = DateTimeOffset.UtcNow, ExpedienteId = expediente,
            SucursalId = sucursal, NumeroVersion = 1, PagoId = expediente, FacturaProveedorId = Guid.NewGuid(),
            DevolucionId = expediente, EsAnticipoAgente = caso == "pago", ProveedorId = Guid.NewGuid(),
            Monto = inventario + gasto, FechaPago = fecha, FechaValor = fecha, ReferenciaBancaria = "FIX-BANCO-P19", Moneda = "MXN",
            Partidas = new[] { new { ArticuloId = Guid.NewGuid(),
                DiferenciaMxn = inventario + gasto, InventarioMxn = inventario, GastoImportacionMxn = gasto } } };
        using var primera = await client.PostAsJsonAsync("/api/v1/contabilidad/motor/eventos", new { tipo, eventoId = evento, payload });
        primera.EnsureSuccessStatusCode(); var id = (await Json(primera)).GetProperty("poliza").GetProperty("id").GetGuid();
        using var repetida = await client.PostAsJsonAsync("/api/v1/contabilidad/motor/eventos", new { tipo, eventoId = evento, payload });
        repetida.EnsureSuccessStatusCode(); Assert.Equal(id, (await Json(repetida)).GetProperty("poliza").GetProperty("id").GetGuid());
        using var verificar = factory.Services.CreateScope(); var contexto = verificar.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var poliza = await contexto.Polizas.IgnoreQueryFilters().Include(p => p.Partidas).SingleAsync(p => p.Id == id);
        Assert.Equal(inventario + gasto, poliza.TotalCargos); Assert.Equal(poliza.TotalCargos, poliza.TotalAbonos);
        Assert.Equal(1, await contexto.Polizas.IgnoreQueryFilters().CountAsync(p => p.OrigenDocumentoId == expediente));
        if (caso == "costo")
        {
        Assert.Equal(inventario, poliza.Partidas.Where(p => p.CuentaId == cuentas["INVENTARIO"]).Sum(p => p.Cargo));
        Assert.Equal(gasto, poliza.Partidas.Where(p => p.CuentaId == cuentas["GASTO_IMPORTACION"]).Sum(p => p.Cargo));
        }
        else
        {
            var cargo = caso == "pago" ? "ANTICIPO_AGENTE_ADUANAL" : "BANCOS";
            var abono = caso == "pago" ? "BANCOS" : "ANTICIPO_AGENTE_ADUANAL";
            Assert.Equal(inventario, poliza.Partidas.Single(p => p.CuentaId == cuentas[cargo]).Cargo);
            Assert.Equal(inventario, poliza.Partidas.Single(p => p.CuentaId == cuentas[abono]).Abono);
        }
    }
}
