using System.Text.Json;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// U-2 (86e3yxngq) · lectura de los eventos que Contabilidad no recibía. Payload serializado como el outbox (enums como número).
/// Datos ficticios.
/// </summary>
public class NormalizadorEventosU2Tests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);

    private static Millet.Contabilidad.Application.Motor.EventoContable Leer(string tipo, object payload) =>
        N.Normalizar(tipo, Guid.NewGuid(), JsonSerializer.Serialize(payload));

    [Fact]
    public void El_deposito_del_corte_se_contabiliza_por_ficha_con_su_fecha_y_sucursal()
    {
        var deposito = Guid.NewGuid();
        var e = Leer(N.DepositoCajaConfirmado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, DepositoId = deposito, FichaId = Guid.NewGuid(), CajaSesionId = Guid.NewGuid(),
            DiaCorte = new DateOnly(2026, 11, 4), SucursalId = Sucursal, Importe = 700m, Moneda = "MXN", FechaFicha = new DateOnly(2026, 11, 5),
            NumeroFicha = "F-001", ImporteCorte = 700m, SaldoPorDepositar = 0m, CorteCompleto = true,
        });

        e.FechaContable.Should().Be(new DateOnly(2026, 11, 5));
        e.SucursalId.Should().Be(Sucursal);
        e.Importes["Total"].Should().Be(700m);
        e.Documento.Should().Match<Millet.Contabilidad.Domain.DocumentoOrigenPoliza>(d =>
            d.Modulo == "tesoreria" && d.Tipo == "deposito_caja" && d.Id == deposito && d.Folio == "F-001");
    }

    [Theory]
    [InlineData("Faltante", 50)]
    [InlineData("Sobrante", -50)]
    public void La_diferencia_de_corte_lleva_signo_faltante_positivo_y_sobrante_negativo(string tipo, decimal neto)
    {
        var e = Leer(N.DiferenciaCorteAutorizada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, DiferenciaId = Guid.NewGuid(), CajaSesionId = Guid.NewGuid(),
            DiaCorte = new DateOnly(2026, 11, 4), SucursalId = Sucursal, CajeroId = Guid.NewGuid(), Tipo = tipo, Importe = 50m, Moneda = "MXN",
        });

        e.Importes["Neto"].Should().Be(neto);
        e.FechaContable.Should().Be(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void La_comision_con_IVA_es_un_egreso_con_base_IVA_y_total_y_su_concepto_como_clasificador()
    {
        var e = Leer(N.MovimientoBancarioRegistrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(),
            Sentido = 2, Monto = 100m, Moneda = "MXN", FechaValor = new DateOnly(2026, 11, 3), ReferenciaBancaria = "COM-1",
            ConceptoId = Guid.NewGuid(), ConceptoNombre = "Comisión bancaria", Iva = 16m, SucursalId = Sucursal,
        });

        e.Importes["Base"].Should().Be(100m);
        e.Importes["Iva"].Should().Be(16m);
        e.Importes["Total"].Should().Be(116m);
        e.Lineas.Should().ContainSingle().Which.Clasificador.Should().Be("Comisión bancaria");
        e.SucursalId.Should().Be(Sucursal);
    }

    [Fact]
    public void Un_interes_ganado_es_ingreso_y_sus_importes_van_en_negativo_para_invertir_los_lados()
    {
        var e = Leer(N.MovimientoBancarioRegistrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(),
            Sentido = 1, Monto = 40m, Moneda = "MXN", FechaValor = new DateOnly(2026, 11, 3), ConceptoNombre = "Intereses ganados", SucursalId = Sucursal,
        });

        e.Importes["Total"].Should().Be(-40m);
        e.Lineas.Single().Importe.Should().Be(-40m);
    }

    [Fact]
    public void El_movimiento_de_IVA_de_una_comision_no_genera_otra_poliza()
    {
        var e = Leer(N.MovimientoBancarioRegistrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(),
            Sentido = 2, Monto = 16m, Moneda = "MXN", FechaValor = new DateOnly(2026, 11, 3), ConceptoNombre = "Impuestos", SucursalId = Sucursal,
            MovimientoBaseId = Guid.NewGuid(),
        });

        N.SinMovimiento(e).Should().BeTrue();
    }

    [Theory]
    [InlineData("Autorizada", true)]
    [InlineData("Rechazada", true)]
    [InlineData("Pagada", false)]
    public void Solo_el_reembolso_pagado_mueve_dinero(string estado, bool sinMovimiento)
    {
        var nc = Guid.NewGuid();
        var e = Leer(N.ReembolsoNotaCreditoActualizado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, DevolucionId = Guid.NewGuid(), DevolucionFolio = "DEV-1", NotaCreditoId = nc,
            Estado = estado, Importe = 300m, Moneda = "MXN", FechaPago = new DateOnly(2026, 11, 5),
        });

        N.SinMovimiento(e).Should().Be(sinMovimiento);
        e.FacturaOrigenId.Should().Be(nc);
    }

    [Fact]
    public void El_costo_de_importacion_separa_inventario_por_articulo_gasto_y_total()
    {
        Guid a1 = Guid.NewGuid(), a2 = Guid.NewGuid();
        var e = Leer(N.ExpedienteCostoAplicado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, Empresa, Fecha = Ocurrido, ExpedienteId = Guid.NewGuid(), NumeroVersion = 1, SucursalId = Sucursal,
            Partidas = new[]
            {
                new { PartidaId = Guid.NewGuid(), ArticuloId = a1, DiferenciaMxn = 6_000m, InventarioMxn = 4_800m, GastoImportacionMxn = 1_200m },
                new { PartidaId = Guid.NewGuid(), ArticuloId = a2, DiferenciaMxn = 4_000m, InventarioMxn = 3_200m, GastoImportacionMxn = 800m },
            },
        });

        e.Importes["Inventario"].Should().Be(8_000m);
        e.Importes["Gasto"].Should().Be(2_000m);
        e.Importes["Total"].Should().Be(10_000m);
        e.Lineas.Select(l => (l.ArticuloId, l.Importe)).Should().Equal((a1, 4_800m), (a2, 3_200m));
        e.Documento.Folio.Should().Be("v1");
    }

    [Theory]
    [InlineData("consumo", "Consumo")]
    [InlineData("consumo_composicion", "Consumo")]
    [InlineData("rotura", "Merma")]
    [InlineData("daño", "Merma")]
    [InlineData("salida_manual", "Ajuste")]
    [InlineData("salida_no_planificada", "Ajuste")]
    public void La_salida_sin_requisicion_carga_segun_su_causa(string causa, string campo)
    {
        var e = Leer(N.SalidaInventarioRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, MovimientoId = Guid.NewGuid(), FolioMovimiento = "SAL-1", FechaMovimiento = new DateOnly(2026, 11, 5),
            Causa = causa, SucursalId = Sucursal, MontoTotalMxn = 250m,
            Lineas = new[] { new { LineaId = Guid.NewGuid(), ArticuloId = Guid.NewGuid(), UnidadMedida = "PZA", Cantidad = 5m, CostoUnitarioMxn = 50m, MontoMxn = 250m } },
        });

        foreach (var c in new[] { "Consumo", "Merma", "Ajuste" })
            e.Importes[c].Should().Be(c == campo ? 250m : 0m);
        e.Importes["Subtotal"].Should().Be(250m);
    }
}
