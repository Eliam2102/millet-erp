using Millet.Contabilidad.Application.Reportes;
using Millet.SharedKernel.Application.Exceptions;
using H = Millet.Contabilidad.Application.Reportes.DiferenciasCambiariasHandler;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// TES-09 (86e3jvvj4) · reporte de diferencias cambiarias realizadas del periodo. Pólizas como las deja CON-11 (#500): el proveedor o
/// cliente al TC de la factura, bancos al TC del pago y la diferencia en su cuenta por origen. Datos FIX de CA10.7 y CON-11-a.
/// </summary>
public class DiferenciasCambiariasReporteTests
{
    private static readonly Guid Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid(), Cliente = Guid.NewGuid();
    private static readonly Guid CtaProveedores = Guid.NewGuid(), CtaClientes = Guid.NewGuid(), CtaBancos = Guid.NewGuid(), CtaIva = Guid.NewGuid();
    private static readonly Guid PerdidaProv = Guid.NewGuid(), UtilidadProv = Guid.NewGuid(), PerdidaCli = Guid.NewGuid(), UtilidadCli = Guid.NewGuid();

    private static readonly Dictionary<Guid, (OrigenDiferenciaCambiaria, bool)> Cuentas = new()
    {
        [PerdidaProv] = (OrigenDiferenciaCambiaria.Proveedores, true),
        [UtilidadProv] = (OrigenDiferenciaCambiaria.Proveedores, false),
        [PerdidaCli] = (OrigenDiferenciaCambiaria.Clientes, true),
        [UtilidadCli] = (OrigenDiferenciaCambiaria.Clientes, false),
    };

    private static H.PartidaDiferencia P(Guid poliza, int linea, Guid cuenta, decimal cargo, decimal abono, string moneda = "USD",
        decimal tc = 1m, decimal? original = null, Guid? cliente = null, Guid? proveedor = null) =>
        new(poliza, linea, cuenta, cargo, abono, moneda, tc, original, Sucursal, cliente, proveedor);

    /// <summary>CA10.7: factura 1,000 USD a 18.00 pagada a 18.50.</summary>
    private static (H.PolizaDiferencia, H.PartidaDiferencia[]) PagoCa107(DateOnly fecha, bool anulacion = false)
    {
        var id = Guid.NewGuid();
        var s = anulacion ? -1 : 1;
        return (new(id, "EG-000001", fecha, "PAGO-1", anulacion),
        [
            P(id, 1, CtaProveedores, s * 18_000m, 0m, tc: 18.00m, original: s * 1_000m, proveedor: Proveedor),
            P(id, 2, CtaBancos, 0m, s * 18_500m, tc: 18.50m, original: s * 1_000m),
            P(id, 3, PerdidaProv, s * 500m, 0m, "MXN"),
        ]);
    }

    [Fact]
    public void CA10_7_pago_a_proveedor_sale_con_tc_de_factura_tc_de_pago_y_perdida()
    {
        var (poliza, partidas) = PagoCa107(new DateOnly(2026, 11, 6));
        var fila = H.Armar([poliza], partidas, Cuentas).Should().ContainSingle().Subject;

        fila.Origen.Should().Be(OrigenDiferenciaCambiaria.Proveedores);
        fila.TerceroId.Should().Be(Proveedor);
        fila.Moneda.Should().Be("USD");
        fila.ImporteOriginal.Should().Be(1_000m);
        fila.TipoCambioDocumento.Should().Be(18.00m);
        fila.TipoCambioPago.Should().Be(18.50m);
        fila.Perdida.Should().Be(500m);
        fila.Utilidad.Should().Be(0m);
        fila.Neto.Should().Be(-500m);
        fila.DocumentoFolio.Should().Be("PAGO-1");
    }

    [Fact]
    public void CON_11_a_cobro_de_cliente_con_iva_toma_el_tc_de_bancos_no_el_del_iva()
    {
        // Cobro 1,000 USD de factura a 18.00 cobrada a 17.50; el IVA se reclasifica al TC de la factura (sin tercero).
        var id = Guid.NewGuid();
        var partidas = new[]
        {
            P(id, 1, CtaBancos, 17_500m, 0m, tc: 17.50m, original: 1_000m),
            P(id, 2, CtaClientes, 0m, 18_000m, tc: 18.00m, original: 1_000m, cliente: Cliente),
            P(id, 3, CtaIva, 2_482.76m, 0m, tc: 18.00m, original: 137.93m),
            P(id, 4, CtaIva, 0m, 2_482.76m, tc: 18.00m, original: 137.93m),
            P(id, 5, PerdidaCli, 500m, 0m, "MXN"),
        };
        var fila = H.Armar([new(id, "IG-000007", new DateOnly(2026, 11, 6), null, false)], partidas, Cuentas).Single();

        fila.Origen.Should().Be(OrigenDiferenciaCambiaria.Clientes);
        fila.TerceroId.Should().Be(Cliente);
        fila.TipoCambioDocumento.Should().Be(18.00m);
        fila.TipoCambioPago.Should().Be(17.50m);
        fila.Perdida.Should().Be(500m);
    }

    [Fact]
    public void Utilidad_cambiaria_sale_en_utilidad_con_neto_positivo()
    {
        var id = Guid.NewGuid();
        var partidas = new[]
        {
            P(id, 1, CtaProveedores, 18_000m, 0m, tc: 18.00m, original: 1_000m, proveedor: Proveedor),
            P(id, 2, CtaBancos, 0m, 17_800m, tc: 17.80m, original: 1_000m),
            P(id, 3, UtilidadProv, 0m, 200m, "MXN"),
        };
        var fila = H.Armar([new(id, "EG-000002", new DateOnly(2026, 11, 6), null, false)], partidas, Cuentas).Single();
        (fila.Perdida, fila.Utilidad, fila.Neto).Should().Be((0m, 200m, 200m));
    }

    [Fact]
    public void Una_anulacion_sale_en_negativo_y_netea_con_su_original()
    {
        var (original, p1) = PagoCa107(new DateOnly(2026, 11, 6));
        var (anulacion, p2) = PagoCa107(new DateOnly(2026, 11, 7), anulacion: true);
        var filas = H.Armar([original, anulacion], [.. p1, .. p2], Cuentas);

        filas.Should().HaveCount(2);
        filas[1].EsAnulacion.Should().BeTrue();
        filas[1].Perdida.Should().Be(-500m);
        filas[1].ImporteOriginal.Should().Be(-1_000m);
        H.Totales(filas).Perdida.Should().Be(0m);
    }

    [Fact]
    public void Polizas_sin_partidas_cambiarias_no_salen()
    {
        var id = Guid.NewGuid();
        var partidas = new[]
        {
            P(id, 1, CtaProveedores, 5_000m, 0m, "MXN", proveedor: Proveedor),
            P(id, 2, CtaBancos, 0m, 5_000m, "MXN"),
        };
        H.Armar([new(id, "EG-000003", new DateOnly(2026, 11, 6), null, false)], partidas, Cuentas).Should().BeEmpty();
    }

    [Fact]
    public void Los_totales_separan_proveedores_y_clientes()
    {
        var (pago, p1) = PagoCa107(new DateOnly(2026, 11, 6));
        var cobro = Guid.NewGuid();
        var p2 = new[]
        {
            P(cobro, 1, CtaBancos, 18_300m, 0m, tc: 18.30m, original: 1_000m),
            P(cobro, 2, CtaClientes, 0m, 18_000m, tc: 18.00m, original: 1_000m, cliente: Cliente),
            P(cobro, 3, UtilidadCli, 0m, 300m, "MXN"),
        };
        var filas = H.Armar([pago, new(cobro, "IG-000008", new DateOnly(2026, 11, 8), null, false)], [.. p1, .. p2], Cuentas);
        var t = H.Totales(filas);

        t.Should().Be(new TotalesDiferenciasCambiarias(Perdida: 500m, Utilidad: 300m, Neto: -200m, PerdidaProveedores: 500m,
            UtilidadProveedores: 0m, PerdidaClientes: 0m, UtilidadClientes: 300m, Movimientos: 2));
        filas.Select(f => f.Fecha).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Los_roles_del_reporte_son_los_de_con_11_por_origen()
    {
        H.Roles.Keys.Should().BeEquivalentTo(
            "PERDIDA_CAMBIARIA_REALIZADA_PROVEEDORES", "UTILIDAD_CAMBIARIA_REALIZADA_PROVEEDORES",
            "PERDIDA_CAMBIARIA_REALIZADA_CLIENTES", "UTILIDAD_CAMBIARIA_REALIZADA_CLIENTES");
    }

    [Fact]
    public void Los_filtros_se_leen_del_texto_del_query()
    {
        var (desde, hasta, sucursal, origen) = FiltrosDiferenciasCambiarias.Leer("2026-11-01", "2026-11-07", Sucursal.ToString(), "clientes");
        (desde, hasta, sucursal, origen).Should().Be((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), (Guid?)Sucursal,
            (OrigenDiferenciaCambiaria?)OrigenDiferenciaCambiaria.Clientes));
        FiltrosDiferenciasCambiarias.Leer(null, "2026-11-07", "", null).Desde.Should().Be(new DateOnly(2026, 11, 1), "sin desde: día 1 del mes de hasta");
    }

    [Theory]
    [InlineData("2026-13-01", null, null, null, "desde", "FECHA_INVALIDA")]
    [InlineData(null, "07/11/2026", null, null, "hasta", "FECHA_INVALIDA")]
    [InlineData(null, null, "00000000-0000-0000-0000-000000000000", null, "sucursalId", "SUCURSAL_INVALIDA")]
    [InlineData(null, null, "MID", null, "sucursalId", "SUCURSAL_INVALIDA")]
    [InlineData(null, null, null, "Bancos", "origen", "ORIGEN_INVALIDO")]
    [InlineData(null, null, null, "7", "origen", "ORIGEN_INVALIDO")]
    public void Un_filtro_mal_formado_responde_400_en_espanol(string? desde, string? hasta, string? sucursal, string? origen,
        string campo, string codigo)
    {
        var error = FluentActions.Invoking(() => FiltrosDiferenciasCambiarias.Leer(desde, hasta, sucursal, origen))
            .Should().Throw<Millet.SharedKernel.Application.Exceptions.ValidationException>().Which.Errors.Should().ContainSingle().Subject;
        (error.Field, error.Code).Should().Be((campo, codigo));
        error.Message.Should().NotContain("must").And.NotContain("Failed");
    }

    [Fact]
    public void El_filtro_de_origen_se_muestra_en_plural_como_la_opcion()
    {
        H.EtiquetaFiltro(OrigenDiferenciaCambiaria.Clientes).Should().Be("Cobros de clientes");
        H.EtiquetaFiltro(OrigenDiferenciaCambiaria.Proveedores).Should().Be("Pagos a proveedores");
    }

    [Fact]
    public void Los_mensajes_del_validador_estan_en_espanol()
    {
        var v = new DiferenciasCambiariasValidator();
        var r = v.Validate(new DiferenciasCambiariasQuery(new DateOnly(1990, 1, 1), new DateOnly(1990, 1, 2), Guid.Empty));
        r.Errors.Select(e => e.ErrorMessage).Should().OnlyContain(m => !m.Contains("must"))
            .And.Contain(m => m.Contains("sucursal")).And.Contain(m => m.Contains("fecha inicial"));
        v.Validate(new DiferenciasCambiariasQuery(new DateOnly(2025, 1, 1), new DateOnly(2026, 11, 7))).Errors
            .Should().ContainSingle(e => e.ErrorMessage == "El periodo no puede pasar de un año.");
    }
}
