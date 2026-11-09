using System.Text.Json;
using Millet.Contabilidad.Application.Iva;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-209 (86e3zr7mp) · IVA por tratamiento (CON-07-01): la factura de proveedor, sus notas y la factura de venta separan el IVA por
/// tratamiento (16 %, 8 %, 0 %, exento, no objeto); el IVA va al rol {base}_{tratamiento} (o al base si no tiene cuenta) y el saldo de
/// IVA del documento guarda base e IVA por tratamiento para la reclasificación y la liquidación. Sin detalle, como hoy. Datos FIX.
/// </summary>
public class M209IvaPorTratamientoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid(), Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);

    private static EventoContable Normalizar(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), JsonSerializer.Serialize(payload));

    // CA1: 10,000 al 16 % (IVA 1,600) y 2,500 exentos → CFDI 14,100.
    private static EventoContable FacturaProveedor(bool conDetalle = true) => Normalizar(N.FacturaProveedorRegistrada, new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaProveedorId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid(), TotalFactura = 14_100m,
        Lineas = new object[] { new { LineaOcId = Guid.NewGuid(), Importe = 12_500m, EsServicio = true } },
        ProveedorId = Proveedor, Uuid = "UUID-FIX", Subtotal = 12_500m, Iva = 1_600m, Moneda = "MXN", SucursalId = Sucursal,
        Impuestos = conDetalle
            ? new object[] { new { Tratamiento = "TASA16", Base = 10_000m, Iva = 1_600m }, new { Tratamiento = "EXENTO", Base = 2_500m, Iva = 0m } }
            : null,
    });

    private static LineaPlantillaContable IvaPendiente(string rol = "IVA_ACREDITABLE_PENDIENTE", LadoPartida lado = LadoPartida.Cargo) =>
        new() { Rol = rol, Lado = lado, Campo = "Iva", PorLinea = true, Grupo = LineaEventoContable.GrupoIva, RolPorClasificador = true };

    private static List<(string? Clasificador, decimal Importe)> Partidas(LineaPlantillaContable l, EventoContable e) =>
        [.. GenerarPolizaDesdeEventoHandler.Importes(l, e).Where(x => x.Importe != 0m).Select(x => (x.Linea?.Clasificador, x.Importe))];

    [Fact]
    public void CA1_la_factura_de_proveedor_lleva_el_iva_del_16_y_la_base_exenta_no_genera_partida()
    {
        var e = FacturaProveedor();

        Partidas(IvaPendiente(), e).Should().Equal(new (string?, decimal)[] { ("TASA16", 1_600m) });
        e.Lineas.Where(l => l.Grupo == null).Sum(l => l.Importe).Should().Be(12_500m, "las líneas del documento no cambian");
    }

    [Fact]
    public void CA2_la_factura_de_venta_al_8_lleva_el_iva_trasladado_con_clasificador_TASA8()
    {
        var e = Normalizar(N.FacturaVentaTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaVentaId = Guid.NewGuid(), Uuid = "UUID-V", Total = 5_400m, Moneda = "MXN",
            Subtotal = 5_000m, Iva = 400m, SucursalId = Sucursal, ClienteId = Cliente,
            Lineas = new object[] { new { TipoProducto = "VIDRIO", Importe = 5_000m, Descuento = 0m } },
            Impuestos = new object[] { new { Tratamiento = "TASA8", Base = 5_000m, Iva = 400m } },
        });

        Partidas(IvaPendiente("IVA_TRASLADADO_NO_COBRADO", LadoPartida.Abono), e).Should().Equal(new (string?, decimal)[] { ("TASA8", 400m) });
        e.Lineas.Where(l => l.Grupo == null).Should().ContainSingle(l => l.Clasificador == "VIDRIO" && l.Importe == 5_000m);
    }

    [Fact]
    public void CA5_un_evento_sin_impuestos_se_trata_como_hoy()
    {
        var e = FacturaProveedor(conDetalle: false);

        e.Lineas.Should().NotContain(l => l.Grupo == LineaEventoContable.GrupoIva);
        Partidas(IvaPendiente(), e).Should().Equal(new[] {((string?)null, 1_600m)}, "sin líneas del grupo, la plantilla usa el IVA total en el rol base");
    }

    [Fact]
    public void Un_detalle_que_no_cuadra_con_el_iva_del_documento_se_ignora_y_el_centavo_va_al_mayor()
    {
        ImpuestosTrasladados.LineasDocumento([("TASA16", 10_000m, 1_500m)], 1_600m).Should().BeEmpty();
        ImpuestosTrasladados.LineasDocumento([("TASA16", 10_000m, 1_599.99m), ("TASA8", 100m, 8m)], 1_608m)
            .Select(l => (l.Clasificador, l.Importe)).Should().Equal(new (string?, decimal)[] { ("TASA16", 1_600m), ("TASA8", 8m) });
    }

    [Theory]
    [InlineData("tasa16", "TASA16")]
    [InlineData(" Tasa_8 ", "TASA8")]
    [InlineData("EXENTO", "EXENTO")]
    [InlineData("IEPS", null)]
    public void Normaliza_el_tratamiento(string entrada, string? esperado) =>
        ImpuestosTrasladados.Normalizar(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("01", "Tasa", 0.16, "NOOBJETO")]
    [InlineData("02", "Exento", null, "EXENTO")]
    [InlineData("02", "Tasa", 0.16, "TASA16")]
    [InlineData("02", "Tasa", 0.08, "TASA8")]
    [InlineData("02", "Tasa", 0.0, "TASA0")]
    [InlineData("02", "Tasa", 0.11, null)]
    public void Tratamiento_de_un_traslado_del_cfdi(string objetoImp, string tipoFactor, double? tasa, string? esperado) =>
        ImpuestosTrasladados.DeTraslado(objetoImp, tipoFactor, (decimal?)tasa).Should().Be(esperado);

    // ── Saldo de IVA por documento ──────────────────────────────────────────

    private static SaldoIvaDocumento Saldo() =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), null, "UUID-FIX", "PPD", Sucursal, Proveedor,
            new DateOnly(2026, 11, 1), "MXN", null, 14_100m, 1_600m, 0m, Guid.NewGuid(),
            tratamientos: [("TASA16", 10_000m, 1_600m), ("EXENTO", 2_500m, 0m)]);

    [Fact]
    public void CA1_el_saldo_guarda_la_base_al_16_y_la_exenta()
    {
        Saldo().Tratamientos.Select(t => (t.Tratamiento, t.Base, t.Iva)).Should().Equal(new[] {("TASA16", 10_000m, 1_600m), ("EXENTO", 2_500m, 0m)});
    }

    [Fact]
    public void Un_pago_parcial_reparte_iva_y_bases_por_tratamiento_y_el_que_salda_toma_el_remanente()
    {
        var s = Saldo();
        var (iva, _) = s.Calcular(7_050m);
        iva.Should().Be(800m);
        var mitad = s.CalcularTratamientos(7_050m, iva);
        mitad.Select(t => (t.Tratamiento, t.Base, t.Iva)).Should().Equal(new[] {("TASA16", 5_000m, 800m), ("EXENTO", 1_250m, 0m)});
        var r = s.Aplicar(("tesoreria", "pago"), Guid.NewGuid(), new DateOnly(2026, 11, 9), 7_050m, iva, 0m, Guid.NewGuid());
        r.Tratamientos.Should().HaveCount(2);

        var (resto, _) = s.Calcular(7_050m);
        s.CalcularTratamientos(7_050m, resto).Select(t => (t.Tratamiento, t.Base, t.Iva))
            .Should().Equal(new[] {("TASA16", 5_000m, 800m), ("EXENTO", 1_250m, 0m)});
    }

    [Fact]
    public void Revertir_un_pago_devuelve_lo_aplicado_por_tratamiento()
    {
        var s = Saldo();
        var r = s.Aplicar(("tesoreria", "pago"), Guid.NewGuid(), new DateOnly(2026, 11, 9), 7_050m, 800m, 0m, Guid.NewGuid());
        s.Revertir(r, Ocurrido);
        s.Tratamientos.Should().OnlyContain(t => t.BaseAplicada == 0m && t.IvaReclasificado == 0m);
    }

    [Fact]
    public void Una_nota_reduce_cada_tratamiento_en_proporcion()
    {
        var s = Saldo();
        var n = s.Reducir(("cuentas_por_pagar", "nota_credito"), Guid.NewGuid(), new DateOnly(2026, 11, 9), 1_410m, 160m, 0m, Guid.NewGuid());

        s.Tratamientos.Select(t => (t.Tratamiento, t.Base, t.Iva)).Should().Equal(new[] {("TASA16", 9_000m, 1_440m), ("EXENTO", 2_250m, 0m)});
        n.Tratamientos.Sum(t => t.Iva).Should().Be(160m);
    }

    [Fact]
    public void Sin_detalle_el_saldo_va_todo_al_16_con_la_base_gravada()
    {
        ImpuestosTrasladados.ParaSaldo([], 10_000m, 1_600m).Should().Equal(new[] {("TASA16", 10_000m, 1_600m)});
        ImpuestosTrasladados.ParaSaldo([], null, 0m).Should().BeEmpty();
    }

    [Fact]
    public void La_reclasificacion_de_un_saldo_anterior_sin_tratamientos_va_al_16()
    {
        ControlIvaDocumentos.PorTratamiento([], 160m).Should().Equal(new[] {("TASA16", 160m)});
        ControlIvaDocumentos.PorTratamiento([new TratamientoAplicadoIva { Tratamiento = "TASA8", Iva = 40m }], 40m)
            .Should().Equal(new[] {("TASA8", 40m)});
    }

    [Fact]
    public void CA3_la_liquidacion_suma_bases_e_iva_por_lado_y_tratamiento()
    {
        var s = Saldo();
        var pago = s.Aplicar(("tesoreria", "pago"), Guid.NewGuid(), new DateOnly(2026, 11, 9), 14_100m, 1_600m, 0m, Guid.NewGuid());

        LiquidacionIvaHandlers.PorTratamiento([pago], new Dictionary<Guid, SaldoIvaDocumento> { [s.Id] = s })
            .Should().Equal(new[] {
                new BaseTratamientoIva(LadoIva.Acreditable, "EXENTO", 2_500m, 0m),
                new BaseTratamientoIva(LadoIva.Acreditable, "TASA16", 10_000m, 1_600m),
            });
    }
}
