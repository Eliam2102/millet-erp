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
        var expedientes = await db.Expedientes.FromSqlInterpolated($"SELECT * FROM comercio_exterior.expedientes WHERE empresa_id = {d.EmpresaId} AND {d.OrdenCompraId} = ANY(ordenes_compra_ids) AND estado <> 'Cancelado' ORDER BY id FOR UPDATE")
            .Include(x => x.PartidasImportacion).Include(x => x.Contenedores).Include(x => x.Hitos).Include(x => x.Incidencias).ThenInclude(i => i.Historial).Include(x => x.AvisosIncidencias)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var dias = await db.Set<ConfiguracionCe>().Where(c => c.EmpresaId == d.EmpresaId).Select(c => (int?)c.DiasHabilesIncidenciaRecepcion).SingleOrDefaultAsync(cancellationToken) ?? 2;
        DateOnly? compromiso = null;
        foreach (var grupo in d.Lineas.Where(l => l.LineaOcId != null).GroupBy(l => l.LineaOcId!.Value))
        {
            var asignaciones = expedientes.Select(e => new { Expediente = e, Partidas = e.PartidasImportacion.Where(p => p.OcLineaId == grupo.Key && e.Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo && e.Contenedor(p.ContenedorId).FechaEmbarqueReal != null).OrderBy(p => p.Id).ToArray() })
                .Where(x => x.Partidas.Length > 0)
                .OrderBy(x => x.Expediente.Hitos.Single(h => h.Clave == "arribado").FechaReal ?? DateOnly.MaxValue)
                .ThenBy(x => x.Expediente.CreatedAt).ThenBy(x => x.Expediente.Id).ToArray();
            if (asignaciones.Length == 0) continue;
            var valores = grupo.Select(l => l.CantidadRecibidaAcumulada).Distinct().ToArray();
            if (valores.Length != 1 || valores[0] is not decimal acumulado || acumulado < 0)
                throw new BusinessRuleException("CE_RECEPCION_ACUMULADO_REQUERIDO", "Almacén debe indicar una cantidad acumulada única por partida de OC.");
            var previo = 0m;
            foreach (var asignacion in asignaciones)
            {
                var e = asignacion.Expediente; var embarcado = asignacion.Partidas.Sum(p => p.Cantidad);
                var recibido = Math.Min(Math.Max(0, acumulado - previo), embarcado);
                if (asignacion == asignaciones[^1]) recibido = Math.Max(0, acumulado - previo);
                var descripcion = $"Partida OC {grupo.Key}. Acumulado Almacén: {acumulado}. Asignado a {e.Folio}: {recibido} de {embarcado}; aplicado a expedientes anteriores: {Math.Min(acumulado, previo)}. Orden: arribo real más antiguo; sin arribo, creación e identificador.";
                previo += embarcado;
                if (e.Estado != EstadoExpediente.Abierto) continue;
                compromiso ??= await PlazoRecepcionCe.SumarAsync(calendario, d.FechaMovimiento, dias, cancellationToken);
                e.ConciliarRecepcion(request.EventoId, asignacion.Partidas[0].Id, recibido, d.FechaMovimiento, compromiso.Value,
                    d.RegistradoPorId == Guid.Empty ? e.ResponsableUsuarioId : d.RegistradoPorId, d.OcurridoEn, descripcion);
            }
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
