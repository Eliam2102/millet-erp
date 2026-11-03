using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// C1.5 · lectura de los eventos #1 y #3–#15 (ficha 86e3kdw33 §3): importes con nombre, líneas, documento origen y referencia de puente
/// que consume cada plantilla. Los payloads se serializan como el outbox (PascalCase). Datos FIX.
/// </summary>
public class NormalizadorEventosC15Tests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Cliente = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static EventoContable Normalizar(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), Json(payload));

    [Fact]
    public void Recepcion_EM_excluye_las_lineas_de_activo_fijo_y_referencia_el_puente_como_recepcion()
    {
        // C1.5-h: una línea de activo fijo y una de material → solo el material entra a la póliza EM.
        var oc = Guid.NewGuid();
        var lineaMaterial = Guid.NewGuid();
        var e = Normalizar(N.RecepcionOcRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, RecepcionId = Guid.NewGuid(), FolioRecepcion = "REC-FIX-1", OrdenCompraId = oc,
            FechaMovimiento = new DateOnly(2026, 10, 27), SucursalId = Sucursal,
            Lineas = new object[]
            {
                new { LineaOcId = Guid.NewGuid(), MontoTotalMxn = 25_000m, EsActivoFijo = true },
                new { LineaOcId = lineaMaterial, MontoTotalMxn = 10_000m, EsActivoFijo = false },
            },
        });

        e.Lineas.Should().ContainSingle().Which.Should().Match<LineaEventoContable>(l => l.Importe == 10_000m && l.LineaOcId == lineaMaterial);
        e.Importes["Subtotal"].Should().Be(10_000m);
        e.OrdenCompraId.Should().Be(oc);
        e.TipoPuente.Should().Be(TipoDocumentoPuente.Recepcion);
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "almacen" && d.Tipo == "recepcion_oc" && d.Folio == "REC-FIX-1");
        e.FechaContable.Should().Be(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void Recepcion_con_solo_activos_fijos_no_mueve_dinero()
    {
        var e = Normalizar(N.RecepcionOcRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, RecepcionId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid(),
            FechaMovimiento = new DateOnly(2026, 10, 27), SucursalId = Sucursal,
            Lineas = new object[] { new { LineaOcId = Guid.NewGuid(), MontoTotalMxn = 25_000m, EsActivoFijo = true } },
        });

        N.SinMovimiento(e).Should().BeTrue();
    }

    [Fact]
    public void Un_evento_sin_normalizador_propio_no_cuenta_como_sin_movimiento()
    {
        // C1.5-i: el genérico no trae importes; tiene que llegar al motor para quedar en la bandeja con SinPlantilla.
        var e = Normalizar("cuentas_por_pagar.factura.autorizada.v1", new { EmpresaId = Empresa, OcurridoEn = Ocurrido });

        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void Devolucion_a_proveedor_EM_referencia_el_puente_como_devolucion_con_su_factura_origen()
    {
        var factura = Guid.NewGuid();
        var e = Normalizar(N.DevolucionProveedorRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, DevolucionId = Guid.NewGuid(), FolioMovimiento = "DEV-FIX-1", ProveedorId = Proveedor,
            FacturaProveedorOrigenId = factura, OrdenCompraOrigenId = Guid.NewGuid(), SucursalId = Sucursal,
            Lineas = new object[] { new { LineaOcId = Guid.NewGuid(), MontoTotalMxn = 2_000m } },
        });

        e.TipoPuente.Should().Be(TipoDocumentoPuente.Devolucion);
        e.FacturaOrigenId.Should().Be(factura);
        e.Importes["Subtotal"].Should().Be(2_000m);
    }

    [Fact]
    public void Ajuste_AS_separa_faltante_y_sobrante_y_sin_motivo_no_usa_merma()
    {
        var e = Normalizar(N.AjusteInventarioAplicado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, ConteoId = Guid.NewGuid(), SucursalId = Sucursal,
            MovimientosGenerados = new object[]
            {
                new { Tipo = "AjusteNegativo", MontoMxn = -300m },
                new { Tipo = "AjustePositivo", MontoMxn = 120m },
            },
        });

        e.Importes["Faltante"].Should().Be(300m);
        e.Importes["FaltanteMerma"].Should().Be(0m);
        e.Importes["FaltanteTotal"].Should().Be(300m);
        e.Importes["Sobrante"].Should().Be(120m);
    }

    [Fact]
    public void Salida_SM_lleva_una_linea_por_renglon_con_su_centro_de_costo()
    {
        var centro = Guid.NewGuid();
        var e = Normalizar(N.SalidaRequisicionRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, SalidaId = Guid.NewGuid(), FolioSalida = "SAL-FIX-1",
            FechaMovimiento = new DateOnly(2026, 10, 27), SucursalId = Sucursal,
            Lineas = new object[] { new { LineaSalidaId = Guid.NewGuid(), ArticuloId = Guid.NewGuid(), MontoTotalMxn = 800m, CentroCostoId = centro } },
        });

        e.Lineas.Should().ContainSingle().Which.CentroCostoId.Should().Be(centro);
        e.Importes["Subtotal"].Should().Be(800m);
    }

    [Theory]
    [InlineData(N.NotaCreditoProveedorRegistrada, "nota_credito_proveedor", TipoDocumentoPuente.NotaCredito)]
    [InlineData(N.NotaCargoAutorizada, "nota_cargo", TipoDocumentoPuente.NotaCargo)]
    public void Nota_de_proveedor_PC_referencia_el_puente_por_su_factura_origen_y_sin_retenciones_usa_cero(
        string tipo, string documento, TipoDocumentoPuente puente)
    {
        var factura = Guid.NewGuid();
        var e = Normalizar(tipo, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, NotaCreditoId = Guid.NewGuid(), NotaCargoId = Guid.NewGuid(), ProveedorId = Proveedor,
            FacturaOrigenId = factura, Total = 2_320m, Monto = 2_320m, Subtotal = 2_000m, Iva = 320m, SucursalId = Sucursal,
        });

        e.Documento.Tipo.Should().Be(documento);
        e.TipoPuente.Should().Be(puente);
        e.FacturaOrigenId.Should().Be(factura);
        e.OrdenCompraId.Should().BeNull();
        e.Importes["RetencionesTotal"].Should().Be(0m);
        e.Importes["TotalMenosRetenciones"].Should().Be(2_320m);
        e.ProveedorId.Should().Be(Proveedor);
    }

    [Fact]
    public void Pago_a_proveedor_PP_guarda_la_factura_pagada_para_completar_proveedor_y_sucursal()
    {
        var factura = Guid.NewGuid();
        var pago = Guid.NewGuid();
        var e = Normalizar(N.PagoProveedorAplicado, new
        {
            EmpresaId = Empresa, FacturaProveedorId = factura, PagoId = pago, Monto = 11_600m, Moneda = "MXN",
            FechaPago = new DateOnly(2026, 10, 28),
        });

        e.FacturaOrigenId.Should().Be(factura);
        e.SucursalId.Should().BeNull();
        e.ProveedorId.Should().BeNull();
        e.Documento.Id.Should().Be(pago);
        e.Importes["Total"].Should().Be(11_600m);
    }

    [Fact]
    public void Factura_de_venta_RF_lleva_ventas_netas_por_tipo_de_producto_y_cliente_menos_retenciones()
    {
        var factura = Guid.NewGuid();
        var e = Normalizar(N.FacturaVentaTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaVentaId = factura, Uuid = "UUID-FIX", Total = 2_320m, Moneda = "MXN",
            Folio = "FV-FIX-1", Subtotal = 2_100m, Descuento = 100m, Iva = 320m, RetencionesTotal = 0m, SucursalId = Sucursal,
            ClienteId = Cliente, FechaContable = new DateOnly(2026, 10, 27),
            Lineas = new object[]
            {
                new { TipoProducto = "PT", Importe = 1_500m, Descuento = 100m },
                new { TipoProducto = "Servicio", Importe = 600m, Descuento = 0m },
            },
        });

        e.Importes["Subtotal"].Should().Be(2_000m);
        e.Importes["TotalMenosRetenciones"].Should().Be(2_320m);
        e.Lineas.Select(l => (l.Importe, l.Clasificador)).Should().Equal((1_400m, "PT"), (600m, "Servicio"));
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "facturacion" && d.Tipo == "comprobante" && d.Id == factura);
    }

    [Fact]
    public void Nota_de_credito_RC_por_aplicacion_de_anticipo_va_a_anticipos_y_la_de_descuento_a_descuentos()
    {
        var anticipo = Normalizar(N.NotaCreditoTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, NotaCreditoId = Guid.NewGuid(), Total = 580m, AnticipoOrigenId = Guid.NewGuid(),
            Subtotal = 500m, Iva = 80m, SucursalId = Sucursal, ClienteId = Cliente,
        });
        var descuento = Normalizar(N.NotaCreditoTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, NotaCreditoId = Guid.NewGuid(), Total = 116m, Subtotal = 100m, Iva = 16m,
            SucursalId = Sucursal, ClienteId = Cliente,
        });

        anticipo.Importes["TotalAnticipo"].Should().Be(580m);
        anticipo.Importes["TotalDescuento"].Should().Be(0m);
        descuento.Importes["SubtotalDescuento"].Should().Be(100m);
        descuento.Importes["IvaDescuento"].Should().Be(16m);
        descuento.Importes["TotalAnticipo"].Should().Be(0m);
    }

    [Fact]
    public void Cobro_de_mostrador_PR_solo_contabiliza_la_parte_no_efectivo_por_forma_de_pago()
    {
        // Reglas de cobro del 23-oct: tarjeta → COBROS_TC_TD, transferencia → BANCOS_MN, cheque → COBROS_CHEQUE; el efectivo va a la
        // liquidación de la sesión.
        var e = Normalizar(N.CobroMostradorRegistrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, CobroMostradorId = Guid.NewGuid(), CajaSesionId = Guid.NewGuid(),
            FormasPago = new object[]
            {
                new { FormaPago = "04", Importe = 1_000m }, new { FormaPago = "28", Importe = 320m }, new { FormaPago = "03", Importe = 500m },
                new { FormaPago = "02", Importe = 200m }, new { FormaPago = "01", Importe = 300m },
            },
            SucursalId = Sucursal, ClienteId = Cliente,
        });

        e.Importes["Tarjeta"].Should().Be(1_320m);
        e.Importes["Transferencia"].Should().Be(500m);
        e.Importes["Cheque"].Should().Be(200m);
        e.Importes["OtraForma"].Should().Be(0m);
        e.Importes["TotalNoEfectivo"].Should().Be(2_020m);
        e.Documento.Tipo.Should().Be("cobro_mostrador");
    }

    [Fact]
    public void Cobro_solo_en_efectivo_no_mueve_dinero_en_su_propia_poliza()
    {
        var e = Normalizar(N.CobroMostradorRegistrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, CobroMostradorId = Guid.NewGuid(), CajaSesionId = Guid.NewGuid(),
            FormasPago = new object[] { new { FormaPago = "01", Importe = 500m } }, SucursalId = Sucursal, ClienteId = Cliente,
        });

        N.SinMovimiento(e).Should().BeTrue();
    }

    [Fact]
    public void Liquidacion_de_efectivo_lleva_un_abono_por_cobro_con_su_cliente()
    {
        // C1.5-c: tres cobros de 500, 300 y 200 → cargo 1,000 y tres líneas con su cliente.
        var clientes = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var e = N.Liquidacion(Guid.NewGuid(), Empresa, new DateOnly(2026, 10, 27), N.DocCajaSesion, Guid.NewGuid(), null, Sucursal,
            [(clientes[0], 500m), (clientes[1], 300m), (clientes[2], 200m)], "{}");

        e.Importes["Total"].Should().Be(1_000m);
        e.Lineas.Select(l => (l.ClienteId, l.Importe)).Should().Equal(((Guid?)clientes[0], 500m), (clientes[1], 300m), (clientes[2], 200m));
        e.Tipo.Should().Be(N.CajaSesionCerrada);
    }

    [Theory]
    [InlineData(N.ComprobanteCancelado, "facturacion", "comprobante", "ComprobanteId")]
    [InlineData(N.FacturaProveedorCancelada, "cuentas_por_pagar", "factura_proveedor", "FacturaProveedorId")]
    [InlineData(N.PagoProveedorRevertido, "tesoreria", "pago_factura_proveedor", "PagoOriginalId")]
    [InlineData(N.RecepcionOcCancelada, "almacen", "recepcion_oc", "RecepcionId")]
    public void Cancelaciones_anulan_la_poliza_del_mismo_documento(string tipo, string modulo, string documento, string campoId)
    {
        var id = Guid.NewGuid();
        var payload = Json(new Dictionary<string, object> { ["EmpresaId"] = Empresa, [campoId] = id, ["Motivo"] = "FIX", ["MotivoTexto"] = "FIX" });

        var anulacion = N.Anulacion(tipo, payload);

        anulacion.Should().NotBeNull();
        anulacion!.Value.Modulo.Should().Be(modulo);
        anulacion.Value.Tipo.Should().Be(documento);
        anulacion.Value.DocumentoId.Should().Be(id);
    }

    [Fact]
    public void Un_tipo_sin_plantilla_se_normaliza_con_lo_minimo_para_ir_a_la_bandeja()
    {
        // C1.5-i: p. ej. cuentas_por_pagar.factura.autorizada.v1 si llegara a la suscripción.
        var e = Normalizar("cuentas_por_pagar.factura.autorizada.v1", new { EmpresaId = Empresa, OcurridoEn = Ocurrido });

        e.EmpresaId.Should().Be(Empresa);
        e.Importes.Should().BeEmpty();
        N.Anulacion("cuentas_por_pagar.factura.autorizada.v1", "{}").Should().BeNull();
    }
}
