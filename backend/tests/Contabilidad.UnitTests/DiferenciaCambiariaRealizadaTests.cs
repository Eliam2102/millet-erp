using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// CON-11 (86e3kdypd) · diferencia cambiaria realizada: el lado del documento al TC de la factura, el banco al TC del pago y el motor
/// asienta lo que falta para cuadrar en pérdida (cargo) o utilidad (abono). Datos FIX de la ficha (CA10.7 y CON-11-a).
/// </summary>
public class DiferenciaCambiariaRealizadaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static EventoContable PagoProveedor(decimal monto, string moneda = "USD", decimal? tcPasivo = 18.00m, decimal? tcPago = null) =>
        N.Normalizar(N.PagoProveedorAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, FacturaProveedorId = Guid.NewGuid(), PagoId = Guid.NewGuid(),
            Monto = monto, Moneda = moneda, FechaPago = new DateOnly(2026, 11, 6), ReferenciaBancaria = "SPEI-FIX", TipoCambio = tcPasivo,
            TipoCambioPago = tcPago,
        }));

    private static EventoContable CobroCliente(decimal monto, string moneda = "USD") =>
        N.Normalizar(N.PagoClienteConfirmado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, ClienteId = Guid.NewGuid(), MovimientoBancarioId = Guid.NewGuid(),
            CuentaBancariaId = Guid.NewGuid(), Monto = monto, Moneda = moneda, FechaValor = new DateOnly(2026, 11, 6), Referencia = "SPEI-FIX",
            Facturas = new[] { new { FacturaVentaId = Guid.NewGuid(), ImporteAplicado = monto } },
        }));

    private static LineaPlantillaContable Linea(int orden, string rol, LadoPartida lado, string campo) =>
        new() { Orden = orden, Rol = rol, Lado = lado, Campo = campo, Auxiliar = AuxiliarPlantilla.Ninguno };

    /// <summary>Plantilla #5 con las líneas de CON-11 (proveedor por el total, banco por el pago).</summary>
    private static readonly LineaPlantillaContable[] Pago5 =
    [
        Linea(1, "PROVEEDORES", LadoPartida.Cargo, "Total"), Linea(2, "BANCOS", LadoPartida.Abono, "Total"),
        Linea(3, "PERDIDA_CAMBIARIA_REALIZADA", LadoPartida.Cargo, EventoContable.CampoDiferenciaCambiaria),
        Linea(4, "UTILIDAD_CAMBIARIA_REALIZADA", LadoPartida.Abono, EventoContable.CampoDiferenciaCambiaria),
    ];

    /// <summary>Plantilla #12 con las líneas de CON-11.</summary>
    private static readonly LineaPlantillaContable[] Cobro12 =
    [
        Linea(1, "BANCOS", LadoPartida.Cargo, "Total"), Linea(2, "CLIENTES", LadoPartida.Abono, "Aplicado"),
        Linea(3, "PERDIDA_CAMBIARIA_REALIZADA", LadoPartida.Cargo, EventoContable.CampoDiferenciaCambiaria),
        Linea(4, "UTILIDAD_CAMBIARIA_REALIZADA", LadoPartida.Abono, EventoContable.CampoDiferenciaCambiaria),
    ];

    /// <summary>Mismo cálculo que el motor: pesos por línea al TC de su rol y, al final, la diferencia cambiaria.</summary>
    private static (Dictionary<string, (decimal Cargo, decimal Abono)> Partidas, (LadoPartida Lado, decimal Importe)? Diferencia) Simular(
        EventoContable e, IEnumerable<LineaPlantillaContable> plantilla)
    {
        var tc = e.TipoCambio ?? 1m;
        var partidas = new Dictionary<string, (decimal Cargo, decimal Abono)>();
        foreach (var l in plantilla.Where(l => l.Campo != EventoContable.CampoDiferenciaCambiaria))
            foreach (var (importe, _, _, _) in GenerarPolizaDesdeEventoHandler.Importes(l, e))
            {
                var mxn = Math.Round(importe * GenerarPolizaDesdeEventoHandler.TipoCambioDeLinea(e, l.Rol, tc), 2, MidpointRounding.AwayFromZero);
                var previo = partidas.GetValueOrDefault(l.Rol);
                partidas[l.Rol] = l.Lado == LadoPartida.Cargo ? (previo.Cargo + mxn, previo.Abono) : (previo.Cargo, previo.Abono + mxn);
            }
        var dif = GenerarPolizaDesdeEventoHandler.DiferenciaCambiaria(partidas.Values.Sum(p => p.Cargo), partidas.Values.Sum(p => p.Abono));
        return (partidas, dif);
    }

    private static EventoContable ConTc(EventoContable e, decimal tcDocumento, decimal tcPago) => e with
    {
        TipoCambio = tcDocumento, TipoCambioPorRol = DiferenciaCambiariaRealizada.RolesAlTcPago.ToDictionary(r => r, _ => tcPago),
    };

    [Fact]
    public void CA10_7_factura_a_18_pagada_a_18_50_nace_la_perdida_cambiaria_realizada()
    {
        var (partidas, dif) = Simular(ConTc(PagoProveedor(1_000m), 18.00m, 18.50m), Pago5);

        partidas["PROVEEDORES"].Cargo.Should().Be(18_000m, "el proveedor se salda al TC de su factura");
        partidas["BANCOS"].Abono.Should().Be(18_500m, "el banco sale al TC del pago");
        dif.Should().Be((LadoPartida.Cargo, 500m), "1,000 USD × (18.50 − 18.00) = pérdida de 500");
    }

    [Fact]
    public void Pago_a_tc_menor_que_la_factura_da_utilidad_cambiaria()
    {
        var (_, dif) = Simular(ConTc(PagoProveedor(1_000m), 18.00m, 17.80m), Pago5);
        dif.Should().Be((LadoPartida.Abono, 200m));
    }

    [Fact]
    public void CON_11_a_cobro_a_tc_menor_que_la_factura_nace_la_perdida_cambiaria_realizada()
    {
        var (partidas, dif) = Simular(ConTc(CobroCliente(1_000m), 18.00m, 17.50m), Cobro12);

        partidas["CLIENTES"].Abono.Should().Be(18_000m, "el cliente se salda al TC de su factura");
        partidas["BANCOS"].Cargo.Should().Be(17_500m, "entra al banco al TC del cobro");
        dif.Should().Be((LadoPartida.Cargo, 500m));
    }

    [Fact]
    public void Mismo_tc_no_genera_diferencia()
    {
        var (_, dif) = Simular(ConTc(PagoProveedor(1_234.56m), 18.1234m, 18.1234m), Pago5);
        dif.Should().BeNull();
    }

    [Fact]
    public void TES_09_a_pago_en_mxn_no_aplica_diferencia_cambiaria()
    {
        DiferenciaCambiariaRealizada.Aplica(PagoProveedor(5_000m, "MXN", null)).Should().BeFalse();
        DiferenciaCambiariaRealizada.Aplica(CobroCliente(5_000m, "MXN")).Should().BeFalse();
        DiferenciaCambiariaRealizada.Aplica(PagoProveedor(5_000m)).Should().BeTrue();
        DiferenciaCambiariaRealizada.Aplica(CobroCliente(5_000m)).Should().BeTrue();
    }

    [Fact]
    public void En_mxn_el_tc_por_rol_no_se_usa()
    {
        var e = PagoProveedor(100m, "MXN", null) with { TipoCambioPorRol = new Dictionary<string, decimal> { ["BANCOS"] = 18.5m } };
        GenerarPolizaDesdeEventoHandler.TipoCambioDeLinea(e, "BANCOS", 1m).Should().Be(1m);
    }

    [Fact]
    public void El_tc_del_pago_que_mande_tesoreria_tiene_prioridad()
    {
        DiferenciaCambiariaRealizada.TipoCambioPagoDelEvento(PagoProveedor(1m, tcPago: 18.50m).Payload).Should().Be(18.50m);
        DiferenciaCambiariaRealizada.TipoCambioPagoDelEvento(PagoProveedor(1m).Payload).Should().BeNull("hoy no viaja: sale del catálogo K10.6");
    }

    [Fact]
    public void Cobro_de_facturas_con_distinto_tc_usa_el_ponderado_y_cuadra_al_centavo()
    {
        DiferenciaCambiariaRealizada.TipoCambioPonderado([(600m, 18.00m), (400m, 19.00m)]).Should().Be(18.40m);
        DiferenciaCambiariaRealizada.TipoCambioPonderado([(500m, 18.00m), (500m, 18.00m)]).Should().Be(18.00m);
        DiferenciaCambiariaRealizada.TipoCambioPonderado([]).Should().BeNull();

        var (partidas, dif) = Simular(ConTc(CobroCliente(1_000m), 18.40m, 18.10m), Cobro12);
        (partidas.Values.Sum(p => p.Cargo) + (dif?.Lado == LadoPartida.Cargo ? dif.Value.Importe : 0m))
            .Should().Be(partidas.Values.Sum(p => p.Abono) + (dif?.Lado == LadoPartida.Abono ? dif.Value.Importe : 0m));
        dif.Should().Be((LadoPartida.Cargo, 300m));
    }

    [Fact]
    public void Redondeo_por_lado_queda_en_la_diferencia_y_la_poliza_cuadra()
    {
        var (partidas, dif) = Simular(ConTc(PagoProveedor(333.33m), 17.3333m, 18.6667m), Pago5);
        var cargos = partidas.Values.Sum(p => p.Cargo) + (dif?.Lado == LadoPartida.Cargo ? dif.Value.Importe : 0m);
        var abonos = partidas.Values.Sum(p => p.Abono) + (dif?.Lado == LadoPartida.Abono ? dif.Value.Importe : 0m);
        cargos.Should().Be(abonos);
    }
}
