using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// 86e3zcbjw · retenciones por concepto y retención de proveedor al pago (Contabilidad, 6-nov): la factura de proveedor se registra por
/// el total; cada pago carga al proveedor el pago más su retención, abona bancos por el pago y la retención a la cuenta de su concepto.
/// Las retenciones que nos hacen los clientes van a la cuenta de su concepto con la factura de venta. Datos FIX.
/// </summary>
public class RetencionesPorConceptoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid(), Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);
    private static readonly (string, string) Pago = ("tesoreria", "pago_factura_proveedor");

    private static EventoContable Normalizar(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), JsonSerializer.Serialize(payload));

    // Flete FIX: 3,000 + IVA 480 − retención de IVA 4 % sobre 2,000 (solo dos líneas) = 80 → CFDI 3,400.
    private static EventoContable FleteProveedor(string? concepto = "FLETES") => Normalizar(N.FacturaProveedorRegistrada, new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaProveedorId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid(), TotalFactura = 3_400m,
        Lineas = new object[]
        {
            new { LineaOcId = Guid.NewGuid(), Importe = 1_500m, EsServicio = true },
            new { LineaOcId = Guid.NewGuid(), Importe = 1_000m, EsServicio = true },
            new { LineaOcId = Guid.NewGuid(), Importe = 500m, EsServicio = true },
        },
        ProveedorId = Proveedor, Uuid = "UUID-FIX", Subtotal = 3_000m, Iva = 480m, RetencionesTotal = 80m, Moneda = "MXN", SucursalId = Sucursal,
        Retenciones = new object[] { new { Impuesto = "002", Tasa = 0.04m, Importe = 80m, Base = 2_000m } },
        ConceptoRetencion = concepto,
    });

    private static SaldoIvaDocumento SaldoFlete(bool alPago = true) =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), null, "UUID-FIX", "PPD", Sucursal, Proveedor,
            new DateOnly(2026, 11, 1), "MXN", null, 3_400m, 480m, 80m, Guid.NewGuid(), [("FLETES_IVA", 80m)], alPago);

    /// <summary>Partidas que arma una plantilla con el evento, con la misma selección de líneas que el motor.</summary>
    private static List<(string Rol, string? Clasificador, decimal Cargo, decimal Abono)> Partidas(EventoContable e,
        params LineaPlantillaContable[] plantilla) =>
        [.. plantilla.SelectMany(l => GenerarPolizaDesdeEventoHandler.Importes(l, e)
            .Where(x => x.Importe != 0m)
            .Select(x => (l.Rol, l.RolPorClasificador ? x.Linea?.Clasificador : null,
                l.Lado == LadoPartida.Cargo ? x.Importe : 0m, l.Lado == LadoPartida.Abono ? x.Importe : 0m)))];

    private static LineaPlantillaContable L(string rol, LadoPartida lado, string campo, bool porLinea = false, string? grupo = null,
        bool porClasificador = false) =>
        new() { Rol = rol, Lado = lado, Campo = campo, PorLinea = porLinea, Grupo = grupo, RolPorClasificador = porClasificador };

    [Fact]
    public void La_factura_de_proveedor_se_registra_por_el_total_y_trae_su_retencion_por_concepto()
    {
        var e = FleteProveedor();

        e.Importes[RetencionesContables.CampoTotalMas].Should().Be(3_480m, "3,400 del CFDI + 80 de retención");
        e.Lineas.Where(l => l.Grupo == LineaEventoContable.GrupoRetencion).Should().ContainSingle()
            .Which.Should().Match<LineaEventoContable>(l => l.Clasificador == "FLETES_IVA" && l.Importe == 80m);

        // #2 v3: gasto por línea + IVA pendiente contra proveedor por el total; sin retenciones por pagar.
        var partidas = Partidas(e,
            L("GASTO", LadoPartida.Cargo, "Subtotal", porLinea: true),
            L("IVA_ACREDITABLE_PENDIENTE", LadoPartida.Cargo, "Iva"),
            L("PROVEEDORES", LadoPartida.Abono, RetencionesContables.CampoTotalMas));
        partidas.Where(p => p.Rol == "GASTO").Sum(p => p.Cargo).Should().Be(3_000m, "las líneas de retención no son gasto");
        (partidas.Sum(p => p.Cargo), partidas.Sum(p => p.Abono)).Should().Be((3_480m, 3_480m));
    }

    [Fact]
    public void El_pago_total_carga_al_proveedor_el_pago_mas_la_retencion_y_la_abona_a_la_cuenta_del_concepto()
    {
        var saldo = SaldoFlete();
        var retenciones = saldo.CalcularRetenciones(3_400m);
        retenciones.Should().ContainSingle().Which.Should().Match<RetencionAplicadaIva>(r => r.Clasificador == "FLETES_IVA" && r.Importe == 80m);

        var pago = Pagado(3_400m, retenciones);
        var partidas = Partidas(pago,
            L("PROVEEDORES", LadoPartida.Cargo, RetencionesContables.CampoTotalMas),
            L("BANCOS", LadoPartida.Abono, "Total"),
            L(RetencionesContables.RolPorPagar, LadoPartida.Abono, RetencionesContables.CampoAplicada, porLinea: true,
                grupo: LineaEventoContable.GrupoRetencion, porClasificador: true));

        partidas.Should().BeEquivalentTo(new[]
        {
            ("PROVEEDORES", (string?)null, 3_480m, 0m),
            ("BANCOS", (string?)null, 0m, 3_400m),
            (RetencionesContables.RolPorPagar, (string?)"FLETES_IVA", 0m, 80m),
        });
    }

    [Fact]
    public void Dos_pagos_parciales_reparten_la_retencion_y_el_ultimo_toma_el_remanente()
    {
        var saldo = SaldoFlete();
        var importes = new List<decimal>();
        foreach (var importe in new[] { 1_133.33m, 2_266.67m })
        {
            var r = saldo.CalcularRetenciones(importe);
            var (iva, ret) = saldo.Calcular(importe);
            saldo.Aplicar(Pago, Guid.NewGuid(), new DateOnly(2026, 11, 2), importe, iva, ret, Guid.NewGuid(), retenciones: r);
            importes.Add(r.Sum(x => x.Importe));
        }

        importes.Should().Equal(26.67m, 53.33m);
        saldo.Retenciones.Single().Should().Match<RetencionSaldoIva>(r => r.Aplicado == 80m && r.Pendiente == 0m);
    }

    [Fact]
    public void La_reversa_del_pago_regresa_la_retencion_a_pendiente()
    {
        var saldo = SaldoFlete();
        var r = saldo.Aplicar(Pago, Guid.NewGuid(), new DateOnly(2026, 11, 2), 1_700m, 240m, 40m, Guid.NewGuid(),
            retenciones: saldo.CalcularRetenciones(1_700m));
        r.Retenciones.Should().ContainSingle().Which.Importe.Should().Be(40m);

        saldo.Revertir(r, DateTimeOffset.UtcNow);

        saldo.Retenciones.Single().Aplicado.Should().Be(0m);
        saldo.CalcularRetenciones(3_400m).Single().Importe.Should().Be(80m);
    }

    [Fact]
    public void La_nota_de_credito_con_retencion_reduce_la_retencion_pendiente_del_mismo_impuesto()
    {
        var saldo = SaldoFlete();
        saldo.Reducir(("cuentas_por_pagar", "nota_credito_proveedor"), Guid.NewGuid(), new DateOnly(2026, 11, 3), 1_700m, 240m, 40m,
            Guid.NewGuid(), [("IVA", 40m), ("ISR", 10m)]);

        saldo.Retenciones.Single().Should().Match<RetencionSaldoIva>(r => r.Importe == 40m && r.Pendiente == 40m,
            "la nota solo reduce la retención de IVA; la de ISR no existe en el documento");
        saldo.CalcularRetenciones(1_700m).Single().Importe.Should().Be(40m, "el pago del resto salda la retención que queda");
    }

    [Fact]
    public void La_factura_registrada_con_la_plantilla_anterior_no_descuenta_la_retencion_en_el_pago()
    {
        var saldo = SaldoFlete(alPago: false);
        var pago = ControlIvaDocumentos.ConCampoIva(PagoEvento(3_400m));

        saldo.RetencionAlPago.Should().BeFalse();
        (pago.Importes[RetencionesContables.CampoAplicada], pago.Importes[RetencionesContables.CampoTotalMas]).Should().Be((0m, 3_400m),
            "su póliza ya abonó la retención; el proveedor quedó por el neto");
        pago.Lineas.Should().BeEmpty();
    }

    [Fact]
    public void Sin_concepto_la_retencion_usa_solo_el_impuesto_y_cae_al_rol_base()
    {
        FleteProveedor(concepto: null).Lineas.Single(l => l.Grupo == LineaEventoContable.GrupoRetencion).Clasificador.Should().Be("IVA");

        var roles = new List<RolCuenta>
        {
            RolCuenta.Crear(Empresa, RetencionesContables.RolPorPagar, "Base", Guid.Parse("00000000-0000-0000-0000-0000000000b1"), null, null, new(2000, 1, 1)),
            RolCuenta.Crear(Empresa, "RETENCIONES_POR_PAGAR_FLETES_IVA", "Fletes", Guid.Parse("00000000-0000-0000-0000-0000000000f1"), null, null, new(2000, 1, 1)),
            RolCuenta.Crear(Empresa, "RETENCIONES_POR_PAGAR_SERVICIOS_IVA", "Sin cuenta", null, null, null, new(2000, 1, 1)),
        };
        Guid? Cuenta(string? c) => GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(roles, RetencionesContables.RolPorPagar, c, Sucursal,
            "MXN", new DateOnly(2026, 11, 6));

        Cuenta("FLETES_IVA").Should().Be(Guid.Parse("00000000-0000-0000-0000-0000000000f1"));
        Cuenta("SERVICIOS_IVA").Should().Be(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "sin cuenta en el concepto, el rol base");
        Cuenta("IVA").Should().Be(Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
    }

    [Fact]
    public void La_venta_lleva_cada_retencion_del_cliente_a_su_concepto_y_las_ventas_no_la_suman()
    {
        // Flete 1,000 (ret. 4 % = 40) y desperdicio 500 (ret. 16 % = 80): 1,500 + IVA 240 − 120 = 1,620.
        var e = Normalizar(N.FacturaVentaTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaVentaId = Guid.NewGuid(), Uuid = "UUID-FIX", Total = 1_620m, Moneda = "MXN",
            Folio = "FV-FIX-3", Subtotal = 1_500m, Descuento = 0m, Iva = 240m, RetencionesTotal = 120m, SucursalId = Sucursal, ClienteId = Cliente,
            Lineas = new object[]
            {
                new { TipoProducto = "Servicio", Importe = 1_000m, Descuento = 0m },
                new { TipoProducto = "Desperdicio", Importe = 500m, Descuento = 0m },
            },
            Retenciones = new object[]
            {
                new { Impuesto = "002", Importe = 40m, Concepto = "FLETES" },
                new { Impuesto = "002", Importe = 80m, Concepto = "DESPERDICIO" },
            },
        });

        // #7 v3: la línea de retenciones por concepto (grupo retención, rol por clasificador).
        var partidas = Partidas(e,
            L("CLIENTES", LadoPartida.Cargo, "TotalMenosRetenciones"),
            L(RetencionesContables.RolClientes, LadoPartida.Cargo, "RetencionesTotal", porLinea: true, grupo: LineaEventoContable.GrupoRetencion,
                porClasificador: true),
            L("VENTAS", LadoPartida.Abono, "Subtotal", porLinea: true, porClasificador: true),
            L("IVA_TRASLADADO_NO_COBRADO", LadoPartida.Abono, "Iva"));

        partidas.Where(p => p.Rol == RetencionesContables.RolClientes).Select(p => (p.Clasificador, p.Cargo))
            .Should().BeEquivalentTo(new[] { ((string?)"FLETES_IVA", 40m), ("DESPERDICIO_IVA", 80m) });
        partidas.Where(p => p.Rol == "VENTAS").Sum(p => p.Abono).Should().Be(1_500m);
        (partidas.Sum(p => p.Cargo), partidas.Sum(p => p.Abono)).Should().Be((1_740m, 1_740m));
    }

    [Fact]
    public void Un_evento_anterior_sin_desglose_usa_el_total_de_retenciones_en_el_rol_base()
    {
        var e = Normalizar(N.FacturaVentaTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaVentaId = Guid.NewGuid(), Total = 1_120m, Moneda = "MXN", Subtotal = 1_000m,
            Descuento = 0m, Iva = 160m, RetencionesTotal = 40m, SucursalId = Sucursal, ClienteId = Cliente,
            Lineas = new object[] { new { TipoProducto = "Servicio", Importe = 1_000m, Descuento = 0m } },
        });

        Partidas(e, L(RetencionesContables.RolClientes, LadoPartida.Cargo, "RetencionesTotal", porLinea: true,
                grupo: LineaEventoContable.GrupoRetencion, porClasificador: true))
            .Should().ContainSingle().Which.Should().Be((RetencionesContables.RolClientes, (string?)null, 40m, 0m));
    }

    private static EventoContable PagoEvento(decimal monto) => Normalizar(N.PagoProveedorAplicado, new
    {
        EmpresaId = Empresa, FacturaProveedorId = Guid.NewGuid(), PagoId = Guid.NewGuid(), Monto = monto, Moneda = "MXN",
        FechaPago = new DateOnly(2026, 11, 6), ReferenciaBancaria = "REF-FIX",
    });

    /// <summary>El pago como lo deja <see cref="ControlIvaDocumentos.PrepararAsync"/>: importes y una línea por concepto retenido.</summary>
    private static EventoContable Pagado(decimal monto, IReadOnlyList<RetencionAplicadaIva> retenciones)
    {
        var e = PagoEvento(monto);
        var aplicada = retenciones.Sum(r => r.Importe);
        return e with
        {
            Importes = new Dictionary<string, decimal?>(e.Importes)
            {
                [RetencionesContables.CampoAplicada] = aplicada, [RetencionesContables.CampoTotalMas] = monto + aplicada,
            },
            Lineas = RetencionesContables.Lineas(retenciones.Select(r => ((string?)r.Clasificador, r.Importe))),
        };
    }
}
