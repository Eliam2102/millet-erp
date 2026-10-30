using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.Integration;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Workers;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Facturacion.Application.Integration;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// C1.5 · los cuatro listeners de Contabilidad con los eventos reales de cada módulo (los records de integración serializados como
/// el outbox) entregados por la entrada local (C1.8). Un caso por criterio de la ficha 86e3kdw33 §5 (C1.5-a … C1.5-i). Cada prueba
/// mapea los roles a cuentas FIX propias y restaura el mapeo al terminar. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;
    private static DateOnly Hoy() => Millet.SharedKernel.Application.FechaContable.HoyLocal(Ahora);
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    /// <summary>Rol → (sufijo de código, naturaleza, control). Todas afectables bajo una raíz FIX.</summary>
    private static readonly (string Rol, string Codigo, string Naturaleza, string Control)[] Roles =
    [
        ("INVENTARIO", "1.1", "Deudora", "Ninguna"),
        ("IVA_ACREDITABLE_PENDIENTE", "1.2", "Deudora", "Ninguna"),
        ("BANCOS", "1.3", "Deudora", "Ninguna"),
        ("BANCOS_MN", "1.4", "Deudora", "Ninguna"),
        ("CLIENTES", "1.5", "Deudora", "Clientes"),
        ("COBROS_TC_TD", "1.6", "Deudora", "Ninguna"),
        ("COBROS_CHEQUE", "1.7", "Deudora", "Ninguna"),
        ("EFECTIVO_CAJA_COBRANZA", "1.8", "Deudora", "Ninguna"),
        ("ANTICIPO_A_PROVEEDORES", "1.9", "Deudora", "Ninguna"),
        ("MERCANCIA_RECIBIDA_POR_FACTURAR", "2.1", "Acreedora", "Ninguna"),
        ("PROVEEDORES", "2.2", "Acreedora", "Proveedores"),
        ("RETENCIONES_POR_PAGAR", "2.3", "Acreedora", "Ninguna"),
        ("IVA_TRASLADADO_NO_COBRADO", "2.4", "Acreedora", "Ninguna"),
        ("ANTICIPOS_DE_CLIENTES", "2.5", "Acreedora", "Ninguna"),
        ("VENTAS", "4.1", "Acreedora", "Ninguna"),
        ("DESCUENTOS_SOBRE_VENTAS", "4.2", "Deudora", "Ninguna"),
        ("CONSUMO", "5.1", "Deudora", "Ninguna"),
        ("AJUSTE_INVENTARIO", "5.2", "Deudora", "Ninguna"),
    ];

    private sealed record Escenario(string Suf, HttpClient Admin, Guid Sucursal, Guid Proveedor, Guid[] Clientes, Guid CentroCosto,
        Dictionary<string, Guid> Cuentas, Dictionary<string, Guid?> Previo);

    private async Task<Escenario> PrepararAsync()
    {
        var suf = Sufijo();
        var admin = await LoginAsync(factory);
        await AsegurarPeriodosAbiertosAsync(admin, Hoy());
        var sucursalResp = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"LC{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursalResp.EnsureSuccessStatusCode();
        var sucursal = Id(await Json(sucursalResp));

        // Centro de costo (Dim3) de la sucursal: ubicación → CeCo → equipo.
        Guid centro;
        using (var scope = factory.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var g2 = await mediator.Send(new CrearGrupoDim2Command($"FIX G2 {suf}"));
            var g3 = await mediator.Send(new CrearGrupoDim3Command($"FIX G3 {suf}"));
            var d1 = await mediator.Send(new CrearDim1Command($"L{suf}", $"FIX Ubicación {suf}"));
            var d2 = await mediator.Send(new CrearDim2Command(d1.Id, $"M{suf}", "FIX CeCo mantenimiento", g2.Id));
            centro = (await mediator.Send(new CrearDim3Command(d2.Id, $"N{suf}", "FIX CC-DEMO-MANT", g3.Id))).Id;
            (await admin.PutAsJsonAsync($"/api/v1/contabilidad/ubicaciones-sucursal/{d1.Id}", new { sucursalId = sucursal })).EnsureSuccessStatusCode();
        }

        var activo = Id(await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo"));
        var pasivo = Id(await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora"));
        var ingresos = Id(await CrearCuenta(admin, Codigo(suf, "4"), "FIX ingresos", naturaleza: "Acreedora"));
        var gastos = Id(await CrearCuenta(admin, Codigo(suf, "5"), "FIX gastos"));
        var cuentas = new Dictionary<string, Guid>();
        foreach (var (rol, codigo, naturaleza, control) in Roles)
        {
            var padre = codigo[0] switch { '1' => activo, '2' => pasivo, '4' => ingresos, _ => gastos };
            cuentas[rol] = Id(await CrearCuenta(admin, Codigo(suf, codigo), $"FIX {rol}", padre, naturaleza: naturaleza, control: control));
        }

        var (proveedor, cliente1) = await MotorTerceros.SembrarAsync(factory);
        var (_, cliente2) = await MotorTerceros.SembrarAsync(factory);
        var (_, cliente3) = await MotorTerceros.SembrarAsync(factory);

        var previo = new Dictionary<string, Guid?>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var codigos = cuentas.Keys.ToList();
            foreach (var r in await db.RolesCuenta.IgnoreQueryFilters().Where(r => r.EmpresaId == EmpresaBootstrapId && codigos.Contains(r.Codigo)).ToListAsync())
                previo[r.Codigo] = r.CuentaId;
            foreach (var (rol, cuenta) in cuentas)
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}",
                    cuenta, EmpresaBootstrapId, rol);
        }
        return new(suf, admin, sucursal, proveedor, [cliente1, cliente2, cliente3], centro, cuentas, previo);
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        foreach (var (codigo, cuenta) in e.Previo)
            await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}",
                cuenta!, EmpresaBootstrapId, codigo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE cuenta_nueva_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE payload::text LIKE {0}", $"%{e.Sucursal}%");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cobros_efectivo_caja WHERE sucursal_id = {0}", e.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consumos_contabilizados WHERE poliza_id IN (SELECT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = {0})", e.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT p.id::text FROM contabilidad.polizas p JOIN contabilidad.polizas_partidas x ON x.poliza_id = p.id WHERE x.sucursal_id = {0})", e.Sucursal);
        await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.polizas SET anulada_por_poliza_id = NULL, poliza_anulada_id = NULL WHERE id IN (SELECT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = {0})", e.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE id IN (SELECT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = {0})", e.Sucursal);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        await Limpiar(factory.Services, e.Suf);
    }

    // ---------------------------------------------------------------- Entrega local y consultas

    private T Listener<T>() where T : ContabilidadEventListenerWorker =>
        (T)Activator.CreateInstance(typeof(T), null, factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<T>.Instance)!;

    /// <summary>Entrega el evento como lo haría el outbox (mismo JSON, id de la fila) al listener del topic.</summary>
    private async Task<Guid> EntregarAsync<T>(IntegrationEvent evento, Guid? eventoId = null) where T : ContabilidadEventListenerWorker
    {
        var id = eventoId ?? Guid.CreateVersion7();
        ILocalIntegrationEventConsumer consumidor = Listener<T>();
        await consumidor.ConsumirLocalAsync(evento.EventType, id, JsonSerializer.Serialize(evento, evento.GetType()), CancellationToken.None);
        return id;
    }

    private Task<Guid> Almacen(IntegrationEvent e, Guid? id = null) => EntregarAsync<AlmacenContabilidadListenerWorker>(e, id);
    private Task<Guid> Cxp(IntegrationEvent e, Guid? id = null) => EntregarAsync<CuentasPorPagarContabilidadListenerWorker>(e, id);
    private Task<Guid> Tesoreria(IntegrationEvent e, Guid? id = null) => EntregarAsync<TesoreriaContabilidadListenerWorker>(e, id);
    private Task<Guid> Facturacion(IntegrationEvent e, Guid? id = null) => EntregarAsync<FacturacionContabilidadListenerWorker>(e, id);

    private async Task<List<Poliza>> PolizasAsync(Guid documentoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        return await db.Polizas.IgnoreQueryFilters().AsNoTracking().Include(p => p.Partidas)
            .Where(p => p.OrigenDocumentoId == documentoId).OrderBy(p => p.Numero).ToListAsync();
    }

    private async Task<List<PolizaPartida>> PartidasSucursalAsync(Guid sucursal)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        return await db.PolizasPartidas.IgnoreQueryFilters().AsNoTracking().Where(x => x.SucursalId == sucursal).ToListAsync();
    }

    private async Task<RechazoContable?> RechazoAsync(Guid eventoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        return await db.RechazosContables.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.EventoId == eventoId);
    }

    private static decimal Saldo(IEnumerable<PolizaPartida> partidas, Guid cuenta) =>
        partidas.Where(x => x.CuentaId == cuenta).Sum(x => x.Cargo - x.Abono);

    // ---------------------------------------------------------------- Eventos DEMO

    private static FacturaVentaTimbradaIntegrationEvent FacturaVenta(Escenario e, Guid factura, Guid cliente, decimal subtotal = 2_000m, decimal iva = 320m) =>
        new(EmpresaBootstrapId, Ahora, factura, $"UUID-{factura:N}", subtotal + iva, "MXN", null, "XAXX010101000", "FIX cliente", "FV-FIX",
            "PUE", Ahora, Subtotal: subtotal, Descuento: 0m, Iva: iva, RetencionesTotal: 0m, SucursalId: e.Sucursal, ClienteId: cliente,
            FechaContable: Hoy(), Lineas: [new FacturaLineaContablePayload(null, "30171500", "PT", subtotal, 0m, iva)]);

    private static CobroMostradorRegistradoIntegrationEvent Cobro(Escenario e, Guid sesion, Guid cliente, params (string Forma, decimal Importe)[] formas) =>
        new(EmpresaBootstrapId, Ahora, Guid.CreateVersion7(), sesion, Guid.NewGuid(), Guid.NewGuid(), "FacturaVenta", "Mostrador",
            formas.Sum(f => f.Importe), [.. formas.Select(f => new CobroFormaPagoAplicada(f.Forma, f.Importe))], e.Sucursal, cliente, "MXN");

    /// <summary>Venta directa (sin pedido): Facturación publica el cobro sin cliente; solo trae el comprobante cobrado.</summary>
    private static CobroMostradorRegistradoIntegrationEvent CobroSinCliente(Escenario e, Guid sesion, Guid comprobante,
        params (string Forma, decimal Importe)[] formas) =>
        new(EmpresaBootstrapId, Ahora, Guid.CreateVersion7(), sesion, Guid.NewGuid(), comprobante, "FacturaVenta", "Mostrador",
            formas.Sum(f => f.Importe), [.. formas.Select(f => new CobroFormaPagoAplicada(f.Forma, f.Importe))], e.Sucursal, ClienteId: null,
            Moneda: "MXN");

    private static CajaSesionCerradaIntegrationEvent SesionCerrada(Escenario e, Guid sesion, decimal efectivo) =>
        new(EmpresaBootstrapId, Ahora, sesion, Guid.NewGuid(), e.Sucursal, Guid.NewGuid(), Hoy(), 500m, efectivo + 500m, efectivo + 500m, 0m,
            false, [new CajaSesionCorteTotal("01", efectivo + 500m, efectivo + 500m)]);

    // ---------------------------------------------------------------- Criterios

    [Fact]
    public async Task C15a_compra_a_pago_genera_EM_TT_y_PP_la_balanza_cuadra_y_el_puente_de_la_OC_queda_en_cero()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var oc = Guid.NewGuid();
            var lineaOc = Guid.NewGuid();
            var recepcion = Guid.NewGuid();
            var factura = Guid.NewGuid();
            var pago = Guid.NewGuid();

            await Almacen(new OcRecepcionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, recepcion, "OC-DEMO-C15-01-R1", oc, Hoy(), false,
                null, null, null, [new LineaRecepcionPayload(Guid.NewGuid(), lineaOc, Guid.NewGuid(), "PZA", 10m, 1_000m, 10_000m, Guid.NewGuid())],
                SucursalId: e.Sucursal));
            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, oc, 11_600m,
                [new LineaFacturadaPayload(Guid.NewGuid(), lineaOc, 10m, 10_000m)], [new LineaOcAcumuladaPayload(lineaOc, 10m)],
                ProveedorId: e.Proveedor, Uuid: $"UUID-{factura:N}", Subtotal: 10_000m, Iva: 1_600m, RetencionesTotal: 0m, Moneda: "MXN",
                SucursalId: e.Sucursal));
            await Tesoreria(new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, pago, 11_600m, "MXN", Hoy(),
                "Transferencia", "REF-FIX"));

            (await PolizasAsync(recepcion)).Should().ContainSingle().Which.TipoClave.Should().Be("EM");
            (await PolizasAsync(factura)).Should().ContainSingle().Which.TipoClave.Should().Be("TT");
            var pp = (await PolizasAsync(pago)).Should().ContainSingle().Subject;
            pp.TipoClave.Should().Be("PP");
            pp.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PROVEEDORES"] && x.Cargo == 11_600m && x.ProveedorId == e.Proveedor);

            var partidas = await PartidasSucursalAsync(e.Sucursal);
            partidas.Sum(x => x.Cargo).Should().Be(partidas.Sum(x => x.Abono), "la balanza del periodo cuadra");
            Saldo(partidas, e.Cuentas["MERCANCIA_RECIBIDA_POR_FACTURAR"]).Should().Be(0m, "el puente de la OC queda en 0");
            Saldo(partidas, e.Cuentas["PROVEEDORES"]).Should().Be(0m);
            Saldo(partidas, e.Cuentas["INVENTARIO"]).Should().Be(10_000m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15a_el_pago_que_llega_antes_que_la_factura_espera_y_nace_solo_al_registrarse_la_TT()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var oc = Guid.NewGuid();
            var factura = Guid.NewGuid();
            var pago = Guid.NewGuid();

            var eventoPago = await Tesoreria(new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, pago, 1_160m, "MXN",
                Hoy(), "Transferencia", null));
            var rechazo = await RechazoAsync(eventoPago);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);

            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, oc, 1_160m,
                [new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 1_000m)], [], ProveedorId: e.Proveedor, Subtotal: 1_000m,
                Iva: 160m, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: e.Sucursal));

            (await PolizasAsync(pago)).Should().ContainSingle().Which.TipoClave.Should().Be("PP");
            (await RechazoAsync(eventoPago))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15b_venta_cobrada_con_tarjeta_genera_RF_y_una_PR_con_cargo_a_cobros_con_tarjeta()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));
            var cobro = Cobro(e, Guid.NewGuid(), e.Clientes[0], ("04", 2_320m));
            await Facturacion(cobro);

            var rf = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            rf.TipoClave.Should().Be("RF");
            rf.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Cargo == 2_320m && x.ClienteId == e.Clientes[0]);
            rf.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["VENTAS"] && x.Abono == 2_000m);
            rf.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_NO_COBRADO"] && x.Abono == 320m);

            var pr = (await PolizasAsync(cobro.CobroMostradorId)).Should().ContainSingle().Subject;
            pr.TipoClave.Should().Be("PR");
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["COBROS_TC_TD"] && x.Cargo == 2_320m);
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Abono == 2_320m && x.ClienteId == e.Clientes[0]);

            var partidas = await PartidasSucursalAsync(e.Sucursal);
            partidas.Sum(x => x.Cargo).Should().Be(partidas.Sum(x => x.Abono));
            Saldo(partidas, e.Cuentas["CLIENTES"]).Should().Be(0m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15c_tres_cobros_en_efectivo_generan_una_sola_poliza_al_cerrar_la_sesion()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var sesion = Guid.NewGuid();
            var cobros = new[]
            {
                Cobro(e, sesion, e.Clientes[0], ("01", 500m)),
                Cobro(e, sesion, e.Clientes[1], ("01", 300m)),
                Cobro(e, sesion, e.Clientes[2], ("01", 200m)),
            };
            foreach (var c in cobros) await Facturacion(c);
            foreach (var c in cobros) (await PolizasAsync(c.CobroMostradorId)).Should().BeEmpty("los cobros en efectivo no generan pólizas sueltas");

            await Facturacion(SesionCerrada(e, sesion, 1_000m));

            var pr = (await PolizasAsync(sesion)).Should().ContainSingle().Subject;
            pr.TipoClave.Should().Be("PR");
            pr.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["EFECTIVO_CAJA_COBRANZA"]).Which.Cargo.Should().Be(1_000m);
            pr.Partidas.Where(x => x.CuentaId == e.Cuentas["CLIENTES"]).Select(x => (x.ClienteId, x.Abono))
                .Should().BeEquivalentTo(new (Guid?, decimal)[] { (e.Clientes[0], 500m), (e.Clientes[1], 300m), (e.Clientes[2], 200m) });
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15c_un_cobro_en_efectivo_cancelado_con_la_sesion_abierta_sale_de_la_liquidacion()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var sesion = Guid.NewGuid();
            var vigente = Cobro(e, sesion, e.Clientes[0], ("01", 500m));
            var cancelado = Cobro(e, sesion, e.Clientes[1], ("01", 300m));
            await Facturacion(vigente);
            await Facturacion(cancelado);
            await Facturacion(new CobroMostradorCanceladoIntegrationEvent(EmpresaBootstrapId, Ahora, cancelado.CobroMostradorId,
                cancelado.ComprobanteId, 300m, ReversadoEnSesion: true));
            await Facturacion(SesionCerrada(e, sesion, 500m));

            var pr = (await PolizasAsync(sesion)).Should().ContainSingle().Subject;
            pr.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["EFECTIVO_CAJA_COBRANZA"]).Which.Cargo.Should().Be(500m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15c_venta_directa_el_cobro_con_tarjeta_y_la_liquidacion_sin_cliente_toman_el_cliente_de_la_RF()
    {
        // QA 28-oct: en venta directa el cobro llega sin ClienteId y caía en la bandeja con «Dato faltante · ClienteId».
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var sesion = Guid.NewGuid();
            var conTarjeta = Guid.NewGuid();
            var enEfectivo = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, conTarjeta, e.Clientes[0]));
            await Facturacion(FacturaVenta(e, enEfectivo, e.Clientes[1], subtotal: 500m, iva: 80m));

            var tarjeta = CobroSinCliente(e, sesion, conTarjeta, ("04", 2_320m));
            var tarjetaEvento = await Facturacion(tarjeta);
            await Facturacion(CobroSinCliente(e, sesion, enEfectivo, ("01", 580m)));
            await Facturacion(SesionCerrada(e, sesion, 580m));

            (await RechazoAsync(tarjetaEvento)).Should().BeNull();
            var pr = (await PolizasAsync(tarjeta.CobroMostradorId)).Should().ContainSingle().Subject;
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Abono == 2_320m && x.ClienteId == e.Clientes[0]);

            var liquidacion = (await PolizasAsync(sesion)).Should().ContainSingle().Subject;
            liquidacion.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["CLIENTES"])
                .Which.Should().Match<PolizaPartida>(x => x.Abono == 580m && x.ClienteId == e.Clientes[1]);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15c_el_cobro_sin_cliente_que_llega_antes_que_su_RF_espera_y_nace_al_registrarse_la_RF()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var cobro = CobroSinCliente(e, Guid.NewGuid(), factura, ("03", 2_320m));

            var evento = await Facturacion(cobro);
            (await RechazoAsync(evento))!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
            (await PolizasAsync(cobro.CobroMostradorId)).Should().BeEmpty();

            await Facturacion(FacturaVenta(e, factura, e.Clientes[2]));

            var pr = (await PolizasAsync(cobro.CobroMostradorId)).Should().ContainSingle().Subject;
            pr.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Abono == 2_320m && x.ClienteId == e.Clientes[2]);
            (await RechazoAsync(evento))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15b_la_nota_de_credito_sin_cliente_toma_el_cliente_de_su_factura_relacionada()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var nota = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));

            var evento = await Facturacion(new NotaCreditoTimbradaIntegrationEvent(EmpresaBootstrapId, Ahora, nota, "Descuento", $"UUID-{nota:N}",
                232m, FacturaRelacionadaId: factura, AnticipoOrigenId: null, Subtotal: 200m, Iva: 32m, SucursalId: e.Sucursal, ClienteId: null,
                Moneda: "MXN", FechaContable: Hoy()));

            (await RechazoAsync(evento)).Should().BeNull();
            var rc = (await PolizasAsync(nota)).Should().ContainSingle().Subject;
            rc.TipoClave.Should().Be("RC");
            rc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["DESCUENTOS_SOBRE_VENTAS"] && x.Cargo == 200m);
            rc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Abono == 232m && x.ClienteId == e.Clientes[0]);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15d_un_cobro_con_transferencia_y_otro_con_cheque_generan_dos_polizas()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Database.ExecuteSqlRawAsync(
                    "UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'COBROS_CHEQUE'",
                    e.Cuentas["COBROS_CHEQUE"], EmpresaBootstrapId);
            var sesion = Guid.NewGuid();
            var transferencia = Cobro(e, sesion, e.Clientes[0], ("03", 1_500m));
            var cheque = Cobro(e, sesion, e.Clientes[1], ("02", 700m));
            await Facturacion(transferencia);
            await Facturacion(cheque);

            (await PolizasAsync(transferencia.CobroMostradorId)).Should().ContainSingle().Which.Partidas
                .Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS_MN"] && x.Cargo == 1_500m);
            (await PolizasAsync(cheque.CobroMostradorId)).Should().ContainSingle().Which.Partidas
                .Should().Contain(x => x.CuentaId == e.Cuentas["COBROS_CHEQUE"] && x.Cargo == 700m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15e_el_mismo_evento_de_factura_de_venta_entregado_dos_veces_da_una_sola_poliza()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var evento = FacturaVenta(e, factura, e.Clientes[0]);
            var id = await Facturacion(evento);
            await Facturacion(evento, id);
            (await PolizasAsync(factura)).Should().ContainSingle();
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15f_cancelar_el_comprobante_genera_la_anulacion_en_negativo_ligada_a_la_RF()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));
            var rf = (await PolizasAsync(factura)).Single();

            await Facturacion(new ComprobanteCanceladoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, "Ingreso", $"UUID-{factura:N}"));

            var polizas = await PolizasAsync(factura);
            polizas.Should().HaveCount(2);
            var anulacion = polizas.Single(p => p.PolizaAnuladaId == rf.Id);
            anulacion.Concepto.Should().Be($"Anulación de {rf.Folio}");
            anulacion.Partidas.Sum(x => x.Cargo).Should().Be(-rf.Partidas.Sum(x => x.Cargo));
            polizas.Single(p => p.Id == rf.Id).Estado.Should().Be(EstadoPoliza.Anulada);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15f_la_cancelacion_que_llega_antes_que_la_factura_espera_y_se_aplica_al_registrarse_la_RF()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var cancelacion = await Facturacion(new ComprobanteCanceladoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, "Ingreso", "UUID"));
            (await RechazoAsync(cancelacion))!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);

            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));

            (await PolizasAsync(factura)).Should().HaveCount(2).And.Contain(p => p.PolizaAnuladaId != null);
            (await RechazoAsync(cancelacion))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15g_la_devolucion_interna_reversa_con_la_misma_cuenta_y_centro_de_costo_de_la_salida()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var salida = Guid.NewGuid();
            var articulo = Guid.NewGuid();
            await Almacen(new SalidaRequisicionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, salida, "SAL-FIX-1", Hoy(), null, false, null,
                null, [new LineaSalidaPayload(Guid.NewGuid(), articulo, "PZA", 8m, 100m, 800m, e.CentroCosto, null)], SucursalId: e.Sucursal));

            // V64: aunque el rol cambie de cuenta después de la salida, la devolución usa la cuenta de la póliza original.
            var otraCuenta = Id(await CrearCuenta(e.Admin, Codigo(e.Suf, "5.9"), "FIX consumo nuevo"));
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Database.ExecuteSqlRawAsync(
                    "UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'CONSUMO'", otraCuenta, EmpresaBootstrapId);

            var devolucion = Guid.NewGuid();
            await Almacen(new DevolucionInternaAplicadaIntegrationEvent(EmpresaBootstrapId, Ahora, devolucion, "DEV-FIX-1", salida, Guid.NewGuid(),
                "Bueno", 300m, [new LineaDevolucionPayload(Guid.NewGuid(), articulo, "PZA", 3m, 100m, 300m)], SucursalId: e.Sucursal));

            var sm = (await PolizasAsync(devolucion)).Should().ContainSingle().Subject;
            sm.TipoClave.Should().Be("SM");
            sm.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["INVENTARIO"]).Which.Cargo.Should().Be(300m);
            sm.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["CONSUMO"])
                .Which.Should().Match<PolizaPartida>(x => x.Abono == 300m && x.Dim3Id == e.CentroCosto);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15h_la_recepcion_con_una_linea_de_activo_fijo_solo_contabiliza_el_material()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var recepcion = Guid.NewGuid();
            await Almacen(new OcRecepcionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, recepcion, "REC-FIX-AF", Guid.NewGuid(), Hoy(), false,
                null, null, null,
                [
                    new LineaRecepcionPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PZA", 1m, 25_000m, 25_000m, Guid.NewGuid(), EsActivoFijo: true),
                    new LineaRecepcionPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PZA", 4m, 1_000m, 4_000m, Guid.NewGuid()),
                ],
                SucursalId: e.Sucursal));

            var em = (await PolizasAsync(recepcion)).Should().ContainSingle().Subject;
            em.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["INVENTARIO"]).Which.Cargo.Should().Be(4_000m);
            em.Partidas.Sum(x => x.Cargo).Should().Be(4_000m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task C15i_un_evento_contable_sin_plantilla_vigente_queda_en_la_bandeja_con_SinPlantilla()
    {
        // C1.4-d: un tipo que sí se contabiliza pero sin plantilla vigente no se pierde. La plantilla #8 se vuelve no vigente
        // (vigencia futura; el motor no mira deleted_at) y se restaura al terminar.
        Escenario? e = null;
        var anticipo = Guid.NewGuid();
        var evento = Guid.Empty;
        var vigencias = new List<(Guid Id, DateOnly Desde)>();
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                vigencias = (await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.EmpresaId == EmpresaBootstrapId && p.TipoEvento == "facturacion.factura-anticipo.timbrada.v1")
                    .Select(p => new { p.Id, p.VigenteDesde }).ToListAsync()).Select(p => (p.Id, p.VigenteDesde)).ToList();
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE contabilidad.plantillas_contables SET vigente_desde = DATE '2999-01-01' WHERE empresa_id = {0} AND tipo_evento = {1}",
                    EmpresaBootstrapId, "facturacion.factura-anticipo.timbrada.v1");
            }

            evento = await Facturacion(new FacturaAnticipoTimbradaIntegrationEvent(EmpresaBootstrapId, Ahora, anticipo, Guid.NewGuid(),
                $"UUID-{anticipo:N}", 1_160m, "MXN", Subtotal: 1_000m, Iva: 160m, SucursalId: e.Sucursal, ClienteId: e.Clientes[0], FechaContable: Hoy()));

            var rechazo = await RechazoAsync(evento);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.SinPlantilla);
            (await PolizasAsync(anticipo)).Should().BeEmpty();
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                foreach (var (id, desde) in vigencias)
                    await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.plantillas_contables SET vigente_desde = {0} WHERE id = {1}", desde, id);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE evento_id = {0}", evento);
            }
            await LimpiarAsync(e);
        }
    }

    [Fact]
    public async Task C15i_un_evento_informativo_del_modulo_se_ignora_sin_llegar_a_la_bandeja()
    {
        // QA 28-oct: con la entrega local (sin filtros de suscripción) la bandeja se llenaba de «Sin plantilla» informativos.
        var evento = Guid.CreateVersion7();
        ILocalIntegrationEventConsumer consumidor = Listener<CuentasPorPagarContabilidadListenerWorker>();
        await consumidor.ConsumirLocalAsync("cuentas_por_pagar.factura.autorizada.v1", evento,
            JsonSerializer.Serialize(new { EmpresaId = EmpresaBootstrapId, OcurridoEn = Ahora }), CancellationToken.None);

        (await RechazoAsync(evento)).Should().BeNull();
    }

    [Fact]
    public async Task C15d_la_salida_sin_centro_de_costo_queda_en_la_bandeja_con_dato_faltante()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var salida = Guid.NewGuid();
            var evento = await Almacen(new SalidaRequisicionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, salida, "SAL-FIX-SC", Hoy(), null,
                false, null, null, [new LineaSalidaPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 2m, 100m, 200m, null, null)], SucursalId: e.Sucursal));

            var rechazo = await RechazoAsync(evento);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
            rechazo.Detalle.Should().Be("CentroCostoId");
            (await PolizasAsync(salida)).Should().BeEmpty();
        }
        finally { await LimpiarAsync(e); }
    }
}
