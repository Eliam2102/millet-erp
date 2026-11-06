using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Facturacion.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// C1.6 · IVA pendiente → pagado y no cobrado → cobrado, en proporción a lo pagado o cobrado (criterios C1.6-a y C1.6-b de la ficha
/// 86e3jvvm9). Mismos listeners y escenario que C1.5. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static FacturaProveedorRegistradaIntegrationEvent FacturaProveedor(Escenario e, Guid factura, decimal subtotal, decimal iva,
        decimal retencionIva = 0m) =>
        new(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), subtotal + iva,
            [new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, subtotal)], [], ProveedorId: e.Proveedor, Uuid: $"UUID-{factura:N}",
            Subtotal: subtotal, Iva: iva, RetencionesTotal: retencionIva, Moneda: "MXN", SucursalId: e.Sucursal,
            Retenciones: retencionIva > 0m ? [new RetencionDetallePayload("002", 0.04m, retencionIva)] : null);

    private static PagoFacturaProveedorAplicadoIntegrationEvent PagoProveedor(Guid factura, Guid pago, decimal monto) =>
        new(EmpresaBootstrapId, Ahora, factura, pago, monto, "MXN", Hoy(), "Transferencia", "REF-FIX");

    private async Task<SaldoIvaDocumento> SaldoIvaAsync(Guid documento)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        return await db.SaldosIvaDocumento.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.DocumentoId == documento);
    }

    [Fact]
    public async Task C16a_la_factura_pagada_al_50_por_ciento_pasa_800_de_IVA_a_pagado_y_el_segundo_pago_salda_el_resto()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var (pago1, pago2) = (Guid.NewGuid(), Guid.NewGuid());
            await Cxp(FacturaProveedor(e, factura, 10_000m, 1_600m));
            await Tesoreria(PagoProveedor(factura, pago1, 5_800m));

            var pp = (await PolizasAsync(pago1)).Should().ContainSingle().Subject;
            pp.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PAGADO"] && x.Cargo == 800m);
            pp.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PENDIENTE"] && x.Abono == 800m);
            Saldo(await PartidasSucursalAsync(e.Sucursal), e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Should().Be(800m);

            await Tesoreria(PagoProveedor(factura, pago2, 5_800m));
            var partidas = await PartidasSucursalAsync(e.Sucursal);
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Should().Be(0m, "el pago que salda la factura deja el pendiente en cero");
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PAGADO"]).Should().Be(1_600m);
            partidas.Sum(x => x.Cargo).Should().Be(partidas.Sum(x => x.Abono));
            var saldo = await SaldoIvaAsync(factura);
            (saldo.Aplicado, saldo.IvaReclasificado, saldo.Saldado).Should().Be((11_600m, 1_600m, true));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C16a_tres_pagos_iguales_no_dejan_centavos_el_ultimo_toma_el_remanente()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var pagos = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            await Cxp(FacturaProveedor(e, factura, 1_000m, 160m));
            await Tesoreria(PagoProveedor(factura, pagos[0], 386.67m));
            await Tesoreria(PagoProveedor(factura, pagos[1], 386.67m));
            await Tesoreria(PagoProveedor(factura, pagos[2], 386.66m));

            var iva = new List<decimal>();
            foreach (var p in pagos)
                iva.Add((await PolizasAsync(p)).Single().Partidas.Single(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PAGADO"]).Cargo);
            iva.Should().Equal(53.33m, 53.33m, 53.34m);
            Saldo(await PartidasSucursalAsync(e.Sucursal), e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Should().Be(0m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C16b_el_pago_revertido_regresa_el_IVA_a_pendiente_y_un_pago_nuevo_lo_vuelve_a_reclasificar()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var (pago, nuevo) = (Guid.NewGuid(), Guid.NewGuid());
            await Cxp(FacturaProveedor(e, factura, 10_000m, 1_600m));
            await Tesoreria(PagoProveedor(factura, pago, 5_800m));
            await Tesoreria(new PagoFacturaProveedorRevertidoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, pago, 5_800m, "MXN", Hoy(),
                "Rechazo del banco"));

            var partidas = await PartidasSucursalAsync(e.Sucursal);
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Should().Be(1_600m, "la anulación del pago revierte su reclasificación");
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PAGADO"]).Should().Be(0m);
            var saldo = await SaldoIvaAsync(factura);
            (saldo.Aplicado, saldo.IvaReclasificado).Should().Be((0m, 0m));

            await Tesoreria(PagoProveedor(factura, nuevo, 11_600m));
            partidas = await PartidasSucursalAsync(e.Sucursal);
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Should().Be(0m);
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PAGADO"]).Should().Be(1_600m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C16a_retencion_de_IVA_del_flete_se_reclasifica_en_proporcion_y_queda_en_el_saldo()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            // Flete: 1,000 + IVA 160 − retención de IVA 40 = 1,120 por pagar.
            await Cxp(FacturaProveedor(e, factura, 1_000m, 160m, retencionIva: 40m));
            await Tesoreria(PagoProveedor(factura, Guid.NewGuid(), 560m));

            var saldo = await SaldoIvaAsync(factura);
            (saldo.Base, saldo.IvaRetenido, saldo.IvaReclasificado, saldo.IvaRetenidoReclasificado).Should().Be((1_120m, 40m, 80m, 20m));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C16c_cobro_de_cliente_y_cobro_mixto_de_caja_pasan_el_IVA_a_cobrado_y_el_efectivo_lo_hace_en_la_liquidacion()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var (credito, mostrador) = (Guid.NewGuid(), Guid.NewGuid());
            await Facturacion(FacturaVenta(e, credito, e.Clientes[0]));
            await Facturacion(FacturaVenta(e, mostrador, e.Clientes[1]));

            // #12: pago de cliente por la mitad de la factura a crédito.
            var movimiento = Guid.NewGuid();
            await Tesoreria(new PagoClienteConfirmadoIntegrationEvent(EmpresaBootstrapId, Ahora, null, e.Clientes[0], movimiento, Guid.NewGuid(),
                1_160m, "MXN", Hoy(), "REF-FIX", [new PagoClienteFacturaAplicada(credito, 1_160m)]));
            var pr = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_NO_COBRADO"] && x.Cargo == 160m);
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_COBRADO"] && x.Abono == 160m);

            // #11a + #11b: 1,000 con tarjeta y 1,320 en efectivo de la factura de mostrador (IVA 320).
            var sesion = Guid.NewGuid();
            var cobro = CobroDe(e, sesion, e.Clientes[1], mostrador, ("04", 1_000m), ("01", 1_320m));
            await Facturacion(cobro);
            (await PolizasAsync(cobro.CobroMostradorId)).Should().ContainSingle().Which.Partidas
                .Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_COBRADO"] && x.Abono == 137.93m);
            await Facturacion(SesionCerrada(e, sesion, 1_320m));
            (await PolizasAsync(sesion)).Should().ContainSingle().Which.Partidas
                .Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_COBRADO"] && x.Abono == 182.07m, "el efectivo salda el remanente");

            var partidas = await PartidasSucursalAsync(e.Sucursal);
            Saldo(partidas, e.Cuentas["IVA_TRASLADADO_NO_COBRADO"]).Should().Be(-160m, "queda solo la mitad sin cobrar de la factura a crédito");
            Saldo(partidas, e.Cuentas["IVA_TRASLADADO_COBRADO"]).Should().Be(-480m);
            partidas.Sum(x => x.Cargo).Should().Be(partidas.Sum(x => x.Abono));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C16b_el_cobro_en_efectivo_cancelado_ya_liquidado_regresa_su_IVA_a_no_cobrado()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0], 500m, 80m));
            var sesion = Guid.NewGuid();
            var cobro = CobroDe(e, sesion, e.Clientes[0], factura, ("01", 580m));
            await Facturacion(cobro);
            await Facturacion(SesionCerrada(e, sesion, 580m));
            Saldo(await PartidasSucursalAsync(e.Sucursal), e.Cuentas["IVA_TRASLADADO_COBRADO"]).Should().Be(-80m);

            await Facturacion(new CobroMostradorCanceladoIntegrationEvent(EmpresaBootstrapId, Ahora, cobro.CobroMostradorId, factura, 580m,
                ReversadoEnSesion: false));

            var partidas = await PartidasSucursalAsync(e.Sucursal);
            Saldo(partidas, e.Cuentas["IVA_TRASLADADO_COBRADO"]).Should().Be(0m);
            Saldo(partidas, e.Cuentas["IVA_TRASLADADO_NO_COBRADO"]).Should().Be(-80m);
            (await SaldoIvaAsync(factura)).IvaReclasificado.Should().Be(0m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task K107_la_liquidacion_del_mes_lista_cada_documento_con_su_IVA_y_su_cumplimiento_y_arma_el_borrador_de_poliza()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var (proveedorCompleto, proveedorParcial, ventaPpd) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await Cxp(FacturaProveedor(e, proveedorCompleto, 1_000m, 160m));
            await Cxp(FacturaProveedor(e, proveedorParcial, 10_000m, 1_600m));
            await Tesoreria(PagoProveedor(proveedorCompleto, Guid.NewGuid(), 1_160m));
            await Tesoreria(PagoProveedor(proveedorParcial, Guid.NewGuid(), 5_800m));
            await Facturacion(FacturaVenta(e, ventaPpd, e.Clientes[0]) with { MetodoPago = "PPD" });
            await Tesoreria(new PagoClienteConfirmadoIntegrationEvent(EmpresaBootstrapId, Ahora, null, e.Clientes[0], Guid.NewGuid(), Guid.NewGuid(),
                2_320m, "MXN", Hoy(), "REF-FIX", [new PagoClienteFacturaAplicada(ventaPpd, 2_320m)]));

            var hoy = Hoy();
            var resp = await e.Admin.GetAsync($"/api/v1/contabilidad/iva/liquidacion?anio={hoy.Year}&mes={hoy.Month}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var docs = json.GetProperty("documentos").EnumerateArray().ToDictionary(d => d.GetProperty("documentoId").GetGuid());

            var completo = docs[proveedorCompleto];
            (completo.GetProperty("ivaMes").GetDecimal(), completo.GetProperty("completo").GetBoolean()).Should().Be((160m, true));
            var parcial = docs[proveedorParcial];
            parcial.GetProperty("ivaMes").GetDecimal().Should().Be(800m);
            parcial.GetProperty("motivos").EnumerateArray().Select(m => m.GetString()).Should().Equal("Con saldo por pagar");
            var venta = docs[ventaPpd];
            (venta.GetProperty("ivaMes").GetDecimal(), venta.GetProperty("metodoPago").GetString()).Should().Be((320m, "PPD"));
            venta.GetProperty("motivos").EnumerateArray().Select(m => m.GetString()).Should().Equal("PPD pagado sin REP");

            // K10.7-a: por documento cuadra con el movimiento del mes en las cuentas del rol (las cuentas FIX de esta prueba).
            var partidas = await PartidasSucursalAsync(e.Sucursal);
            (-Saldo(partidas, e.Cuentas["IVA_TRASLADADO_COBRADO"])).Should().Be(venta.GetProperty("ivaMes").GetDecimal());
            Saldo(partidas, e.Cuentas["IVA_ACREDITABLE_PAGADO"]).Should().Be(960m);
            json.GetProperty("cuadre").EnumerateArray().Select(c => c.GetProperty("rol").GetString())
                .Should().BeEquivalentTo("IVA_TRASLADADO_COBRADO", "IVA_ACREDITABLE_PAGADO");

            // Borrador de la póliza de liquidación con las cuentas de los roles.
            var poliza = await e.Admin.PostAsJsonAsync("/api/v1/contabilidad/iva/liquidacion/poliza",
                new { anio = hoy.Year, mes = hoy.Month, sucursalId = e.Sucursal });
            poliza.StatusCode.Should().Be(HttpStatusCode.Created, await poliza.Content.ReadAsStringAsync());
            var p = await poliza.Content.ReadFromJsonAsync<JsonElement>();
            p.GetProperty("estado").GetString().Should().Be("Borrador");
            var cuentasPoliza = p.GetProperty("partidas").EnumerateArray().Select(x => x.GetProperty("cuentaId").GetGuid()).ToList();
            cuentasPoliza.Should().Contain([e.Cuentas["IVA_TRASLADADO_COBRADO"], e.Cuentas["IVA_ACREDITABLE_PAGADO"]]);
        }
        finally { await LimpiarAsync(e); }
    }
}
