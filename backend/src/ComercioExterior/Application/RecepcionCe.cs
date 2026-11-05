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
public sealed class RecibirRecepcionCeHandler(ComercioExteriorDbContext db, ICalendarioHabil calendario) : IRequestHandler<RecibirRecepcionCeCommand>
{
    public async Task Handle(RecibirRecepcionCeCommand request, CancellationToken cancellationToken)
    {
        var d = request.Datos;
        if (d.EmpresaId == Guid.Empty || d.OrdenCompraId == Guid.Empty || d.FechaMovimiento == default || request.EventoId == Guid.Empty || d.Lineas is null)
            throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "La recepción no contiene empresa, orden de compra, fecha o partidas válidas.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var e = await db.Expedientes.FromSqlInterpolated($"SELECT * FROM comercio_exterior.expedientes WHERE empresa_id = {d.EmpresaId} AND oc_id = {d.OrdenCompraId} AND estado = 'Abierto' FOR UPDATE")
            .Include(x => x.PartidasImportacion).Include(x => x.Contenedores).Include(x => x.Incidencias).ThenInclude(i => i.Historial).Include(x => x.AvisosIncidencias)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (e is null) return;
        var dias = await db.Set<ConfiguracionCe>().Where(c => c.EmpresaId == d.EmpresaId).Select(c => (int?)c.DiasHabilesIncidenciaRecepcion).SingleOrDefaultAsync(cancellationToken) ?? 2;
        DateOnly? compromiso = null;
        foreach (var grupo in d.Lineas.Where(l => l.LineaOcId != null).GroupBy(l => l.LineaOcId!.Value))
        {
            var partidas = e.PartidasImportacion.Where(p => p.OcLineaId == grupo.Key && e.Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo && e.Contenedor(p.ContenedorId).FechaEmbarqueReal != null).ToArray();
            if (partidas.Length == 0) continue;
            // shortcut: el evento solo identifica la partida de OC, definir la asignación por contenedor antes de soportar embarques repartidos.
            if (partidas.Length != 1)
                throw new BusinessRuleException("CE_RECEPCION_ASIGNACION_REQUERIDA", "La partida de OC está repartida entre contenedores. Confirme la asignación de la recepción antes de conciliarla.");
            var p = partidas[0];
            if (p.EventosRecepcion.Contains(request.EventoId)) continue;
            var valores = grupo.Select(l => l.CantidadRecibidaAcumulada).Distinct().ToArray();
            if (valores.Length != 1 || valores[0] is not decimal acumulado)
                throw new BusinessRuleException("CE_RECEPCION_ACUMULADO_REQUERIDO", "Almacén debe indicar una cantidad acumulada única por partida de OC.");
            compromiso ??= await PlazoRecepcionCe.SumarAsync(calendario, d.FechaMovimiento, dias, cancellationToken);
            e.ConciliarRecepcion(request.EventoId, p.Id, acumulado, d.FechaMovimiento, compromiso.Value,
                d.RegistradoPorId == Guid.Empty ? e.ResponsableUsuarioId : d.RegistradoPorId, d.OcurridoEn);
        }
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
