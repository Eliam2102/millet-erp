using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record LineaRecepcionCe(Guid? LineaOcId, decimal? CantidadRecibidaAcumulada);
public sealed record RecepcionCePayload(Guid EmpresaId, Guid OrdenCompraId, DateOnly FechaMovimiento, DateTimeOffset OcurridoEn,
    IReadOnlyList<LineaRecepcionCe> Lineas, Guid RegistradoPorId = default);
public sealed record RecibirRecepcionCeCommand(Guid EventoId, RecepcionCePayload Datos) : IRequest;
public sealed class RecibirRecepcionCeHandler(ComercioExteriorDbContext db, ConciliacionRecepcionServicio conciliacion, Millet.SharedKernel.Application.IClock clock) : IRequestHandler<RecibirRecepcionCeCommand>
{
    public async Task Handle(RecibirRecepcionCeCommand request, CancellationToken cancellationToken)
    {
        var d = request.Datos;
        if (d.EmpresaId == Guid.Empty || d.OrdenCompraId == Guid.Empty || d.FechaMovimiento == default || request.EventoId == Guid.Empty || d.Lineas is null)
            throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "La recepción no contiene empresa, orden de compra, fecha o partidas válidas.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var grupos = d.Lineas.Where(l => l.LineaOcId != null).GroupBy(l => l.LineaOcId!.Value).ToArray();
        // Las devoluciones no llevan acumulado; siempre se consulta el vigente en el puerto de Almacén.
        foreach (var grupo in grupos)
        {
            var valores = grupo.Select(l => l.CantidadRecibidaAcumulada).Distinct().ToArray();
            if (valores.Length != 1 || (valores[0] is decimal acumulado && (acumulado < 0 || decimal.Round(acumulado, 6) != acumulado)))
                throw new BusinessRuleException("CE_RECEPCION_ACUMULADO_REQUERIDO", "Si Almacén indica un acumulado, debe ser único, no negativo y con hasta seis decimales por partida de OC.");
        }
        await conciliacion.AplicarAsync(d.EmpresaId, [d.OrdenCompraId], grupos.Select(g => g.Key).ToArray(),
            request.EventoId, d.FechaMovimiento, d.RegistradoPorId, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
}
public static class PlazoRecepcionCe
{
    public static async Task<DateOnly> SumarAsync(ICalendarioHabil calendario, DateOnly fecha, int dias, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dias);
        var resultado = fecha;
        while (await calendario.ContarDiasAsync(fecha, resultado, ct) < dias) resultado = resultado.AddDays(1);
        return resultado;
    }
}
