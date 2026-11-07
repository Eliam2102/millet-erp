using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.Integration;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Facturacion.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Movimientos;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// U-2 (86e3yxngq) · pólizas de los eventos que Contabilidad no recibía, con el motor real y las plantillas sembradas por la migración.
/// Los eventos son los records de Tesorería y Almacén serializados como el outbox y entregados al listener de su topic. Un caso por
/// criterio de la ficha (U2-1 … U2-5) y los demás eventos del alcance. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static DepositoCajaConfirmadoIntegrationEvent Deposito(Escenario e, Guid deposito, decimal importe) =>
        new(EmpresaBootstrapId, Ahora, deposito, Guid.NewGuid(), Guid.NewGuid(), Hoy(), e.Sucursal, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            importe, "MXN", Hoy(), "FICHA-FIX", "Ficha", "FICHA-FIX", importe, 0m, true, Guid.NewGuid());

    private static MovimientoBancarioRegistradoIntegrationEvent MovimientoBanco(Escenario e, Guid movimiento, SentidoMovimiento sentido, decimal monto,
        string concepto, decimal? iva = null, Guid? movimientoBase = null) =>
        new(EmpresaBootstrapId, Ahora, movimiento, Guid.NewGuid(), sentido, monto, "MXN", Hoy(), "EXT-FIX", Guid.NewGuid(), concepto, iva,
            SucursalId: e.Sucursal, MovimientoBaseId: movimientoBase);

    [Fact]
    public async Task U2_1_el_deposito_del_corte_carga_bancos_y_abona_el_efectivo_en_caja()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var deposito = Guid.NewGuid();
            var evento = await Tesoreria(Deposito(e, deposito, 700m));

            (await RechazoAsync(evento)).Should().BeNull();
            var dp = (await PolizasAsync(deposito)).Should().ContainSingle().Subject;
            dp.TipoClave.Should().Be("DP");
            dp.Partidas.Should().HaveCount(2);
            dp.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Cargo == 700m);
            dp.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["EFECTIVO_CAJA_COBRANZA"] && x.Abono == 700m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_2_la_comision_con_IVA_del_extracto_genera_una_sola_poliza_con_comision_IVA_y_bancos()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            Guid baseId = Guid.NewGuid(), ivaId = Guid.NewGuid();
            // Tesorería registra dos movimientos (base con su IVA y el IVA aparte); el del IVA no genera otra póliza.
            var eventoBase = await Tesoreria(MovimientoBanco(e, baseId, SentidoMovimiento.Egreso, 100m, "Comisión bancaria", iva: 16m));
            var eventoIva = await Tesoreria(MovimientoBanco(e, ivaId, SentidoMovimiento.Egreso, 16m, "Impuestos", movimientoBase: baseId));

            (await RechazoAsync(eventoBase)).Should().BeNull();
            (await RechazoAsync(eventoIva)).Should().BeNull();
            (await PolizasAsync(ivaId)).Should().BeEmpty();
            var mb = (await PolizasAsync(baseId)).Should().ContainSingle().Subject;
            mb.Partidas.Should().HaveCount(3);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CONCEPTO_BANCARIO_COMISION_BANCARIA"] && x.Cargo == 100m);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PAGADO"] && x.Cargo == 16m);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Abono == 116m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_3_el_costo_de_importacion_carga_inventario_y_lo_vendido_contra_gastos_de_importacion_por_aplicar()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var expediente = Guid.NewGuid();
            // $10,000 repartidos: 8 de 10 hojas en existencia ($8,000) y 2 vendidas ($2,000), como lo calcula Almacén.
            await Almacen(new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, expediente, 1,
                [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, 8_000m, 2_000m)])
                { SucursalId = e.Sucursal });

            var ci = (await PolizasAsync(expediente)).Should().ContainSingle().Subject;
            ci.Partidas.Should().HaveCount(3);
            ci.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["INVENTARIO"] && x.Cargo == 8_000m);
            ci.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["GASTO_IMPORTACION"] && x.Cargo == 2_000m);
            ci.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["GASTOS_IMPORTACION_POR_APLICAR"] && x.Abono == 10_000m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_4_el_mismo_evento_entregado_dos_veces_da_una_sola_poliza()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var deposito = Guid.NewGuid();
            var evento = Deposito(e, deposito, 700m);
            var id = await Tesoreria(evento);
            await Tesoreria(evento, id);

            (await PolizasAsync(deposito)).Should().ContainSingle();
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_5_sin_cuenta_queda_en_la_bandeja_con_el_motivo_y_al_configurarla_y_reprocesar_nace_la_poliza()
    {
        // «Nómina» no tiene rol propio: cae en el rol base CONCEPTO_BANCARIO, que nace sin cuenta.
        Escenario? e = null;
        Guid? previo = null;
        var movimiento = Guid.NewGuid();
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                previo = await db.RolesCuenta.IgnoreQueryFilters().Where(r => r.EmpresaId == EmpresaBootstrapId && r.Codigo == "CONCEPTO_BANCARIO")
                    .Select(r => r.CuentaId).FirstOrDefaultAsync();
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = NULL WHERE empresa_id = {0} AND codigo = 'CONCEPTO_BANCARIO'",
                    EmpresaBootstrapId);
            }

            var evento = await Tesoreria(MovimientoBanco(e, movimiento, SentidoMovimiento.Egreso, 250m, "Nómina"));
            var rechazo = await RechazoAsync(evento);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.RolSinCuenta);
            rechazo.Detalle.Should().Be("CONCEPTO_BANCARIO");
            (await PolizasAsync(movimiento)).Should().BeEmpty();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'CONCEPTO_BANCARIO'",
                    e.Cuentas["CONSUMO"], EmpresaBootstrapId);
            }
            (await e.Admin.PostAsync($"/api/v1/contabilidad/motor/rechazos/{rechazo.Id}/reprocesar", null)).EnsureSuccessStatusCode();

            var mb = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CONSUMO"] && x.Cargo == 250m);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Abono == 250m);
            (await RechazoAsync(evento))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'CONCEPTO_BANCARIO'",
                    previo!, EmpresaBootstrapId);
            }
            await LimpiarAsync(e);
        }
    }

    [Fact]
    public async Task U2_un_interes_ganado_invierte_los_lados_bancos_al_cargo()
    {
        Escenario? e = null;
        Guid? previo = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                previo = await db.RolesCuenta.IgnoreQueryFilters().Where(r => r.EmpresaId == EmpresaBootstrapId && r.Codigo == "CONCEPTO_BANCARIO_INTERESES_GANADOS")
                    .Select(r => r.CuentaId).FirstOrDefaultAsync();
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'CONCEPTO_BANCARIO_INTERESES_GANADOS'",
                    e.Cuentas["VENTAS"], EmpresaBootstrapId);
            }
            var movimiento = Guid.NewGuid();
            await Tesoreria(MovimientoBanco(e, movimiento, SentidoMovimiento.Ingreso, 40m, "Intereses ganados"));

            var mb = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Cargo == 40m);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["VENTAS"] && x.Abono == 40m);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = 'CONCEPTO_BANCARIO_INTERESES_GANADOS'",
                    previo!, EmpresaBootstrapId);
            }
            await LimpiarAsync(e);
        }
    }

    [Fact]
    public async Task U2_el_faltante_de_corte_va_al_deudor_y_su_devolucion_lo_salda()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            Guid diferencia = Guid.NewGuid(), sesion = Guid.NewGuid(), cajero = Guid.NewGuid(), devolucion = Guid.NewGuid();
            await Tesoreria(new DiferenciaCorteAutorizadaIntegrationEvent(EmpresaBootstrapId, Ahora, diferencia, sesion, Hoy(), e.Sucursal, cajero,
                DiferenciaCorteAutorizadaIntegrationEvent.Faltante, 50m, "MXN", Guid.NewGuid()));
            await Tesoreria(new DiferenciaCorteDevolucionAplicadaIntegrationEvent(EmpresaBootstrapId, Ahora, diferencia, sesion, Hoy(), e.Sucursal,
                cajero, devolucion, 50m, 50m, 0m, true, "MXN"));

            var dc = (await PolizasAsync(diferencia)).Should().ContainSingle().Subject;
            dc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["DEUDORES_FUNCIONARIOS_EMPLEADOS"] && x.Cargo == 50m);
            dc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["EFECTIVO_CAJA_COBRANZA"] && x.Abono == 50m);
            var dv = (await PolizasAsync(devolucion)).Should().ContainSingle().Subject;
            Saldo(dc.Partidas.Concat(dv.Partidas), e.Cuentas["DEUDORES_FUNCIONARIOS_EMPLEADOS"]).Should().Be(0m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_el_sobrante_de_corte_queda_a_favor_del_cajero()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var diferencia = Guid.NewGuid();
            await Tesoreria(new DiferenciaCorteAutorizadaIntegrationEvent(EmpresaBootstrapId, Ahora, diferencia, Guid.NewGuid(), Hoy(), e.Sucursal,
                Guid.NewGuid(), DiferenciaCorteAutorizadaIntegrationEvent.Sobrante, 30m, "MXN", Guid.NewGuid()));

            var dc = (await PolizasAsync(diferencia)).Should().ContainSingle().Subject;
            dc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["EFECTIVO_CAJA_COBRANZA"] && x.Cargo == 30m);
            dc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["DEUDORES_FUNCIONARIOS_EMPLEADOS"] && x.Abono == 30m);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task U2_el_reembolso_pagado_de_una_NC_carga_al_cliente_de_la_NC_y_abona_bancos_y_los_demas_estados_no_contabilizan()
    {
        Escenario? e = null;
        var eventos = new List<Guid>();
        try
        {
            e = await PrepararAsync();
            Guid factura = Guid.NewGuid(), nota = Guid.NewGuid(), devolucion = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0]));
            await Facturacion(new NotaCreditoTimbradaIntegrationEvent(EmpresaBootstrapId, Ahora, nota, "Descuento", $"UUID-{nota:N}",
                232m, FacturaRelacionadaId: factura, AnticipoOrigenId: null, Subtotal: 200m, Iva: 32m, SucursalId: e.Sucursal,
                ClienteId: e.Clientes[0], Moneda: "MXN", FechaContable: Hoy()));

            eventos.Add(await Tesoreria(new ReembolsoNotaCreditoActualizadoIntegrationEvent(EmpresaBootstrapId, Ahora, devolucion, "DEV-FIX", nota,
                "Autorizada", 232m, "MXN", null, null, null)));
            (await PolizasAsync(devolucion)).Should().BeEmpty();
            (await RechazoAsync(eventos[^1])).Should().BeNull();

            eventos.Add(await Tesoreria(new ReembolsoNotaCreditoActualizadoIntegrationEvent(EmpresaBootstrapId, Ahora, devolucion, "DEV-FIX", nota,
                "Pagada", 232m, "MXN", Hoy(), "SPEI-FIX", null)));
            (await RechazoAsync(eventos[^1])).Should().BeNull();
            var dv = (await PolizasAsync(devolucion)).Should().ContainSingle().Subject;
            dv.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CLIENTES"] && x.Cargo == 232m && x.ClienteId == e.Clientes[0]);
            dv.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Abono == 232m);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                foreach (var id in eventos)
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE evento_id = {0}", id);
            }
            await LimpiarAsync(e);
        }
    }

    [Theory]
    [InlineData("consumo", "CONSUMO")]
    [InlineData("rotura", "MERMA")]
    [InlineData("salida_no_planificada", "AJUSTE_INVENTARIO")]
    public async Task U2_la_salida_sin_requisicion_de_G1_carga_segun_su_causa_contra_inventario(string causa, string rol)
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var movimiento = Guid.NewGuid();
            await Almacen(new SalidaInventarioRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, movimiento, "SAL-FIX-G1", Hoy(), causa, "FIX",
                Guid.NewGuid(), e.Sucursal, null, 300m, [new LineaSalidaInventarioPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 3m, 100m, 300m)]));

            var sm = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            sm.TipoClave.Should().Be("SM");
            sm.Partidas.Should().HaveCount(2);
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas[rol] && x.Cargo == 300m);
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["INVENTARIO"] && x.Abono == 300m);
        }
        finally { await LimpiarAsync(e); }
    }

    // ---------------------------------------------------------------- H1 · reglas de dimensión de K10.2 (C-03)

    /// <summary>
    /// H1 (QA de U-2): con la regla C-03 activa (cuentas de gasto piden ubicación y área), el costo de importación y la salida G-1 sin
    /// centro de costo se quedan en la bandeja con «Dato faltante»; la salida con el centro del vale (equipo, de ahí se derivan área y
    /// ubicación) sí genera su póliza. Las reglas cuelgan de la cuenta padre de gastos del escenario y se borran al final.
    /// </summary>
    [Fact]
    public async Task U2_H1_con_la_regla_C03_activa_lo_que_pega_en_gastos_sin_centro_queda_en_la_bandeja()
    {
        Escenario? e = null;
        Guid? gastos = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Id == e.Cuentas["MERMA"]).Select(c => c.PadreId).SingleAsync();
            }
            foreach (var dim in new[] { "Dim1", "Dim2" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = gastos, tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (H1)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            // Costo de importación: la parte vendida pega en gasto (GASTO_IMPORTACION) y el evento no trae centro.
            var expediente = Guid.NewGuid();
            var eventoCosto = await Almacen(new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, expediente, 1,
                [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, 8_000m, 2_000m)])
                { SucursalId = e.Sucursal });
            (await PolizasAsync(expediente)).Should().BeEmpty();
            (await RechazoAsync(eventoCosto))!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);

            // Salida G-1 por rotura sin centro: MERMA es gasto → bandeja.
            var sinCentro = Guid.NewGuid();
            var eventoSinCentro = await Almacen(new SalidaInventarioRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, sinCentro, "SAL-FIX-H1A", Hoy(),
                "rotura", "FIX", Guid.NewGuid(), e.Sucursal, null, 300m, [new LineaSalidaInventarioPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 3m, 100m, 300m)]));
            (await PolizasAsync(sinCentro)).Should().BeEmpty();
            (await RechazoAsync(eventoSinCentro))!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);

            // La misma salida con el centro del vale (equipo): el validador deriva área y ubicación y la póliza nace.
            var conCentro = Guid.NewGuid();
            var eventoConCentro = await Almacen(new SalidaInventarioRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, conCentro, "SAL-FIX-H1B", Hoy(),
                "rotura", "FIX", Guid.NewGuid(), e.Sucursal, e.CentroCosto, 300m,
                [new LineaSalidaInventarioPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 3m, 100m, 300m, e.CentroCosto)]));
            (await RechazoAsync(eventoConCentro)).Should().BeNull();
            var sm = (await PolizasAsync(conCentro)).Should().ContainSingle().Subject;
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["MERMA"] && x.Cargo == 300m);
        }
        finally
        {
            if (gastos is { } padre)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE cuenta_id = {0})", padre);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id = {0}", padre);
            }
            await LimpiarAsync(e);
        }
    }

    /// <summary>
    /// H1 paso 1 (86e3z5w25): salidas de la app de Almacenes. Con la regla C-03 activa en gastos (ubicación y área), el puerto que usa
    /// el lector de <c>alm_mensaje</c> dice que la merma exige área; resuelve el área o el equipo por clave dentro de la sucursal y
    /// rechaza una clave que no existe o un equipo de otra área. Con solo el área (Dim2) la póliza nace: la ubicación se deriva.
    /// </summary>
    [Fact]
    public async Task H1_salida_de_la_app_con_area_o_equipo_de_la_sucursal_genera_su_poliza()
    {
        Escenario? e = null;
        Guid? gastos = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Id == e.Cuentas["MERMA"]).Select(c => c.PadreId).SingleAsync();
            }
            foreach (var dim in new[] { "Dim1", "Dim2" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = gastos, tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (H1 salidas)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            Guid area;
            using (var scope = factory.Services.CreateScope())
            {
                var centros = scope.ServiceProvider.GetRequiredService<Millet.Contabilidad.Application.Ports.ICentroCostoContabilidadPort>();
                var equipo = (await centros.ObtenerAsync([e.CentroCosto], default))[e.CentroCosto];
                area = equipo.Dim2Id!.Value;
                var puerto = scope.ServiceProvider.GetRequiredService<Millet.Contabilidad.Application.PublicPorts.IDimensionesSalidaInventarioPort>();
                Task<Millet.Contabilidad.Application.PublicPorts.DimensionesSalidaResultado> R(string? a, string? q) => puerto.ResolverAsync(
                    new(EmpresaBootstrapId, e.Sucursal, "rotura", Hoy(), a, q), default);

                var sinNada = await R(null, null);
                sinNada.Error.Should().BeNull();
                sinNada.ExigeArea.Should().BeTrue();
                sinNada.ExigeEquipo.Should().BeFalse();
                sinNada.Faltante.Should().Contain("el área");
                sinNada.CentroCostoId.Should().BeNull();

                var conArea = await R(equipo.Dim2Clave, null);
                conArea.Should().Match<Millet.Contabilidad.Application.PublicPorts.DimensionesSalidaResultado>(x =>
                    x.Error == null && x.Faltante == null && x.CentroCostoId == area);

                var conEquipo = await R(null, equipo.Clave);
                conEquipo.Should().Match<Millet.Contabilidad.Application.PublicPorts.DimensionesSalidaResultado>(x =>
                    x.Error == null && x.Faltante == null && x.CentroCostoId == e.CentroCosto);

                (await R("NO-EXISTE-H1", null)).Error.Should().Contain("NO-EXISTE-H1");
                (await R(equipo.Dim2Clave, "NO-EXISTE-H1")).Error.Should().Contain("NO-EXISTE-H1");
            }

            // La salida con solo el área: la póliza nace con la ubicación derivada (Dim1) y el área (Dim2).
            var movimiento = Guid.NewGuid();
            var evento = await Almacen(new SalidaInventarioRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, movimiento, "SAL-FIX-H1C", Hoy(),
                "rotura", "FIX", Guid.NewGuid(), e.Sucursal, area, 300m,
                [new LineaSalidaInventarioPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 3m, 100m, 300m, area)]));
            (await RechazoAsync(evento)).Should().BeNull();
            var sm = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["MERMA"] && x.Cargo == 300m && x.Dim2Id == area && x.Dim1Id != null);
        }
        finally
        {
            if (gastos is { } padre)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE cuenta_id = {0})", padre);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id = {0}", padre);
            }
            await LimpiarAsync(e);
        }
    }
}
