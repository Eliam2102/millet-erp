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
/// como procesado, acumulado (<c>CantidadRecibidaAcumulada</c>) con tolerancia y
/// dedupe por <c>EventoId</c>. Usa PostgreSQL real y OC sembradas con el fixture P1.
/// </summary>
public sealed class ConsumidoresComprasContModAlmTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Acumulado_105_con_tolerancia_5_sobre_100_completa_la_linea()
    {
        var articulo = Guid.NewGuid();
        var (ocId, lineas) = await SembrarOcAsync(articulo);
        var eventoId = Guid.NewGuid();
        try
        {
            await EnviarAsync(eventoId, Payload(ocId, Linea(lineas[0], articulo, 105m, tolerancia: 5m, acumulado: 105m)));

            var oc = await LeerOcAsync(ocId);
            Assert.Equal(105m, oc.Lineas.Single(l => l.Id == lineas[0]).CantidadRecibida);
            Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
            Assert.Equal(1, await ContarEventosAsync(eventoId));
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
