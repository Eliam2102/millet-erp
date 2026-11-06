using Millet.Contabilidad.Application.ConciliacionAuxiliares;

namespace Millet.Contabilidad.UnitTests.ConciliacionAuxiliares;

/// <summary>
/// F1-CON-09: comparaciones puras de la conciliación de auxiliares. Estado del control (nunca «Cuadra» sin cuenta), diferencias por
/// proveedor, causa de un documento sin póliza (CON-09-a) y reconstrucción de saldos al corte (cartera y libros de bancos).
/// </summary>
public class ComparadorAuxiliaresTests
{
    private static readonly DateOnly Corte = new(2026, 1, 31);
    private static readonly DateTimeOffset Fin = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sin_cuenta_configurada_nunca_cuadra_aunque_ambos_saldos_sean_cero()
    {
        ComparadorAuxiliares.Estado(tieneCuenta: false, 0m, 0m).Should().Be(EstadoConciliacion.SinCuenta);
        ComparadorAuxiliares.Etiqueta(EstadoConciliacion.SinCuenta).Should().Be("Sin cuenta configurada");
    }

    [Theory]
    [InlineData(1_000.00, 1_000.00, EstadoConciliacion.Cuadra)]
    [InlineData(1_000.00, 1_000.01, EstadoConciliacion.Cuadra)]
    [InlineData(1_000.00, 1_000.02, EstadoConciliacion.Diferencia)]
    [InlineData(0, 500, EstadoConciliacion.Diferencia)]
    public void Con_cuenta_compara_con_tolerancia_de_un_centavo(decimal contable, decimal auxiliar, EstadoConciliacion esperado) =>
        ComparadorAuxiliares.Estado(true, contable, auxiliar).Should().Be(esperado);

    [Fact]
    public void Sin_saldo_del_auxiliar_es_no_comparable() =>
        ComparadorAuxiliares.Estado(true, 1_000m, null).Should().Be(EstadoConciliacion.NoComparable);

    [Fact]
    public void El_saldo_de_proveedores_es_acreedor()
    {
        // Factura de 1,160 (abono a proveedores) y pago de 160 (cargo): cargo − abono = −1,000 → saldo acreedor 1,000.
        ComparadorAuxiliares.SaldoAcreedor(160m - 1_160m).Should().Be(1_000m);
    }

    [Fact]
    public void Diferencias_por_proveedor_solo_devuelve_los_que_no_coinciden_de_mayor_a_menor()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var contable = new Dictionary<Guid, decimal> { [a] = 1_000m, [b] = 500m, [c] = 300m };
        var auxiliar = new Dictionary<Guid, decimal> { [a] = 1_000m, [b] = 450m, [d] = 2_000m };

        var difs = ComparadorAuxiliares.DiferenciasPorTercero(contable, auxiliar);

        difs.Select(x => x.ProveedorId).Should().Equal(d, c, b);
        difs[0].Should().Be((d, 0m, 2_000m)); // con saldo en CxP y sin póliza
        difs[1].Should().Be((c, 300m, 0m));   // póliza sin documento vivo en CxP
        difs[2].Should().Be((b, 500m, 450m)); // importe distinto
    }

    [Fact]
    public void CON_09_a_documento_sin_poliza_muestra_la_causa_del_rechazo_del_motor()
    {
        ComparadorAuxiliares.CausaSinPoliza("RolSinCuenta · El rol PROVEEDORES no tiene cuenta", null)
            .Should().Be("El evento no generó póliza: RolSinCuenta · El rol PROVEEDORES no tiene cuenta");
        ComparadorAuxiliares.CausaSinPoliza(null, new DateOnly(2026, 2, 3))
            .Should().Be("La póliza tiene fecha posterior al corte (2026-02-03)");
        ComparadorAuxiliares.CausaSinPoliza(null, null).Should().StartWith("El evento no generó póliza");
    }

    [Fact]
    public void Cartera_al_corte_ignora_pagos_posteriores_y_cuenta_los_revertidos_despues_del_corte()
    {
        var movimientos = new[]
        {
            (200m, new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), false, (DateTimeOffset?)null), // vigente: resta
            (300m, new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero), false, (DateTimeOffset?)null),  // posterior al corte: no resta
            (100m, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), true,
                (DateTimeOffset?)new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero)),                    // revertido antes: no resta
            (50m, new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero), true,
                (DateTimeOffset?)new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)),                    // revertido después: sí resta
        };

        ComparadorAuxiliares.SaldoCarteraAlCorte(1_160m, movimientos, Fin).Should().Be(910m);
    }

    [Fact]
    public void Libros_de_bancos_al_corte_suman_saldo_inicial_y_movimientos_y_no_existen_antes_del_saldo_inicial()
    {
        ComparadorAuxiliares.SaldoLibrosAlCorte(10_000m, new DateOnly(2025, 12, 31), Corte, 2_500m).Should().Be(12_500m);
        ComparadorAuxiliares.SaldoLibrosAlCorte(null, null, Corte, -300m).Should().Be(-300m);
        ComparadorAuxiliares.SaldoLibrosAlCorte(10_000m, new DateOnly(2026, 2, 28), Corte, 0m).Should().BeNull();
    }
}
