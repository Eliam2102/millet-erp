using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>M-326 parte 2 (b): en el pago a proveedor con redondeo (±≤$0.99) el banco sale por el efectivo y la diferencia va a REDONDEO.</summary>
public class RedondeoPagoProveedorM326Tests
{
    private static EventoContable Pago(decimal monto, decimal? redondeo) =>
        N.Normalizar(N.PagoProveedorAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["EmpresaId"] = Guid.NewGuid(), ["OcurridoEn"] = DateTimeOffset.UtcNow, ["FacturaProveedorId"] = Guid.NewGuid(), ["PagoId"] = Guid.NewGuid(),
            ["Monto"] = monto, ["Moneda"] = "MXN", ["FechaPago"] = new DateOnly(2026, 11, 12), ["ReferenciaBancaria"] = "SPEI-FIX",
        }.Concat(redondeo is { } r ? [new KeyValuePair<string, object?>("Redondeo", r)] : []).ToDictionary()));

    [Fact]
    public void Redondeo_a_favor_banco_por_el_efectivo_y_abono_a_redondeo_la_poliza_cuadra()
    {
        var e = Pago(1_000m, 0.40m);
        e.Importes["Total"].Should().Be(1_000m);
        e.Importes["EfectivoPagado"].Should().Be(999.60m);
        e.Importes["RedondeoAbono"].Should().Be(0.40m);
        e.Importes["RedondeoCargo"].Should().Be(0m);
        // Cargo proveedor (1,000) = abono bancos (999.60) + abono redondeo (0.40).
        (e.Importes["EfectivoPagado"]! + e.Importes["RedondeoAbono"]! - e.Importes["RedondeoCargo"]!).Should().Be(e.Importes["Total"]);
    }

    [Fact]
    public void Redondeo_en_contra_cargo_a_redondeo_y_banco_por_mas()
    {
        var e = Pago(1_000m, -0.30m);
        e.Importes["EfectivoPagado"].Should().Be(1_000.30m);
        e.Importes["RedondeoCargo"].Should().Be(0.30m);
        e.Importes["RedondeoAbono"].Should().Be(0m);
        (e.Importes["EfectivoPagado"]! - e.Importes["RedondeoCargo"]!).Should().Be(e.Importes["Total"]);
    }

    [Fact]
    public void Sin_redondeo_o_evento_anterior_todo_sale_por_bancos()
    {
        foreach (var r in new decimal?[] { 0m, null })
        {
            var e = Pago(1_000m, r);
            e.Importes["EfectivoPagado"].Should().Be(1_000m);
            e.Importes["RedondeoAbono"].Should().Be(0m);
            e.Importes["RedondeoCargo"].Should().Be(0m);
        }
    }
}
