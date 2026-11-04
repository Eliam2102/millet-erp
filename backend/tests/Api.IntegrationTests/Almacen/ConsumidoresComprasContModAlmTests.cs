using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// P-15 (86e3pc50v): consumidor de <c>almacen.oc_recepcion.registrada.v1</c> en Compras.
/// Cubre resolución de línea nula por artículo, fallo visible sin marcar el evento
/// como procesado, acumulado (<c>CantidadRecibidaAcumulada</c>) sin tolerancia en piezas y
/// dedupe por <c>EventoId</c>. Usa PostgreSQL real y OC sembradas con el fixture P1.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ConsumidoresComprasContModAlmTests(WebApplicationFactory<Program> factory)
{
    // 86e3tab1k: en piezas no hay tolerancia. El campo ToleranciaPorcentaje del evento es obsoleto y se ignora.
    [Fact]
    public async Task Acumulado_100_sobre_100_completa_la_linea_e_ignora_la_tolerancia_del_evento()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoId, Payload(ocId, Linea(lineas[0], articulo, 100m, tolerancia: 5m, acumulado: 100m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(100m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
            Assert.Equal(1, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task Acumulado_105_sobre_100_se_rechaza_aunque_el_evento_traiga_tolerancia()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                EnviarAsync(eventoId, Payload(ocId, Linea(lineas[0], articulo, 105m, tolerancia: 5m, acumulado: 105m))));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(0m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(0, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task Linea_sin_LineaOcId_con_articulo_unico_se_resuelve_por_articulo()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoId, Payload(ocId, Linea(null, articulo, 10m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(10m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(1, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task Linea_sin_LineaOcId_con_articulo_repetido_falla_visible_sin_marcar_evento()
    {
        var articulo = Guid.NewGuid();
        var (ocId, _) = await SembrarOcAsync(articulo, articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => EnviarAsync(eventoId, Payload(ocId, Linea(null, articulo, 10m))));

            Assert.Equal("RECEPCION_LINEA_OC_NO_RESOLUBLE", ex.Code);
            Assert.Equal(0, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task Mismo_EventoId_dos_veces_aplica_una_sola_vez()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            var payload = Payload(ocId, Linea(lineas[0], articulo, 10m));
            await EnviarAsync(eventoId, payload);
            await EnviarAsync(eventoId, payload);

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(10m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(1, await ContarEventosAsync(eventoId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoId);
        }
    }

    [Fact]
    public async Task Eventos_con_acumulado_100_y_40_en_orden_inverso_dejan_100()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoA = Guid.NewGuid();
        var eventoB = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoA, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));
            await EnviarAsync(eventoB, Payload(ocId, Linea(lineas[0], articulo, 40m, acumulado: 40m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(100m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoA, eventoB);
        }
    }

    [Fact]
    public async Task Devolucion_30_sobre_recibida_100_deja_70_y_parcial()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoDevolucion = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));
            await EnviarDevolucionAsync(eventoDevolucion, PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 30m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(70m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);
            Assert.Equal(1, await ContarEventosAsync(eventoDevolucion, OcDevolucionEnAlmacenHandler.EventType));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoDevolucion);
        }
    }

    [Fact]
    public async Task Devolucion_usa_CantidadDocumento_en_unidad_de_OC()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoDevolucion = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));
            await EnviarDevolucionAsync(eventoDevolucion, PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 20m, cantidadDocumento: 2m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(98m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoDevolucion);
        }
    }

    [Fact]
    public async Task Dos_devoluciones_30_y_20_sobre_misma_linea_recibida_100_dejan_50()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoDevolucionA = Guid.NewGuid();
        var eventoDevolucionB = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));
            await EnviarDevolucionAsync(eventoDevolucionA, PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 30m)));
            await EnviarDevolucionAsync(eventoDevolucionB, PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 20m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(50m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoDevolucionA, eventoDevolucionB);
        }
    }

    [Fact]
    public async Task Devolucion_linea_nula_con_articulo_repetido_falla_visible_sin_marcar_evento()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo, articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => EnviarDevolucionAsync(eventoId, PayloadDevolucion(ocId, LineaDevolucion(null, articulo, 10m))));

            Assert.Equal("DEVOLUCION_LINEA_OC_NO_RESOLUBLE", ex.Code);
            Assert.Equal(0, await ContarEventosAsync(eventoId, OcDevolucionEnAlmacenHandler.EventType));
            var oc = await LeerOcAsync(ocId);
            Assert.Equal(100m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoId);
        }
    }

    [Fact]
    public async Task Devolucion_mismo_EventoId_dos_veces_descuenta_una_sola_vez()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoDevolucion = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 100m, acumulado: 100m)));
            var payload = PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 30m));
            await EnviarDevolucionAsync(eventoDevolucion, payload);
            await EnviarDevolucionAsync(eventoDevolucion, payload);

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(70m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(1, await ContarEventosAsync(eventoDevolucion, OcDevolucionEnAlmacenHandler.EventType));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoDevolucion);
        }
    }

    // F-01 (recorrido 04-nov): la devolución bajaba la OC (9/10) y la RQ seguía en 10/10.
    [Fact]
    public async Task Devolucion_baja_tambien_lo_recibido_en_la_RQ()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var (rqId, rqLineaId) = await SembrarRqLigadaAsync(ocId, lineas[0], articulo);
        var eventoRecepcion = Guid.NewGuid();
        var eventoDevolucion = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoRecepcion, Payload(ocId, Linea(lineas[0], articulo, 10m, acumulado: 10m)));
            Assert.Equal(10m, await LeerRecibidoRqAsync(rqLineaId));

            await EnviarDevolucionAsync(eventoDevolucion, PayloadDevolucion(ocId, LineaDevolucion(lineas[0], articulo, 1m)));

            Assert.Equal(9m, (await LeerOcAsync(ocId)).Lineas.Single().CantidadRecibida);
            Assert.Equal(9m, await LeerRecibidoRqAsync(rqLineaId));
        }
        finally
        {
            await LimpiarAsync(ocId, eventoRecepcion, eventoDevolucion);
            using var scope = factory.Services.CreateScope();
            var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE id = {rqId}");
        }
    }

    /// <summary>RQ en surtido (10 a compra) ligada a la línea de OC, como la deja «Convertir a OC».</summary>
    private async Task<(Guid RqId, Guid RqLineaId)> SembrarRqLigadaAsync(Guid ocId, Guid lineaOcId, Guid articulo)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var rq = new Millet.Compras.Domain.Requisicion(
            id: Guid.CreateVersion7(), empresaId: EmpresaBootstrapId,
            folio: Millet.Compras.Domain.Folio.Parse($"MID2026-{Random.Shared.Next(900000, 999999)}"), folioAnio: 2026,
            clasificacion: Millet.Compras.Domain.Clasificacion.MateriaPrima,
            sucursalId: Guid.NewGuid(), departamentoId: Guid.NewGuid(), almacenDestinoId: Guid.NewGuid(),
            requisitanteId: Guid.NewGuid(), creadorId: Guid.NewGuid(),
            prioridad: Millet.Compras.Domain.Prioridad.Normal, fechaSolicitud: DateTimeOffset.UtcNow, descripcion: null);
        var rqLineaId = Guid.CreateVersion7();
        rq.AgregarLinea(rqLineaId, articulo, 10m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(25m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.CreateVersion7(), Millet.Compras.Domain.NivelAutorizacion.Nivel1, Guid.NewGuid(),
            DateTimeOffset.UtcNow, Millet.Compras.Domain.Matriz.RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento(
            [new Millet.Compras.Domain.CubrimientoLinea(rqLineaId, CantidadDeAlmacen: 0m, CantidadDeCompra: 10m)],
            DateTimeOffset.UtcNow);
        compras.Requisiciones.Add(rq);
        await compras.SaveChangesAsync();
        var lineaOc = (await compras.OrdenesCompra.Include(o => o.Lineas).FirstAsync(o => o.Id == ocId))
            .Lineas.Single(l => l.Id == lineaOcId);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.RequisicionId))!.SetValue(lineaOc, rq.Id);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.LineaRequisicionId))!.SetValue(lineaOc, rqLineaId);
        await compras.SaveChangesAsync();
        return (rq.Id, rqLineaId);
    }

    private async Task<decimal> LeerRecibidoRqAsync(Guid rqLineaId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await compras.LineaRequisiciones.Where(l => l.Id == rqLineaId).Select(l => l.CantidadRecibida).SingleAsync();
    }

    /// <summary>Siembra una OC autorizada con una línea manual por cada artículo (100 PZA c/u).</summary>
    private async Task<(Guid OcId, Guid[] LineaIds)> SembrarOcAsync(params Guid[] articuloIds)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = P1DocumentosValidosTests.NuevaOc(EstadoOrdenCompra.Autorizada);
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Borrador);
        var lineaIds = articuloIds.Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < articuloIds.Length; i++)
        {
            oc.AgregarLineaManual(lineaIds[i], articuloIds[i], 100, "PZA", 25, Guid.NewGuid());
        }
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Autorizada);
        compras.OrdenesCompra.Add(oc);
        await compras.SaveChangesAsync();
        return (oc.Id, lineaIds);
    }

    private async Task EnviarAsync(Guid eventoId, OcRecepcionRegistradaAlmacenPayload payload)
    {
        using var scope = factory.Services.CreateScope();
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

    private async Task EnviarDevolucionAsync(Guid eventoId, OcDevolucionRegistradaAlmacenPayload payload)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(new OcDevolucionEnAlmacenCommand(eventoId, payload));
    }

    private async Task<int> ContarEventosAsync(Guid eventoId, string tipo = OcRecepcionEnAlmacenHandler.EventType)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await compras.EventosProcesados
            .IgnoreQueryFilters()
            .CountAsync(e => e.EventoId == eventoId && e.EventoTipo == tipo);
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

    private static OcRecepcionRegistradaAlmacenPayload Payload(Guid ocId, params LineaRecepcionAlmacenPayload[] lineas) =>
        new(EmpresaBootstrapId, DateTimeOffset.UtcNow, Guid.NewGuid(), "FIX-P15", ocId,
            new DateOnly(2026, 10, 9), false, null, null, lineas);

    private static LineaRecepcionAlmacenPayload Linea(
        Guid? lineaOcId, Guid articuloId, decimal cantidad,
        decimal? tolerancia = null, decimal? acumulado = null) =>
        new(Guid.NewGuid(), lineaOcId, articuloId, "PZA", cantidad, 25m, cantidad * 25m, tolerancia, acumulado);

    private static OcDevolucionRegistradaAlmacenPayload PayloadDevolucion(Guid ocId, params LineaDevolucionAlmacenPayload[] lineas) =>
        new(EmpresaBootstrapId, DateTimeOffset.UtcNow, Guid.NewGuid(), "FIX-P16", Guid.NewGuid(),
            null, null, ocId, "Devolucion de prueba P-16", 0m, lineas);

    private static LineaDevolucionAlmacenPayload LineaDevolucion(
        Guid? lineaOcId, Guid articuloId, decimal cantidad, decimal? cantidadDocumento = null) =>
        new(Guid.NewGuid(), null, lineaOcId, articuloId, "PZA", cantidad, 25m, cantidad * 25m, cantidadDocumento);
}
