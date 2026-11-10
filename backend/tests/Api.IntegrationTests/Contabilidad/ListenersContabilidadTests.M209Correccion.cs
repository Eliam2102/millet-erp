using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-209 (86e3zr7mp) · CA4: una factura de proveedor con IVA capturado 1,500 cuyo XML dice 1,600 se recalcula con motivo: nace una
/// póliza de ajuste por 100 ligada a la misma factura, la original no se anula y el saldo de IVA queda en 1,600. El XML lo da un puerto
/// falso (el real lo lee CxP de su buzón). Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private sealed class XmlFijo(string uuid, decimal iva) : ITrasladosIvaCfdiReadPort
    {
        public Task<IReadOnlyList<TrasladoIvaDocumento>?> LeerAsync(string u, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrasladoIvaDocumento>?>(string.Equals(u, uuid, StringComparison.OrdinalIgnoreCase)
                ? [new TrasladoIvaDocumento("TASA16", 10_000m, iva)] : null);
    }

    [Fact]
    public async Task M209_CA4_recalcular_el_iva_contra_el_xml_genera_la_poliza_de_ajuste_sin_anular_la_original()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var uuid = $"{Guid.NewGuid()}".ToUpperInvariant();
            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), 11_500m,
                [new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 10_000m)], [], ProveedorId: e.Proveedor, Uuid: uuid,
                Subtotal: 10_000m, Iva: 1_500m, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: e.Sucursal,
                Impuestos: [new ImpuestoTrasladoPayload("TASA16", 10_000m, 1_500m)]));
            Guid saldoId;
            using (var scope = factory.Services.CreateScope())
                saldoId = await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().SaldosIvaDocumento.IgnoreQueryFilters()
                    .Where(s => s.DocumentoId == factura).Select(s => s.Id).SingleAsync();

            await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.AddScoped<ITrasladosIvaCfdiReadPort>(_ => new XmlFijo(uuid, 1_600m))));
            var admin = await LoginAsync(app);
            var url = $"{Base}/iva/documentos/{saldoId}/recalcular";

            // Vista previa: 100 de diferencia, sin póliza.
            var previa = await admin.PostAsJsonAsync(url, new { motivo = (string?)null, aplicar = false });
            previa.StatusCode.Should().Be(HttpStatusCode.OK);
            var p = await Json(previa);
            p.GetProperty("ivaDiferencia").GetDecimal().Should().Be(100m);
            p.GetProperty("polizaAjusteId").ValueKind.Should().Be(JsonValueKind.Null);
            (await PolizasAsync(factura)).Should().ContainSingle();

            // Sin motivo no se aplica.
            (await admin.PostAsJsonAsync(url, new { motivo = "corto", aplicar = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var aplicada = await admin.PostAsJsonAsync(url, new { motivo = "FIX el XML trae 1,600 de IVA", aplicar = true });
            aplicada.StatusCode.Should().Be(HttpStatusCode.OK);
            var ajusteId = (await Json(aplicada)).GetProperty("polizaAjusteId").GetGuid();

            var polizas = await PolizasAsync(factura);
            polizas.Should().HaveCount(2, "la original y la de ajuste, ligadas a la misma factura");
            polizas.Should().OnlyContain(x => x.Estado == EstadoPoliza.Autorizada, "la original no se anula");
            var ajuste = polizas.Single(x => x.Id == ajusteId);
            ajuste.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PENDIENTE"] && x.Cargo == 100m);
            ajuste.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PROVEEDORES"] && x.Abono == 100m && x.ProveedorId == e.Proveedor);

            using (var scope = factory.Services.CreateScope())
            {
                var saldo = await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().SaldosIvaDocumento.IgnoreQueryFilters()
                    .AsNoTracking().SingleAsync(s => s.Id == saldoId);
                saldo.Iva.Should().Be(1_600m);
            }

            // Ya coincide: aplicar otra vez no hay qué corregir.
            (await admin.PostAsJsonAsync(url, new { motivo = "FIX el XML trae 1,600 de IVA", aplicar = true })).StatusCode
                .Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        finally { await LimpiarAsync(e); }
    }
}
