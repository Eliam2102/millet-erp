using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.CuentasPorCobrar.Application.ConciliacionPolizas;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// CXC-06 (86e3qddef) · pólizas de cobro conciliadas con la cartera, con el motor contable real (plantilla #12 v2 sembrada por la
/// migración): el ajuste autorizado va a la cuenta de su tipo, un faltante sin autorizar no se absorbe, el cobro genera su póliza una
/// sola vez y la conciliación de CxC cuadra cartera contra pólizas y lista las diferencias. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static readonly Guid Proponente = Guid.Parse("c0600000-0000-0000-0000-00000000000a");
    private static readonly Guid Autorizador = Guid.Parse("c0600000-0000-0000-0000-00000000000b");

    /// <summary>Propuesta de CxC con depósito corto documentado; autorizada por otra persona si <paramref name="autorizar"/>.</summary>
    private async Task<PropuestaAplicacionPago> PropuestaConAjusteAsync(Guid cliente, Guid facturaCartera, decimal deposito, decimal aplicado,
        TipoAjusteCobro tipo, bool autorizar)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var p = PropuestaAplicacionPago.Crear(EmpresaBootstrapId, cliente, $"DEP-C06-{Guid.NewGuid():N}"[..20], deposito, "MXN", "REM-C06",
            [(Guid.NewGuid().ToString(), facturaCartera, aplicado, null)], 1_000m, Proponente, Guid.NewGuid(),
            tipo, "Comisión del banco del cliente (FIX)");
        if (autorizar) p.AutorizarAjuste(Autorizador, DateTimeOffset.UtcNow);
        db.PropuestasAplicacionPago.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static PagoClienteConfirmadoIntegrationEvent PagoCliente(PropuestaAplicacionPago p, Guid facturaVenta, decimal aplicado) =>
        new(EmpresaBootstrapId, Ahora, p.Id, p.ClienteId, p.MovimientoBancarioId!.Value, Guid.NewGuid(), p.MontoDeposito, "MXN", Hoy(),
            "SPEI-C06", [new PagoClienteFacturaAplicada(facturaVenta, aplicado)], ConfirmadaPor: Autorizador);

    private async Task LimpiarCxcAsync(params Guid[] clientes)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        foreach (var c in clientes)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM cuentas_por_cobrar.propuesta_aplicacion_pago WHERE cliente_id = {0}", c);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM cuentas_por_cobrar.movimiento_cartera WHERE factura_cartera_id IN (SELECT id FROM cuentas_por_cobrar.factura_cartera WHERE cliente_id = {0})", c);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM cuentas_por_cobrar.factura_cartera WHERE cliente_id = {0}", c);
        }
    }

    [Fact]
    public async Task CXC06_pago_corto_con_ajuste_autorizado_carga_la_diferencia_a_su_cuenta_y_abona_a_clientes_lo_aplicado()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));
            var p = await PropuestaConAjusteAsync(e.Clientes[0], Guid.NewGuid(), 2_300m, 2_320m, TipoAjusteCobro.ComisionBancaria, autorizar: true);

            await Tesoreria(PagoCliente(p, factura, 2_320m));

            var pr = (await PolizasAsync(p.MovimientoBancarioId!.Value)).Should().ContainSingle().Subject;
            pr.TipoClave.Should().Be("PR");
            pr.Estado.Should().Be(EstadoPoliza.Autorizada);
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Cargo == 2_300m);
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["COMISIONES_BANCARIAS"] && x.Cargo == 20m);
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Abono == 2_320m && x.ClienteId == e.Clientes[0]);
            pr.Partidas.Should().NotContain(x => x.CuentaId == e.Cuentas["DIFERENCIAS_EN_COBRO"]);

            // Cartera y póliza cuadran: la factura RF de 2,320 queda saldada en clientes.
            var partidas = await PartidasSucursalAsync(e.Sucursal);
            partidas.Sum(x => x.Cargo).Should().Be(partidas.Sum(x => x.Abono));
            Saldo(partidas, e.Cuentas["CLIENTES"]).Should().Be(0m);
        }
        finally { await LimpiarAsync(e); if (e is not null) await LimpiarCxcAsync(e.Clientes); }
    }

    [Fact]
    public async Task CXC06_faltante_sin_ajuste_autorizado_no_se_absorbe_y_queda_en_la_bandeja_de_rechazos()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[1]));
            var p = await PropuestaConAjusteAsync(e.Clientes[1], Guid.NewGuid(), 2_300m, 2_320m, TipoAjusteCobro.DiferenciaMenor, autorizar: false);

            var evento = await Tesoreria(PagoCliente(p, factura, 2_320m));

            (await PolizasAsync(p.MovimientoBancarioId!.Value)).Should().BeEmpty();
            var rechazo = await RechazoAsync(evento);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
            rechazo.Detalle.Should().Be("AjusteAutorizado");
        }
        finally { await LimpiarAsync(e); if (e is not null) await LimpiarCxcAsync(e.Clientes); }
    }

    [Fact]
    public async Task CXC06_el_cobro_genera_su_poliza_una_sola_vez_aunque_el_evento_llegue_dos_veces()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[2]));
            var p = await PropuestaConAjusteAsync(e.Clientes[2], Guid.NewGuid(), 2_310m, 2_320m, TipoAjusteCobro.DiferenciaMenor, autorizar: true);
            var pago = PagoCliente(p, factura, 2_320m);

            var id = await Tesoreria(pago);
            await Tesoreria(pago, id);

            var pr = (await PolizasAsync(p.MovimientoBancarioId!.Value)).Should().ContainSingle().Subject;
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["DIFERENCIAS_EN_COBRO"] && x.Cargo == 10m);
        }
        finally { await LimpiarAsync(e); if (e is not null) await LimpiarCxcAsync(e.Clientes); }
    }

    [Fact]
    public async Task CXC06_conciliacion_cuadra_el_REP_con_su_poliza_y_lista_el_cobro_sin_poliza()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var cliente = e.Clientes[0];
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, cliente));

            // Cartera: la factura, su REP con ajuste (2,320 aplicado por un depósito de 2,300) y un cobro de mostrador sin póliza.
            Guid rep = Guid.NewGuid(), cobroSinPoliza = Guid.NewGuid();
            PropuestaAplicacionPago p;
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
                var fc = FacturaCartera.Crear(EmpresaBootstrapId, factura, cliente, "XAXX010101000", $"FIX cliente C06 {e.Suf}",
                    factura.ToString(), "FV-C06", 2_320m, "MXN", "PPD", Ahora.AddDays(-5), Ahora.AddDays(25));
                var otra = FacturaCartera.Crear(EmpresaBootstrapId, Guid.NewGuid(), cliente, "XAXX010101000", $"FIX cliente C06 {e.Suf}",
                    Guid.NewGuid().ToString(), "FV-C06B", 500m, "MXN", "PUE", Ahora.AddDays(-5), Ahora.AddDays(25));
                db.FacturasCartera.AddRange(fc, otra);
                db.MovimientosCartera.Add(new MovimientoCartera(EmpresaBootstrapId, fc.Id, TipoMovimientoCartera.Pago, rep, 2_320m, Ahora));
                db.MovimientosCartera.Add(new MovimientoCartera(EmpresaBootstrapId, otra.Id, TipoMovimientoCartera.Pago, cobroSinPoliza, 500m, Ahora));
                await db.SaveChangesAsync();
                p = PropuestaAplicacionPago.Crear(EmpresaBootstrapId, cliente, "DEP-C06-CONC", 2_300m, "MXN", "REM-C06",
                    [(fc.Uuid, fc.Id, 2_320m, 1)], 1_000m, Proponente, Guid.NewGuid(), TipoAjusteCobro.ComisionBancaria, "Comisión (FIX)");
                p.AutorizarAjuste(Autorizador, Ahora);
                p.Confirmar(Autorizador, Ahora);
                p.MarcarReppTimbrado(rep);
                db.PropuestasAplicacionPago.Add(p);
                await db.SaveChangesAsync();
            }
            await Tesoreria(PagoCliente(p, factura, 2_320m));

            var r = await e.Admin.GetFromJsonAsync<ConciliacionCobrosResponse>(
                $"/api/v1/cuentas-por-cobrar/cartera/conciliacion-polizas?desde={Hoy():yyyy-MM-dd}&hasta={Hoy():yyyy-MM-dd}",
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

            var filaRep = r!.Filas.Should().ContainSingle(f => f.DocumentoId == p.MovimientoBancarioId).Subject;
            filaRep.Estado.Should().Be(EstadoConciliacionCobro.Cuadra);
            filaRep.ImporteCartera.Should().Be(2_320m);
            filaRep.ImportePolizas.Should().Be(2_320m);
            filaRep.Polizas.Should().ContainSingle().Which.Should().StartWith("PR-");

            var filaSinPoliza = r.Filas.Should().ContainSingle(f => f.DocumentoId == cobroSinPoliza).Subject;
            filaSinPoliza.Estado.Should().Be(EstadoConciliacionCobro.Diferencia);
            filaSinPoliza.Diferencia.Should().Be(500m);
            filaSinPoliza.Causa.Should().Contain("no tiene póliza");
        }
        finally { await LimpiarAsync(e); if (e is not null) await LimpiarCxcAsync(e.Clientes); }
    }
}
