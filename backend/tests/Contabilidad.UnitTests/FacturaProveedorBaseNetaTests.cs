using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// La TT de la factura de proveedor cuadra aunque el CFDI traiga descuento o centavos de diferencia dentro de la tolerancia de CxP.
/// Antes la base iba bruta (Subtotal del CFDI): la TT no cuadraba, quedaba rechazada sin remedio y el pago esperaba su póliza para
/// siempre. Plantilla #2 vigente (puente, gasto, IVA, proveedor por el total más retenciones) más las líneas REDONDEO. Datos FIX.
/// </summary>
public class FacturaProveedorBaseNetaTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private static readonly List<LineaPlantillaContable> PlantillaTt =
    [
        new() { Orden = 1, Rol = "MERCANCIA_RECIBIDA_POR_FACTURAR", Lado = LadoPartida.Cargo, Campo = "Subtotal", PorLinea = true, SoloServicios = false },
        new() { Orden = 2, Rol = "GASTO", Lado = LadoPartida.Cargo, Campo = "Subtotal", PorLinea = true, SoloServicios = true, ConCentroCosto = true },
        new() { Orden = 3, Rol = "IVA_ACREDITABLE_PENDIENTE", Lado = LadoPartida.Cargo, Campo = "Iva" },
        new() { Orden = 4, Rol = "PROVEEDORES", Lado = LadoPartida.Abono, Campo = RetencionesContables.CampoTotalMas, Auxiliar = AuxiliarPlantilla.Proveedor },
        new() { Orden = 5, Rol = "REDONDEO", Lado = LadoPartida.Cargo, Campo = "RedondeoCargo" },
        new() { Orden = 6, Rol = "REDONDEO", Lado = LadoPartida.Abono, Campo = "RedondeoAbono" },
    ];

    private static EventoContable Factura(decimal subtotal, decimal iva, decimal total, params object[] lineas) =>
        N.Normalizar(N.FacturaProveedorRegistrada, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaProveedorId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid(), TotalFactura = total,
            Lineas = lineas, ProveedorId = Proveedor, Uuid = "UUID-FIX", Subtotal = subtotal, Iva = iva, RetencionesTotal = 0m, Moneda = "MXN",
            SucursalId = Sucursal,
        }));

    private static (decimal Cargos, decimal Abonos, decimal Puente, decimal Redondeo) Partidas(EventoContable e)
    {
        var partidas = PlantillaTt.SelectMany(l => GenerarPolizaDesdeEventoHandler.Importes(l, e).Where(x => x.Importe != 0m)
            .Select(x => (l.Rol, l.Lado, x.Importe))).ToList();
        decimal Suma(Func<(string Rol, LadoPartida Lado, decimal Importe), bool> f) => partidas.Where(f).Sum(p => p.Importe);
        return (Suma(p => p.Lado == LadoPartida.Cargo), Suma(p => p.Lado == LadoPartida.Abono),
            Suma(p => p.Rol == "MERCANCIA_RECIBIDA_POR_FACTURAR"),
            Suma(p => p.Rol == "REDONDEO" && p.Lado == LadoPartida.Cargo) - Suma(p => p.Rol == "REDONDEO" && p.Lado == LadoPartida.Abono));
    }

    [Fact]
    public void La_factura_con_descuento_carga_el_puente_por_la_base_neta_y_cuadra()
    {
        // CFDI: subtotal 10,000 − descuento 500 = 9,500 + IVA 1,520 = 11,020. CxP manda la base neta por línea en ValorCapitalizable.
        var e = Factura(10_000m, 1_520m, 11_020m,
            new { LineaOcId = Guid.NewGuid(), Importe = 6_000m, ValorCapitalizable = 5_700m, EsServicio = false },
            new { LineaOcId = Guid.NewGuid(), Importe = 4_000m, ValorCapitalizable = 3_800m, EsServicio = false });

        Partidas(e).Should().Be((11_020m, 11_020m, 9_500m, 0m), "puente 9,500 + IVA 1,520 = PROVEEDORES 11,020, sin redondeo");
    }

    [Fact]
    public void Los_centavos_dentro_de_la_tolerancia_van_a_REDONDEO_y_la_TT_cuadra()
    {
        // CxP aceptó el total del CFDI 0.40 arriba de subtotal + IVA (tolerancia $0.99); y un prorrateo de descuento con decimales.
        var e = Factura(1_000m, 160m, 1_160.40m,
            new { LineaOcId = Guid.NewGuid(), Importe = 1_000m, ValorCapitalizable = 999.996m, EsServicio = false });

        Partidas(e).Should().Be((1_160.40m, 1_160.40m, 1_000m, 0.40m), "la línea se redondea a 1,000.00 y los 0.40 van al Debe de REDONDEO");

        var abajo = Factura(1_000m, 160m, 1_159.70m, new { LineaOcId = Guid.NewGuid(), Importe = 1_000m, EsServicio = false });
        Partidas(abajo).Should().Be((1_160m, 1_160m, 1_000m, -0.30m), "PROVEEDORES 1,159.70 + REDONDEO 0.30 al Haber = puente 1,000 + IVA 160");
    }

    [Fact]
    public void Sin_desglose_de_lineas_la_base_es_la_neta_del_CFDI()
    {
        var e = Factura(10_000m, 1_520m, 11_020m);

        e.Importes["Subtotal"].Should().Be(9_500m);
        Partidas(e).Should().Be((11_020m, 11_020m, 9_500m, 0m));
    }
}
