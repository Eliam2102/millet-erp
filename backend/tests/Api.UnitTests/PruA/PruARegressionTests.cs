using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Application.Series;
using Millet.Api.Web;
using Millet.Compras.Application.Oc.Eventos;
using Millet.Compras.Application.Oc.Eventos.Cxp;
using Millet.Compras.Domain.Oc;
using NivelAutorizacion = Millet.Compras.Domain.NivelAutorizacion;
using Millet.Compras.Domain.Ports.Tesoreria;
using Millet.Compras.Infrastructure;
using Millet.Contabilidad.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.DatosMaestros;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.PublicAdapters;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Application.NotaCargo;
using Millet.CuentasPorPagar.Application.NotaCreditoProveedor.CapturarNotaCredito;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Pendientes;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.PagosACuenta;
using Millet.Tesoreria.Application.Repp;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure;
using Millet.Tesoreria.Infrastructure.Persistence;
using CxpFactura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.Tests.PruA;

// La misma regresión corre en memoria y enlazada contra PostgreSQL desechable.
// No inicia API, no usa login simulado ni modifica catálogos compartidos.
public sealed class PruARegressionTests
{
    private static readonly decimal[] EsperadosSaldados = [300m, 500m, 1160m];
    [Fact]
    public async Task TES03_periodo_real_rechaza_ingreso_pago_pago_a_cuenta_y_reversa_y_octubre_pasa()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var cuenta = await a.CuentaAsync(); var facturaId = Guid.NewGuid();
        a.Tes.PasivosPendientesPago.Add(new PasivoPendientePago(a.Empresa, facturaId, a.Proveedor, null,
            1000, 1000, "MXN", null, new(2026, 10, 15), null, "FIX-PRUA", a.UtcNow, "PPD"));
        await a.Tes.SaveChangesAsync();
        var ingreso = new RegistrarMovimientoIngresoHandler(a.Tes, a, a, a.PeriodoTes, a);
        var pago = new RegistrarPagoProveedorHandler(a.Tes, a, a, a.PeriodoTes, a, a, a.Eventos, a);
        var cuentaPago = new RegistrarPagoACuentaHandler(a.Tes, a, a, a.PeriodoTes, a);
        var septiembre = new DateOnly(2026, 9, 30); var octubre = new DateOnly(2026, 10, 10);
        await Cerrado(() => ingreso.Handle(new(cuenta.Id, 100, septiembre, SucursalId: a.Sucursal), default));
        await Cerrado(() => pago.Handle(new(cuenta.Id, septiembre, [new(facturaId, 500)]), default));
        await Cerrado(() => cuentaPago.Handle(new(cuenta.Id, 100, septiembre, "FIX PRU-A", a.Proveedor), default));
        Assert.Empty(await a.Tes.MovimientosBancarios.ToListAsync());
        await ingreso.Handle(new(cuenta.Id, 100, octubre, SucursalId: a.Sucursal), default);
        var registrado = await pago.Handle(new(cuenta.Id, octubre, [new(facturaId, 500)]), default);
        await cuentaPago.Handle(new(cuenta.Id, 100, octubre, "FIX PRU-A", a.Proveedor), default);
        var reversa = new RevertirPagoProveedorHandler(a.Tes, a, a, a.PeriodoTes, a.Eventos, a);
        a.UtcNow = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        await Cerrado(() => reversa.Handle(new(registrado.Aplicaciones.Single().PagoId, "FIX corrección"), default));
        Assert.False((await a.Tes.AplicacionesPagoProveedor.SingleAsync()).Revertida);
        a.UtcNow = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);
        await reversa.Handle(new(registrado.Aplicaciones.Single().PagoId, "FIX corrección"), default);
        Assert.True((await a.Tes.AplicacionesPagoProveedor.SingleAsync()).Revertida);
    }

    [Fact]
    public async Task FAC13_REP_manual_y_pendiente_rechazan_septiembre_cerrado_y_octubre_timbra()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var factura = await a.FacturaVentaAsync(100);
        // 23:30 de septiembre en México ya es octubre en UTC: sigue siendo período 09.
        var septiembre = new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.FromHours(-6));
        await Cerrado(() => a.EmisorRep.Handle(a.CommandRep(factura.Id, septiembre.ToUniversalTime(), 100), default));
        var pendiente = await a.PendienteAsync(factura, new(2026, 9, 30), 100);
        var fallido = await a.EmitirPendiente.Handle(new(pendiente.Id), default);
        Assert.False(fallido.Emitido); Assert.Contains("2026/09", fallido.Mensaje);
        Assert.Null(fallido.ReciboPagoId); Assert.Equal(0, a.Pac.Llamadas);
        var octubre = await a.EmisorRep.Handle(a.CommandRep(factura.Id, new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-6)), 100), default);
        Assert.Equal("Timbrado", octubre.Estado);
        a.Fac.ChangeTracker.Clear();
        Assert.Equal(TimeSpan.Zero, (await a.Fac.RecibosPago.SingleAsync()).FechaPago.Offset);
    }

    [Fact]
    public async Task CXP12_captura_NC_con_offset_Mexico_y_persiste_el_mismo_instante_en_UTC()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var factura = a.FacturaProveedor(); a.Cxp.FacturasProveedor.Add(factura); await a.Cxp.SaveChangesAsync();
        var fecha = new DateTimeOffset(2026, 10, 10, 10, 12, 30, TimeSpan.FromHours(-6));
        var command = new CapturarNotaCreditoCommand(null, Guid.NewGuid().ToString(), a.Proveedor, "FIX-NC", "FIX", fecha,
            "MXN", null, 100, 0, 0, 100, TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, factura.UuidCfdi!);
        var r = await new CapturarNotaCreditoHandler(a.Cxp, a, a, a.Mediator, a, a, a.Blobs, new XmlCfdiParser()).Handle(command, default);
        a.Cxp.ChangeTracker.Clear(); var nc = await a.Cxp.NotasCreditoProveedor.SingleAsync(n => n.Id == r.Id);
        Assert.Equal(TimeSpan.Zero, nc.FechaCfdi.Offset); Assert.Equal(fecha.ToUniversalTime(), nc.FechaCfdi);
        Assert.Equal(factura.Id, nc.FacturaOrigenId);
    }

    [Theory]
    [InlineData("MXN")]
    [InlineData("USD")]
    public async Task CXC09_REP_desde_pendientes_pasa_mapping_SDK_de_dos_decimales_y_libera_saldo(string moneda)
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var factura = await a.FacturaVentaAsync(1000m, moneda);
        var pendiente = await a.PendienteAsync(factura, new(2026, 10, 10), 120.123456m);
        var r = await a.EmitirPendiente.Handle(new(pendiente.Id), default);
        Assert.True(r.Emitido, r.Mensaje); Assert.NotNull(r.ReciboPagoId);
        var sdk = Assert.IsType<Fiscalapi.Models.Invoice>(a.Pac.Invoice);
        var p = sdk.Complement!.Payment!;
        Assert.Equal(120.12m, p.Amount); Assert.Equal(p.Amount, p.PaidInvoices!.Sum(d => d.PaymentAmount));
        foreach (var d in p.PaidInvoices!) { Assert.Equal(879.88m, d.RemainingBalance); Assert.Equal(d.PreviousBalance - d.PaymentAmount, d.RemainingBalance); }
        Assert.Equal(new DateTime(2026, 10, 10), p.PaymentDate);
        var resto = await a.PendienteAsync(factura, new(2026, 10, 11), 879.876544m);
        Assert.True((await a.EmitirPendiente.Handle(new(resto.Id), default)).Emitido);
        Assert.Equal(0m, a.Pac.Invoice!.Complement!.Payment!.PaidInvoices!.Single().RemainingBalance);
        a.Fac.ChangeTracker.Clear();
        Assert.All(await a.Fac.ReppPendientes.ToListAsync(), p => Assert.Equal(EstadoReppPendiente.Emitido, p.Estado));
        Assert.Equal(0m, factura.Total - (await SaldoPorCobrar.PagosVigentes(a.Fac).Where(f => f.FacturaVentaId == factura.Id).ToListAsync()).Sum(f => f.ImportePagado));
        Assert.Contains(await a.Fac.OutboxEntries.ToListAsync(), e => e.EventType == "facturacion.recibo-pago.timbrado.v1");
    }

    [Fact]
    public async Task CXC04_ingreso_de_otro_usuario_de_MID_es_visible_y_operativo_no_accede_a_otra_sucursal()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync(); var cuenta = await a.CuentaAsync();
        var handler = new RegistrarMovimientoIngresoHandler(a.Tes, a, a, a.PeriodoTes, a);
        var creador = a.UserId;
        var ingreso = await handler.Handle(new(cuenta.Id, 100, new(2026, 10, 10), SucursalId: a.Sucursal), default);
        a.UserId = Guid.NewGuid(); Assert.NotEqual(creador, a.UserId);
        var cuentas = await new Millet.Tesoreria.Application.Cuentas.ListarCuentasParaIngresosHandler(a.Tes)
            .Handle(new(), default);
        var cuentaDisponible = Assert.Single(cuentas);
        Assert.Equal(cuenta.Id, cuentaDisponible.Id);
        Assert.NotEqual(cuenta.NumeroCuenta, cuentaDisponible.NumeroCuenta);
        Assert.DoesNotContain(typeof(Millet.Tesoreria.Application.Cuentas.CuentaParaIngresoResponse).GetProperties(), p => p.Name == "Saldo");
        var port = new TesoreriaSucursalReadAdapter(a.Tes, a, a, a);
        var scope = new DocumentoSucursalScope(a, a, a, a, a, a, port, a, a);
        await scope.VerificarAsync("movimiento_bancario", ingreso.Id, "tesoreria.documentos.gestionar-todas-sucursales", default);
        var doc = (await port.ListarAsync("movimiento_bancario", default)).Single(d => d.Id == ingreso.Id);
        Assert.True(DocumentoSucursalScope.Permitido(doc, [a.Sucursal]));
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.VerificarSucursalAsync(Guid.NewGuid(), "tesoreria.documentos.gestionar-todas-sucursales", default));
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.VerificarSucursalAsync(null, "tesoreria.documentos.gestionar-todas-sucursales", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CXC04_alta_rapida_y_confirmacion_son_atomicas_incluso_si_proponente_intenta_confirmar(bool mismoUsuario)
    {
        await using var a = new Ambiente(); await a.CalendarioAsync(); var cuenta = await a.CuentaAsync();
        var deposito = DepositoConfirmacion.CrearDesdePropuesta(a.Empresa, Guid.NewGuid(), a.Cliente, "FIX PRU-A", 100,
            "MXN", "[]", mismoUsuario ? a.UserId!.Value : Guid.NewGuid(), 0);
        a.Tes.DepositosConfirmacion.Add(deposito); await a.Tes.SaveChangesAsync();
        var handler = new ConfirmarDepositoHandler(a.Tes, a, a, a.Eventos, a, a.PeriodoTes);
        var cmd = new ConfirmarDepositoCommand(deposito.Id, Guid.Empty, deposito.Version,
            new(cuenta.Id, 100, new(2026, 10, 10), SucursalId: a.Sucursal));
        if (mismoUsuario)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(cmd, default));
            Assert.Equal("DEP_MISMO_USUARIO", error.Code);
            a.Tes.ChangeTracker.Clear();
            // Solo lo de esta prueba: la base es compartida con otras pruebas.
            Assert.Empty(await a.Tes.MovimientosBancarios.Where(m => m.CuentaBancariaId == cuenta.Id).ToListAsync());
            Assert.DoesNotContain(await a.Tes.OutboxEntries.ToListAsync(), e => e.EventType == "tesoreria.pago-cliente.confirmado.v1" && e.Payload.Contains(deposito.Id.ToString()));
            Assert.Equal(EstadoDepositoConfirmacion.Pendiente, (await a.Tes.DepositosConfirmacion.SingleAsync()).Estado);
        }
        else
        {
            var confirmado = await handler.Handle(cmd, default); a.Tes.ChangeTracker.Clear();
            Assert.Equal(EstadoDepositoConfirmacion.Confirmada, confirmado.Estado);
            Assert.Equal(a.Sucursal, (await a.Tes.MovimientosBancarios.SingleAsync()).SucursalId);
            Assert.Contains(await a.Tes.OutboxEntries.ToListAsync(), e => e.EventType == "tesoreria.pago-cliente.confirmado.v1");
        }
    }

    [Fact]
    public async Task TES15_tesorero_MID_vincula_XML_de_REP_sin_permiso_CxP_y_no_ve_MTY_ni_XML_mixto()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var mid = a.FacturaProveedor(); var mty = a.FacturaProveedor(sucursal: Guid.NewGuid());
        a.Cxp.FacturasProveedor.AddRange(mid, mty); await a.Cxp.SaveChangesAsync();
        var permitido = await a.CfdiPagoAsync([mid.UuidCfdi!]);
        await a.CfdiPagoAsync([mty.UuidCfdi!]); await a.CfdiPagoAsync([mid.UuidCfdi!, mty.UuidCfdi!]);
        Assert.False(await a.TieneAsync("cuentas_por_pagar.cfdis.ver"));
        var handler = new ListarReppParaTesoreriaHandler(a.Cxp, a.Blobs, a, a, a);
        var cfdis = await handler.Handle(new(mid.Id), default); Assert.Equal(permitido.Id, Assert.Single(cfdis).Id);
        await using var xml = await a.Blobs.LeerXmlAsync(permitido.XmlBlobRef!, default);
        Assert.NotNull(xml);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(mty.Id), default));
        var cuenta = await a.CuentaAsync();
        a.Tes.PasivosPendientesPago.Add(new PasivoPendientePago(a.Empresa, mid.Id, a.Proveedor, null, 1000, 1000,
            "MXN", null, new(2026, 10, 30), Guid.Parse(mid.UuidCfdi!), "FIX", a.UtcNow, "PPD"));
        await a.Tes.SaveChangesAsync();
        var pago = await new RegistrarPagoProveedorHandler(a.Tes, a, a, a.PeriodoTes, a, a, a.Eventos, a)
            .Handle(new(cuenta.Id, new(2026, 10, 10), [new(mid.Id, 500)]), default);
        var registrado = await new RegistrarReppRecibidoHandler(a.Tes, a, a, a.Blobs, a.Eventos, a, a)
            .Handle(new(mid.Id, Guid.Parse(permitido.UuidCfdi.Valor), new(2026, 10, 11),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(a.Blobs.Xml[permitido.XmlBlobRef!])), [new(pago.Aplicaciones.Single().PagoId, 500)]), default);
        Assert.NotNull(registrado.XmlBlobRef);
        a.Tes.ChangeTracker.Clear(); Assert.Equal(permitido.UuidCfdi.Valor, (await a.Tes.ReppsProveedorRecibidos.SingleAsync()).UuidComplemento.ToString().ToUpperInvariant());
        Assert.Contains(await a.Tes.OutboxEntries.ToListAsync(), e => e.EventType == "tesoreria.repp-proveedor.recibido.v1");
    }

    [Fact]
    public async Task TES16_anticipo_cargo_y_pago_publican_acumulado_en_Outbox_y_Compras_cierra_sin_duplicar_aplicaciones()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var oc = new OrdenCompra(Guid.NewGuid(), a.Empresa, Folio.Parse("OC-FIX2026-000102"), 2026, a.Proveedor, a.Sucursal,
            Guid.NewGuid(), Guid.NewGuid(), a.UserId!.Value, Guid.NewGuid(), new(2026, 10, 10), sinRequisicionPrevia: true, motivoSinRequisicion: "FIX PRU-A");
        var linea = oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 10, "H87", 100, Guid.NewGuid(), descuento: DescuentoLinea.Cero, indicadorImpuestos: IndicadorImpuestos.Iva16Default);
        oc.EnviarAAutorizacion(a.UtcNow); oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), a.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), a.UtcNow);
        oc.RegistrarRecepcionLinea(linea.Id, 10, a.UtcNow); oc.RegistrarFacturacionLinea(linea.Id, 10, a.UtcNow);
        a.Compras.OrdenesCompra.Add(oc); await a.Compras.SaveChangesAsync();
        var f = a.FacturaProveedor(oc: oc.Id, total: 1160); a.Cxp.FacturasProveedor.Add(f);
        var anticipo = AnticipoProveedor.Capturar(a.Empresa, null, Guid.NewGuid().ToString(), a.Proveedor, "FIX", "A", a.UtcNow,
            "MXN", null, 300, oc.Id, a.UserId, a.UtcNow);
        var cargo = NotaCargo.Crear(a.Empresa, FolioInternoNotaCargo.FromAnioSecuencial(2026, 1), a.Proveedor, a.Sucursal,
            "FIX ajuste", null, 200, "MXN", null, f.Id, null, a.UserId, a.UtcNow);
        cargo.Autorizar(a.UserId, a.UtcNow); a.Cxp.AnticiposProveedor.Add(anticipo); a.Cxp.NotasCargo.Add(cargo); await a.Cxp.SaveChangesAsync();
        var mapper = new FacturaPagoAplicadoMapper(a.Eventos, a.Cxp);
        var pasivos = new PasivoAutorizadoParaPagoMapper(a.Eventos, a.Cxp, new ElegibilidadFacturaService(a.Cxp, a));
        await new AplicarAnticipoAFacturaHandler(a.Cxp, a, pasivos, mapper).Handle(new(f.Id, f.Version, anticipo.Id, anticipo.Version, 300), default);
        await new AplicarNotaCargoHandler(a.Cxp, a, a, pasivos, mapper).Handle(new(cargo.Id, cargo.Version), default);
        f.Autorizar(a.UserId, a.UtcNow); await a.Cxp.SaveChangesAsync();
        await new PagoFacturaProveedorAplicadoHandler(a.Cxp, a, a.Mediator, NullLogger<PagoFacturaProveedorAplicadoHandler>.Instance)
            .Handle(new(Guid.NewGuid(), new(a.Empresa, a.UtcNow, f.Id, Guid.NewGuid(), 660, "MXN", new(2026, 10, 10), "FIX pago", null)), default);
        var entries = await a.Cxp.OutboxEntries.Where(e => e.EventType == FacturaPagoAplicadoCommandHandler.EventType).OrderBy(e => e.CreatedAt).ToListAsync();
        var eventos = entries.Select(e => JsonSerializer.Deserialize<FacturaPagoAplicadoIntegrationEvent>(e.Payload)!)
            .Where(e => e.FacturaProveedorId == f.Id) // la base es compartida con otras pruebas
            .OrderBy(e => e.MontoPagadoAcumuladoOc).ToArray();
        Assert.Equal(EsperadosSaldados, eventos.Select(e => e.MontoPagadoAcumuladoOc));
        Assert.Equal(3, eventos.Select(e => e.AplicacionId).Distinct().Count()); Assert.All(eventos, e => Assert.NotNull(e.AplicacionId));
        var consumer = new FacturaPagoAplicadoCommandHandler(a.Compras, a.Mediator, NullLogger<FacturaPagoAplicadoCommandHandler>.Instance);
        foreach (var e in eventos)
        {
            var payload = new FacturaPagoAplicadoPayload(e.EmpresaId, e.OcurridoEn, e.FacturaProveedorId, e.OrdenCompraId, e.ImportePagadoFactura, e.MontoPagadoAcumuladoOc, e.AplicacionId);
            await consumer.Handle(new(Guid.NewGuid(), payload), default);
            await consumer.Handle(new(Guid.NewGuid(), payload), default); // otro sobre, misma aplicación
        }
        a.Compras.ChangeTracker.Clear(); a.Cxp.ChangeTracker.Clear();
        Assert.Equal(0m, (await a.Cxp.FacturasProveedor.SingleAsync()).SaldoPendiente);
        Assert.Equal(EstadoOrdenCompra.Cerrada, await a.Compras.OrdenesCompra.Select(o => o.Estado).SingleAsync());
        var aplicaciones = eventos.Select(e => e.AplicacionId!.Value).ToArray();
        Assert.Equal(3, await a.Compras.EventosProcesados.CountAsync(p => aplicaciones.Contains(p.EventoId)));
    }

    [Fact]
    public async Task FAC15_reintento_usa_emisor_corregido_guarda_mismo_folio_y_lo_timbrado_no_se_modifica()
    {
        await using var a = new Ambiente(); await a.CalendarioAsync();
        var factura = await a.FacturaVentaAsync(100, timbrada: false);
        var empresa = new Millet.Administracion.Domain.Empresa(a.Empresa, $"FIX{Guid.NewGuid().ToString("N")[..8]}", "EKU9003173C9", a.RazonSocial, "601",
            "Calle ficticia", "1", "Centro", "Mérida", "Mérida", "Yucatán", "MEX", codigoPostal: "42501");
        a.Maestros.Empresas.Add(empresa);
        a.Maestros.Sucursales.Add(new Millet.Administracion.Domain.Sucursal(a.Sucursal, a.Empresa, "FIX", "Sucursal ficticia",
            Millet.Administracion.Domain.TipoSucursal.Taller, "Calle ficticia", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "MEX"));
        await a.Maestros.SaveChangesAsync();
        a.Pac.NombreEsperado = "ESCUELA KEMPER URGATE";
        await TimbradoEjecutor.TimbrarYAplicarAsync(a.Fac, factura, CfdiEmisionBuilder.DesdeFacturaVenta(factura, a.UtcNow),
            a.Pac, new FakeCfdiRepositorioPort(), a.UtcNow, default);
        await a.Fac.SaveChangesAsync(); Assert.Equal("CFDI40139", factura.TimbradoErrorCodigo);
        a.Fac.ChangeTracker.Clear(); a.RazonSocial = "ESCUELA KEMPER URGATE";
        empresa.ActualizarDatos(razonSocial: a.RazonSocial); await a.Maestros.SaveChangesAsync();
        var folio = factura.Folio;
        var h = new ReintentarTimbradoHandler(a.Fac, a.Sender, a.PeriodoFac, a.Pac, new FakeCfdiRepositorioPort(), a.Eventos,
            new FakeContabilidadAsientoPort(), a, ReceptorFiscalTestFactory.Crear(a.Fac), empresaFiscal: new EmpresaFiscalReadAdapter(a.Maestros));
        var r = await h.Handle(new(factura.Id), default);
        Assert.Equal(folio, r.Folio); Assert.Equal("Timbrado", r.Estado);
        Assert.Equal(a.RazonSocial, a.Pac.Emision!.Emisor.Nombre);
        a.Fac.ChangeTracker.Clear(); var guardada = await a.Fac.FacturasVenta.SingleAsync();
        Assert.Equal(a.RazonSocial, guardada.NombreEmisor); Assert.Equal(folio, guardada.Folio);
        a.RazonSocial = "Otro nombre";
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(guardada.Id), default));
        a.Fac.ChangeTracker.Clear(); Assert.Equal("ESCUELA KEMPER URGATE", (await a.Fac.FacturasVenta.SingleAsync()).NombreEmisor);
    }

    private static async Task Cerrado(Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<BusinessRuleException>(action); Assert.Contains("El período 2026/09 está cerrado", e.Message);
    }

    private sealed class Ambiente : ICurrentEmpresaContext, ICurrentUserContext, IClock, ICurrentUserPermissions, IUsuarioSucursalReadPort,
        IProveedorReadPort, IProveedorBancoReadPort, IElegibleFacturaReadPort, IComprasOcReadPort, IEmpresaFiscalReadPort,
        IReppBancarioReadPort, ICxpSucursalReadPort, ICxcSucursalReadPort, IFacturacionSucursalReadPort, IComprasSucursalReadPort, IAlmacenSucursalReadPort, IAsyncDisposable
    {
        public Guid Empresa { get; } = Guid.NewGuid(); public Guid Sucursal { get; } = Guid.NewGuid();
        public Guid Cliente { get; } = Guid.NewGuid(); public Guid Proveedor { get; } = Guid.NewGuid();
        public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => throw new NotSupportedException();
        public Guid? UserId { get; set; } = Guid.NewGuid(); public string? UserName => "FIX PRU-A";
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);
        public string RazonSocial { get; set; } = "Empresa ficticia FIX";
        public TesoreriaDbContext Tes { get; } public CuentasPorPagarDbContext Cxp { get; }
        public FacturacionDbContext Fac { get; } public CompartidoDbContext Maestros { get; } public ComprasDbContext Compras { get; } public ContabilidadDbContext Contab { get; }
        public InMemoryIntegrationEventBuffer Buffer { get; } = new();
        public IIntegrationEventPublisher Eventos { get; }
        public FakeSender Sender { get; } = new(new ReservarFolioResponse($"FIX-{Guid.NewGuid():N}", 1, ""));
        public PacConMapping Pac { get; } = new(); public BlobsXml Blobs { get; } = new();
        public IMediator Mediator { get; }
        private readonly ServiceProvider _services;
        private readonly List<IDbContextTransaction> _tx = [];
        public Millet.Tesoreria.Domain.Ports.IPeriodoContablePort PeriodoTes => new Millet.Tesoreria.Infrastructure.Adapters.PeriodoContableAdapter(new PeriodoContableConsultaAdapter(Contab));
        public Millet.Facturacion.Domain.Ports.IPeriodoContablePort PeriodoFac => new Millet.Facturacion.Infrastructure.Adapters.PeriodoContableAdapter(new PeriodoContableConsultaAdapter(Contab));
        public IClientesReadPort Clientes => new FakeClientesReadPort(new(Cliente, "AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "03", "PPD", "MXN", false));
        public EmitirReppHandler EmisorRep => new(Fac, Mediator, PeriodoFac, Pac, new FakeCfdiRepositorioPort(), Eventos, this, this, this, ReceptorFiscalTestFactory.Crear(Fac));
        public EmitirReppPendienteHandler EmitirPendiente => new(Fac, new(Fac, Clientes, new FakeCatalogosSatReadPort(), this), this, Mediator);
        public Ambiente()
        {
            Eventos = new OutboxIntegrationEventPublisher(Buffer, NullLogger<OutboxIntegrationEventPublisher>.Instance);
            Contab = new(Options<ContabilidadDbContext>(), this); Maestros = new(Options<CompartidoDbContext>(), this);
            Tes = new(Options<TesoreriaDbContext>(), this); Cxp = new(Options<CuentasPorPagarDbContext>(), this, new Millet.CuentasPorPagar.Infrastructure.Adapters.PeriodoContableAdapter(new PeriodoContableConsultaAdapter(Contab)), this);
            Fac = new(Options<FacturacionDbContext>(), this); Compras = new(Options<ComprasDbContext>(), this);
#if P3_POSTGRES
            foreach (var db in new DbContext[] { Cxp, Compras, Contab, Maestros }) _tx.Add(db.Database.BeginTransaction());
#else
            foreach (var db in new DbContext[] { Tes, Cxp, Fac, Compras, Contab, Maestros }) db.Database.EnsureCreated();
#endif
            var s = new ServiceCollection();
            s.AddSingleton<IMediator>(sp => new Mediator(sp));
            s.AddSingleton<INotificationHandler<FacturaProveedorPagoAplicadoDomainEvent>>(new FacturaPagoAplicadoMapper(Eventos, Cxp));
            #if P3_POSTGRES
            s.AddSingleton<INotificationHandler<PagoFacturaProveedorEvent>>(sp => new PagoFacturaProveedorListener(Compras, sp.GetRequiredService<IMediator>(), NullLogger<PagoFacturaProveedorListener>.Instance));
#else
            // InMemory no materializa ComplexProperty(DescuentoLinea) en Include (EF10).
            // La integración usa el listener real; aquí aplica el mismo evento al agregado ya cargado.
            s.AddSingleton<INotificationHandler<PagoFacturaProveedorEvent>>(new PagoEnMemoria(Compras));
#endif
            s.AddSingleton<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>>(new FolioPrueba());
            s.AddSingleton<IRequestHandler<EmitirReppCommand, EmitirReppResponse>>(_ => EmisorRep);
            _services = s.BuildServiceProvider(); Mediator = _services.GetRequiredService<IMediator>();
        }
        private DbContextOptions<T> Options<T>() where T : DbContext
        {
            var b = new DbContextOptionsBuilder<T>();
#if P3_POSTGRES
            b.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new InvalidOperationException("Usa PostgreSQL desechable con validate-integration-isolated.sh.")).UseSnakeCaseNamingConvention();
#else
            b.UseInMemoryDatabase(Guid.NewGuid().ToString());
#endif
            b.AddInterceptors(new OutboxSaveChangesInterceptor(Buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance)); return b.Options;
        }
        public async Task CalendarioAsync()
        {
            var ejercicio = new EjercicioContable(Guid.NewGuid(), 2026) { EmpresaId = Empresa }; var periodos = ejercicio.GenerarPeriodos();
            foreach (var p in periodos)
            {
                p.EmpresaId = Empresa;
                if (p.Numero <= 10) p.Abrir(null, "FIX apertura", UserId, "FIX", UtcNow);
                if (p.Numero < 10) p.Cerrar(periodos.Where(x => x.Numero < p.Numero), "FIX cierre", UserId, "FIX", UtcNow);
            }
            Contab.Ejercicios.Add(ejercicio); Contab.Periodos.AddRange(periodos); await Contab.SaveChangesAsync();
        }
        public async Task<CuentaBancaria> CuentaAsync()
        {
            var cuenta = new CuentaBancaria(Empresa, "Banco ficticio", Guid.NewGuid().ToString("N"), null, "MXN");
            Tes.CuentasBancarias.Add(cuenta); await Tes.SaveChangesAsync(); return cuenta;
        }
        public CxpFactura FacturaProveedor(Guid? sucursal = null, Guid? oc = null, decimal total = 1000)
        {
            var uuid = Guid.NewGuid().ToString().ToUpperInvariant(); var s = sucursal ?? Sucursal;
            var subtotal = oc is null ? total : Math.Round(total / 1.16m, 2);
            return oc is Guid id ? CxpFactura.CapturarConOc(Empresa, null, uuid, Proveedor, s, "FIX", null, UtcNow, UtcNow,
                new(2026, 10, 30), "MXN", null, subtotal, 0, total - subtotal, 0, total, id, null, Tolerancia.MontoAbsoluto(0), 0, 0, UtcNow)
                : CxpFactura.CapturarSinOc(Empresa, null, uuid, Proveedor, s, "FIX", null, UtcNow, UtcNow, new(2026, 10, 30), "MXN", null, subtotal, 0, 0, 0, total, "FIX prueba", UtcNow);
        }
        public async Task<FacturaVenta> FacturaVentaAsync(decimal total, string moneda = "MXN", bool timbrada = true)
        {
            var f = FacturaVenta.CrearBorrador(Empresa, $"FIX-{Guid.NewGuid():N}", 1, Sucursal, null, UserId,
                new("AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "MEX", false),
                new("EKU9003173C9", RazonSocial, "601", "42501"), "PPD", "99", moneda, moneda == "MXN" ? null : 20,
                2026, 10, 7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            f.AgregarLinea(null, "01010101", "Prueba ficticia", "H87", 1, total, 0, "02", 0m, null, null); f.RecalcularTotales();
            if (timbrada) { f.MarcarTimbradoEnProceso(); f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, UtcNow, null, null); }
            Fac.FacturasVenta.Add(f); await Fac.SaveChangesAsync(); return f;
        }
        public EmitirReppCommand CommandRep(Guid id, DateTimeOffset fecha, decimal monto) => new(Sucursal, fecha, "MXN", null, "03", null, null, "FIX", [new(id, monto)]);
        public async Task<ReppPendiente> PendienteAsync(FacturaVenta f, DateOnly fecha, decimal monto)
        {
            var p = new ReppPendiente(Empresa, Cliente, Guid.NewGuid(), Guid.NewGuid(), null, monto, f.Moneda, fecha, "FIX", [new(f.Id, monto)]);
            Fac.ReppPendientes.Add(p); await Fac.SaveChangesAsync(); return p;
        }
        public async Task<CfdiRecibido> CfdiPagoAsync(string[] documentos)
        {
            var uuid = Guid.NewGuid().ToString();
            var xml = $"""
                <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:p="http://www.sat.gob.mx/Pagos20" xmlns:t="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="P" Fecha="2026-10-11T12:00:00">
                  <cfdi:Emisor Rfc="AAA010101AAA"/><cfdi:Complemento><t:TimbreFiscalDigital UUID="{uuid}"/>
                    <p:Pagos Version="2.0"><p:Pago FechaPago="2026-10-10T12:00:00" MonedaP="MXN" Monto="500">
                      {string.Join("", documentos.Select(d => $"<p:DoctoRelacionado IdDocumento=\"{d}\" MonedaDR=\"MXN\" ImpPagado=\"500\" NumParcialidad=\"1\" ImpSaldoAnt=\"1000\" ImpSaldoInsoluto=\"500\"/>"))}
                    </p:Pago></p:Pagos>
                  </cfdi:Complemento>
                </cfdi:Comprobante>
                """;
            Blobs.Xml[uuid] = xml;
            var c = CfdiRecibido.Ingresar(Empresa, UuidCfdi.Parse(uuid), RfcMexicano.Parse("AAA010101AAA"), RfcMexicano.Parse("EKU9003173C9"), Millet.CuentasPorPagar.Domain.Cfdi.TipoCfdi.Pago,
                "FIX", "FIX", UtcNow, 0, 0, 0, 0, "XXX", null, CanalOrigenCfdi.CargaManual, UtcNow, uuid, null, new string('a', 64));
            Cxp.CfdisRecibidos.Add(c); await Cxp.SaveChangesAsync(); return c;
        }
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(permiso == "tesoreria.repp.registrar");
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuario, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Sucursal]);
        public Task<bool> EstaAsociadoAsync(Guid usuario, Guid sucursal, CancellationToken ct) => Task.FromResult(sucursal == Sucursal);
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) => Task.FromResult<IReadOnlyList<DocumentoSucursales>>([]);
        Task<ProveedorDto?> IProveedorReadPort.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorDto?>(new(id, "AAA010101AAA", "Proveedor FIX", null, false, true));
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => ((IProveedorReadPort)this).ObtenerAsync(Proveedor, ct);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "Proveedor FIX"));
        Task<ProveedorBancoDto?> IProveedorBancoReadPort.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(new(id, "FIX", "Proveedor FIX", null, null, null, Rfc: "AAA010101AAA"));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
        public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(1000m);
        Task<OrdenCompraDto?> IComprasOcReadPort.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<EmpresaFiscalLectura?>(new(id, "EKU9003173C9", RazonSocial, "601", .16m, "42501"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(Sucursal);
        public Task<Guid> SucursalEmisoraAsync(CancellationToken ct) => Task.FromResult(Sucursal);
        public Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken ct) => Task.FromResult<decimal?>(20m);
        public async ValueTask DisposeAsync()
        {
#if P3_POSTGRES
            // Facturación abre su propia TX al emitir pendientes; limpieza exclusivamente de esta empresa FIX.
            Fac.ChangeTracker.Clear(); Fac.ReppPendientes.RemoveRange(await Fac.ReppPendientes.Include(p => p.Facturas).ToListAsync());
            Fac.RecibosPago.RemoveRange(await Fac.RecibosPago.Include(r => r.FacturasPagadas).ThenInclude(f => f.Impuestos).ToListAsync());
            Fac.FacturasVenta.RemoveRange(await Fac.FacturasVenta.Include(f => f.Lineas).ToListAsync());
            Fac.BitacorasIntentoTimbrado.RemoveRange(await Fac.BitacorasIntentoTimbrado.Where(b => b.EmpresaId == Empresa).ToListAsync());
            await Fac.SaveChangesAsync(); await Fac.OutboxEntries.Where(e => e.IntegrationEmpresaId == Empresa).ExecuteDeleteAsync();
            Tes.ChangeTracker.Clear();
            await Tes.ReppPagosProveedor.ExecuteDeleteAsync(); await Tes.ReppsProveedorRecibidos.ExecuteDeleteAsync();
            await Tes.DepositosConfirmacion.ExecuteDeleteAsync(); await Tes.AplicacionesPagoProveedor.ExecuteDeleteAsync();
            await Tes.MovimientosBancarios.ExecuteDeleteAsync(); await Tes.CuentasBancarias.ExecuteDeleteAsync();
            await Tes.PasivosPendientesPago.ExecuteDeleteAsync(); await Tes.OutboxEntries.Where(e => e.IntegrationEmpresaId == Empresa).ExecuteDeleteAsync();
#endif
            foreach (var tx in _tx) { await tx.RollbackAsync(); await tx.DisposeAsync(); }
            foreach (var db in new DbContext[] { Tes, Cxp, Fac, Compras, Contab, Maestros }) await db.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

#if !P3_POSTGRES
    private sealed class PagoEnMemoria(ComprasDbContext db) : INotificationHandler<PagoFacturaProveedorEvent>
    {
        public async Task Handle(PagoFacturaProveedorEvent notification, CancellationToken cancellationToken)
        {
            db.OrdenesCompra.Local.Single(o => o.Id == notification.OrdenCompraId).RegistrarPago(notification.MontoPagadoAcumulado, notification.OcurridoEn);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
#endif
    private sealed class FolioPrueba : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
    {
        public Task<ReservarFolioResponse> Handle(ReservarFolioCommand request, CancellationToken cancellationToken) =>
            Task.FromResult(new ReservarFolioResponse($"FIX-{Guid.NewGuid():N}", 1, ""));
    }
    private sealed class PacConMapping : ICfdiTimbradoPort
    {
        public Fiscalapi.Models.Invoice? Invoice { get; private set; } public CfdiEmision? Emision { get; private set; } public string? NombreEsperado { get; set; } public int Llamadas { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            Llamadas++; Emision = emision; Invoice = FiscalApiTimbradoAdapter.MapInvoice(emision);
            if (NombreEsperado is not null && emision.Emisor.Nombre != NombreEsperado)
                return Task.FromResult(new TimbradoResultado(TimbradoEstado.Fallido, null, null, null, null, null, null, null, "CFDI40139", "La razón social del emisor no coincide."));
            if (Invoice.Complement?.Payment is { } p)
            {
                Assert.True(Scale(p.Amount) <= 2);
                foreach (var d in p.PaidInvoices!) { Assert.True(Scale(d.PaymentAmount) <= 2); Assert.True(Scale(d.PreviousBalance) <= 2); Assert.True(Scale(d.RemainingBalance) <= 2); }
            }
            return Task.FromResult(new TimbradoResultado(TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<ficticio/>", null, null));
        }
        private static int Scale(decimal valor) => (decimal.GetBits(valor)[3] >> 16) & 0xff;
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class BlobsXml : ICfdiBlobStorage, IReppXmlBlobStorage
    {
        public Dictionary<string, string> Xml { get; } = [];
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fecha, Stream contenido, CancellationToken ct) => throw new NotSupportedException();
        public async Task<string> GuardarXmlAsync(string uuid, DateOnly fecha, Stream contenido, CancellationToken ct)
        {
            using var reader = new StreamReader(contenido); var xml = await reader.ReadToEndAsync(ct);
            var referencia = "TES-" + uuid; Xml[referencia] = xml; return referencia;
        }
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fecha, Stream? contenido, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> LeerXmlAsync(string referencia, CancellationToken ct) => Task.FromResult<Stream?>(Xml.TryGetValue(referencia, out var xml) ? new MemoryStream(Encoding.UTF8.GetBytes(xml)) : null);
        public Task<Stream?> LeerPdfAsync(string referencia, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }
}
