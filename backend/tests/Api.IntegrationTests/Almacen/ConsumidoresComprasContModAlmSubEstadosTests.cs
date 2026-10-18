using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// V-10 (86e3pc50v): pruebas de integración del consumidor
/// <c>almacen.oc_recepcion.registrada.v1</c> sobre sub-estados, cierre y
/// trazabilidad a requisición, contra PostgreSQL real.
/// <list type="bullet">
///   <item>M-031: variante B (<c>FacturaPendiente=true</c>) suma recibido y no toca facturación.</item>
///   <item>M-041: cierre automático y <see cref="OrdenCompraCerradaEvent"/> solo con las 3 dimensiones Completas.</item>
///   <item>M-047: recepción sobre OC Cancelada se rechaza sin cambiar lo recibido.</item>
///   <item>M-099: dos recepciones parciales (30 y 20) dejan 50 en la línea de RQ; el reproceso no duplica.</item>
/// </list>
/// Patrón de siembra y ejecución idéntico a <c>ConsumidoresComprasContModAlmTests</c>.
/// </summary>
public sealed class ConsumidoresComprasContModAlmSubEstadosTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task M031_variante_B_FacturaPendiente_suma_recibido_y_no_cambia_sub_estado_facturacion()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            await EnviarAsync(factory, eventoId, Payload(ocId, facturaPendiente: true,
                Linea(lineas[0], articulo, 10m, acumulado: 10m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(10m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);
            Assert.Equal(SubEstadoFacturacion.SinFactura, oc.SubEstadoFacturacion);
            Assert.Equal(0m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadFacturada);
            Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
            Assert.Equal(1, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task M041_recepcion_completa_con_facturacion_y_pago_completos_cierra_la_oc_y_emite_OrdenCompraCerradaEvent()
    {
        var articulo = Guid.NewGuid();
        var captura = new CapturaCerrada();
        var (ocId, lineas) = await SembrarOcAsync(articulo, facturadaYPagada: true);
        var eventoId = Guid.NewGuid();
        using var host = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<INotificationHandler<OrdenCompraCerradaEvent>>(captura)));
        try
        {
            await EnviarAsync(host, eventoId, Payload(ocId, facturaPendiente: false,
                Linea(lineas[0], articulo, 100m, acumulado: 100m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
            Assert.Equal(SubEstadoFacturacion.Completa, oc.SubEstadoFacturacion);
            Assert.Equal(SubEstadoPago.Pagada, oc.SubEstadoPago);
            Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
            Assert.NotNull(oc.FechaCierre);
            var cerrada = Assert.Single(captura.Recibidos);
            Assert.Equal(ocId, cerrada.OrdenCompraId);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task M041_solo_recepcion_completa_no_cierra_la_oc_ni_emite_evento()
    {
        var articulo = Guid.NewGuid();
        var captura = new CapturaCerrada();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        using var host = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<INotificationHandler<OrdenCompraCerradaEvent>>(captura)));
        try
        {
            await EnviarAsync(host, eventoId, Payload(ocId, facturaPendiente: false,
                Linea(lineas[0], articulo, 100m, acumulado: 100m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
            Assert.Equal(SubEstadoFacturacion.SinFactura, oc.SubEstadoFacturacion);
            Assert.Equal(SubEstadoPago.SinPago, oc.SubEstadoPago);
            Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
            Assert.Null(oc.FechaCierre);
            Assert.Empty(captura.Recibidos);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task M047_recepcion_sobre_oc_cancelada_se_rechaza_sin_cambiar_recibido()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo, estado: EstadoOrdenCompra.Cancelada);
        var eventoId = Guid.NewGuid();
        try
        {
            // Regla en OrdenCompra.ValidarEstadoParaRegistrarMovimiento: solo Autorizada o Cerrada aceptan movimientos.
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => EnviarAsync(factory, eventoId, Payload(ocId, facturaPendiente: false,
                    Linea(lineas[0], articulo, 10m, acumulado: 10m))));

            Assert.Equal("OC_MOVIMIENTO_ESTADO_INVALIDO", ex.Code);
            var oc = await LeerOcAsync(ocId);
            Assert.Equal(EstadoOrdenCompra.Cancelada, oc.Estado);
            Assert.Equal(0m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(0, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task M099_recepciones_parciales_30_y_20_dejan_50_en_la_linea_de_requisicion_y_el_reproceso_no_duplica()
    {
        var articulo = Guid.NewGuid();
        var (rqId, rqLineaId) = await SembrarRequisicionEnSurtidoAsync(articulo, cantidad: 100m);
        var (ocId, lineas) = await SembrarOcAsync(articulo, rqId: rqId, rqLineaId: rqLineaId);
        var eventoA = Guid.NewGuid();
        var eventoB = Guid.NewGuid();
        try
        {
            var payloadB = Payload(ocId, facturaPendiente: false, Linea(lineas[0], articulo, 20m, acumulado: 50m));
            await EnviarAsync(factory, eventoA, Payload(ocId, facturaPendiente: false,
                Linea(lineas[0], articulo, 30m, acumulado: 30m)));
            await EnviarAsync(factory, eventoB, payloadB);
            // Reproceso del mismo EventoId: no debe volver a sumar en la requisición.
            await EnviarAsync(factory, eventoB, payloadB);

            var rq = await LeerRequisicionAsync(rqId);
            Assert.Equal(50m, rq.Lineas.Single(l => l.Id == rqLineaId).CantidadRecibida);
            var oc = await LeerOcAsync(ocId);
            Assert.Equal(50m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(1, await ContarEventosAsync(eventoB));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoA, eventoB);
            await LimpiarRequisicionAsync(rqId);
        }
    }

    /// <summary>
    /// Siembra una OC Autorizada con una línea por artículo (100 PZA a 25 MXN).
    /// Con <paramref name="rqId"/> la línea nace de una RQ (vínculo requisición/línea).
    /// <paramref name="facturadaYPagada"/> deja facturación y pago completos ya aplicados
    /// (los aplican otros consumidores; aquí se simulan con los métodos de dominio).
    /// </summary>
    private async Task<(Guid OcId, Guid[] LineaIds)> SembrarOcAsync(
        Guid articuloId, bool facturadaYPagada = false, EstadoOrdenCompra estado = EstadoOrdenCompra.Autorizada,
        Guid? rqId = null, Guid? rqLineaId = null)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        // Una OC con líneas de RQ exige SinRequisicionPrevia=false (regla de dominio); la de siembra manual usa el fixture P1.
        var oc = rqId is null
            ? P1DocumentosValidosTests.NuevaOc(EstadoOrdenCompra.Autorizada)
            : new OrdenCompra(Guid.NewGuid(), EmpresaBootstrapId,
                Millet.Compras.Domain.Oc.Folio.Parse($"OC-FIX2026-{Random.Shared.Next(100000, 999999)}"), 2026,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 10, 9));
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Borrador);
        var lineaId = Guid.NewGuid();
        if (rqId is { } rq && rqLineaId is { } rqLinea)
        {
            oc.AgregarLineaDesdeRequisicion(lineaId, articuloId, 100m, "PZA", 25m, Guid.NewGuid(),
                requisicionId: rq, lineaRequisicionId: rqLinea);
        }
        else
        {
            oc.AgregarLineaManual(lineaId, articuloId, 100m, "PZA", 25m, Guid.NewGuid());
        }
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Autorizada);
        if (facturadaYPagada)
        {
            var ahora = DateTimeOffset.UtcNow;
            oc.RegistrarFacturacionLinea(lineaId, 100m, ahora);
            oc.RegistrarPago(oc.CalcularTotales().TotalAPagar, ahora);
        }
        if (estado != EstadoOrdenCompra.Autorizada)
        {
            typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, estado);
        }
        compras.OrdenesCompra.Add(oc);
        await compras.SaveChangesAsync();
        return (oc.Id, [lineaId]);
    }

    /// <summary>Siembra una RQ en EnSurtido con una línea de <paramref name="cantidad"/> unidades.</summary>
    private async Task<(Guid RqId, Guid RqLineaId)> SembrarRequisicionEnSurtidoAsync(Guid articuloId, decimal cantidad)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var rq = new Requisicion(
            id: Guid.NewGuid(),
            empresaId: EmpresaBootstrapId,
            folio: Millet.Compras.Domain.Folio.Parse($"MID2026-{Random.Shared.Next(100000, 999999)}"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.NewGuid(),
            departamentoId: Guid.NewGuid(),
            almacenDestinoId: Guid.NewGuid(),
            requisitanteId: Guid.NewGuid(),
            creadorId: Guid.NewGuid(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow);
        var lineaId = Guid.NewGuid();
        rq.AgregarLinea(
            lineaId: lineaId,
            articuloId: articuloId,
            cantidad: cantidad,
            unidadMedida: "PZA",
            precioEstimado: Money.Mxn(25m));
        // Cubrimiento de compra total (la RQ queda con la cantidad por comprar; la recepción la consume).
        // Se asigna por reflexión: RegistrarCubrimiento exige un flujo completo de Autorizada.
        typeof(LineaRequisicion).GetProperty(nameof(LineaRequisicion.CantidadDeCompra))!.SetValue(rq.Lineas.Single(), cantidad);
        typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!.SetValue(rq, EstadoRequisicion.EnSurtido);
        compras.Requisiciones.Add(rq);
        await compras.SaveChangesAsync();
        return (rq.Id, lineaId);
    }

    private static async Task EnviarAsync(WebApplicationFactory<Program> host, Guid eventoId, OcRecepcionRegistradaAlmacenPayload payload)
    {
        using var scope = host.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(new OcRecepcionEnAlmacenCommand(eventoId, payload));
    }

    private async Task<OrdenCompra> LeerOcAsync(Guid ocId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await compras.OrdenesCompra
            .AsNoTracking()
            .Include(o => o.Lineas)
            .FirstAsync(o => o.Id == ocId);
    }

    private async Task<Requisicion> LeerRequisicionAsync(Guid rqId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await compras.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .FirstAsync(r => r.Id == rqId);
    }

    private async Task<int> ContarEventosAsync(Guid eventoId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await compras.EventosProcesados
            .IgnoreQueryFilters()
            .CountAsync(e => e.EventoId == eventoId && e.EventoTipo == OcRecepcionEnAlmacenHandler.EventType);
    }

    private async Task LimpiarAsync(Guid ocId, params Guid[] eventoIds)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await compras.EventosProcesados
            .IgnoreQueryFilters()
            .Where(e => eventoIds.Contains(e.EventoId))
            .ExecuteDeleteAsync();
        await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.ordenes_compra WHERE id = {ocId}");
    }

    private async Task LimpiarRequisicionAsync(Guid rqId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var rq = await compras.Requisiciones.Include(r => r.Lineas).FirstOrDefaultAsync(r => r.Id == rqId);
        if (rq is not null)
        {
            compras.Requisiciones.Remove(rq);
            await compras.SaveChangesAsync();
        }
    }

    private static OcRecepcionRegistradaAlmacenPayload Payload(
        Guid ocId, bool facturaPendiente, params LineaRecepcionAlmacenPayload[] lineas) =>
        new(EmpresaBootstrapId, DateTimeOffset.UtcNow, Guid.NewGuid(), "FIX-V10", ocId,
            new DateOnly(2026, 10, 9), facturaPendiente, null, null, lineas);

    private static LineaRecepcionAlmacenPayload Linea(
        Guid? lineaOcId, Guid articuloId, decimal cantidad, decimal? acumulado = null) =>
        new(Guid.NewGuid(), lineaOcId, articuloId, "PZA", cantidad, 25m, cantidad * 25m, 0m, acumulado);

    /// <summary>Captura los <see cref="OrdenCompraCerradaEvent"/> que publica el handler por MediatR.</summary>
    private sealed class CapturaCerrada : INotificationHandler<OrdenCompraCerradaEvent>
    {
        public List<OrdenCompraCerradaEvent> Recibidos { get; } = [];

        public Task Handle(OrdenCompraCerradaEvent notification, CancellationToken cancellationToken)
        {
            Recibidos.Add(notification);
            return Task.CompletedTask;
        }
    }
}
