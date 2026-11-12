using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;

public sealed class ConciliacionRecepcionServicio(ComercioExteriorDbContext db, ICalendarioHabil calendario, IRecepcionesImportacionReadPort almacen)
{
    public async Task BloquearAsync(Guid empresa, Guid[] ordenes, CancellationToken ct)
    {
        if (ordenes.Length == 0) return;
        // Bloquear antes de cargar entidades evita reutilizar snapshots rastreados anteriores al candado.
        _ = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM comercio_exterior.expedientes WHERE empresa_id = {empresa} AND ordenes_compra_ids && {ordenes} AND estado <> 'Cancelado' ORDER BY id FOR UPDATE").ToArrayAsync(ct);
    }

    public async Task AplicarAsync(Guid empresa, Guid[] ordenes, IReadOnlyCollection<Guid> lineas,
        Guid? evento, DateOnly fecha, Guid actor, DateTimeOffset ahora, CancellationToken ct)
    {
        if (ordenes.Length == 0 || lineas.Count == 0) return;
        await BloquearAsync(empresa, ordenes, ct);
        var expedientes = await db.Expedientes.FromSqlInterpolated($"SELECT * FROM comercio_exterior.expedientes WHERE empresa_id = {empresa} AND ordenes_compra_ids && {ordenes} AND estado <> 'Cancelado' ORDER BY id")
            .Include(x => x.PartidasImportacion).Include(x => x.Contenedores).Include(x => x.Hitos)
            .Include(x => x.Incidencias).ThenInclude(i => i.Historial).Include(x => x.AvisosIncidencias)
            .AsSplitQuery().ToListAsync(ct);
        if (expedientes.Count == 0) return;
        var cantidades = new Dictionary<Guid, decimal>();
        foreach (var oc in ordenes.Distinct())
            foreach (var cantidad in await almacen.ObtenerCantidadesRecibidasAsync(oc, ct))
                cantidades.Add(cantidad.Key, cantidad.Value);
        var dias = await db.Set<ConfiguracionCe>().Where(c => c.EmpresaId == empresa)
            .Select(c => (int?)c.DiasHabilesIncidenciaRecepcion).SingleOrDefaultAsync(ct) ?? 2;
        foreach (var linea in lineas.Distinct())
        {
            var asignaciones = expedientes.Select(e => new { Expediente = e, Partidas = e.PartidasImportacion
                    .Where(p => p.OcLineaId == linea && e.Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo && e.Contenedor(p.ContenedorId).FechaEmbarqueReal != null)
                    .OrderBy(p => p.Id).ToArray() })
                .Where(x => x.Partidas.Length > 0)
                .OrderBy(x => x.Expediente.Hitos.Single(h => h.Clave == "arribado").FechaReal ?? DateOnly.MaxValue)
                .ThenBy(x => x.Expediente.CreatedAt).ThenBy(x => x.Expediente.Id).ToArray();
            if (asignaciones.Length == 0) continue;
            if (!cantidades.TryGetValue(linea, out var acumulado))
                throw new BusinessRuleException("CE_RECEPCION_LINEA_SIN_ORIGEN", "No se pudo consultar la partida de OC en Almacén; no se modificó su conciliación.");
            var previo = 0m;
            foreach (var asignacion in asignaciones)
            {
                var e = asignacion.Expediente; var embarcado = asignacion.Partidas.Sum(p => p.Cantidad);
                var recibido = Math.Min(Math.Max(0, acumulado - previo), embarcado);
                if (asignacion == asignaciones[^1]) recibido = Math.Max(0, acumulado - previo);
                var descripcion = $"Partida OC {linea}. Acumulado Almacén: {acumulado}. Asignado a {e.Folio}: {recibido} de {embarcado}; aplicado a expedientes anteriores: {Math.Min(acumulado, previo)}. Orden: arribo real más antiguo; sin arribo, creación e identificador.";
                previo += embarcado;
                if (e.Estado != EstadoExpediente.Abierto) continue;
                var p = asignacion.Partidas[0];
                var fechaAplicada = evento is null ? p.FechaRecepcion ?? fecha : fecha;
                var compromiso = evento is null && p.CompromisoRecepcion is DateOnly anterior ? anterior
                    : await PlazoRecepcionCe.SumarAsync(calendario, fechaAplicada, dias, ct);
                var responsable = actor == Guid.Empty ? e.ResponsableUsuarioId : actor;
                if (evento is Guid id) e.ConciliarRecepcion(id, p.Id, recibido, fechaAplicada, compromiso, responsable, ahora, descripcion);
                else e.ReconciliarRecepcion(p.Id, recibido, fechaAplicada, compromiso, responsable, ahora, descripcion);
            }
        }
    }
}
