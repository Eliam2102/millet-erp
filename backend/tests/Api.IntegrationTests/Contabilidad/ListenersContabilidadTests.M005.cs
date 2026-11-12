using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.EventListeners;
using Millet.Contabilidad.Infrastructure.Workers;
using Millet.SharedKernel.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

public sealed partial class ListenersContabilidadTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task M005_servicio_CE_reconoce_un_solo_gasto_al_distribuir_y_conserva_servicio_ordinario(bool servicioCe)
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var facturaId = Guid.NewGuid();
            var eventoId = Guid.NewGuid();
            const string tipo = "cuentas_por_pagar.factura.registrada.v1";
            var payload = JsonSerializer.Serialize(new
            {
                EmpresaId = EmpresaBootstrapId, OcurridoEn = Ahora, FacturaProveedorId = facturaId,
                OrdenCompraId = Guid.NewGuid(), TotalFactura = 100m, Subtotal = 100m, Iva = 0m, RetencionesTotal = 0m,
                ProveedorId = e.Proveedor, SucursalId = e.Sucursal, Moneda = "MXN", CentroCostoId = e.CentroCosto,
                EsServicioComercioExterior = servicioCe,
                Lineas = new[] { new { LineaOcId = Guid.NewGuid(), Importe = 100m, CentroCostoId = e.CentroCosto, EsServicio = true } },
            });
            using var worker = Listener<CuentasPorPagarContabilidadListenerWorker>();
            var consumidor = (ILocalIntegrationEventConsumer)worker;
            await consumidor.ConsumirLocalAsync(tipo, eventoId, payload, default);
            await consumidor.ConsumirLocalAsync(tipo, eventoId, payload, default);
            var factura = (await PolizasAsync(facturaId)).Should().ContainSingle().Subject;
            var partidas = factura.Partidas.ToList();
            if (servicioCe)
            {
                var expedienteId = Guid.NewGuid();
                var costo = new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, expedienteId, 1,
                    [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, 0m, 100m)])
                    { SucursalId = e.Sucursal, CentroCostoId = e.CentroCosto };
                var idCosto = await Almacen(costo);
                await Almacen(costo, idCosto);
                partidas.AddRange((await PolizasAsync(expedienteId)).Should().ContainSingle().Subject.Partidas);
                Saldo(partidas, e.Cuentas["GASTOS_IMPORTACION_POR_APLICAR"]).Should().Be(0m, "la factura carga la cuenta por aplicar y CE la abona al reconocer el gasto");
            }
            var gasto = Saldo(partidas, e.Cuentas["GASTO"]) + Saldo(partidas, e.Cuentas["GASTO_IMPORTACION"]);
            gasto.Should().Be(100m, "el servicio, su factura y su distribución representan el mismo gasto");
            Saldo(partidas, e.Cuentas["PROVEEDORES"]).Should().Be(-100m, "una sola deuda al proveedor");
        }
        finally { await LimpiarAsync(e); }
    }
}
