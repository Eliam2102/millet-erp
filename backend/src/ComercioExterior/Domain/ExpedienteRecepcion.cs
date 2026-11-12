using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    public void ConciliarRecepcion(Guid evento, Guid partida, decimal recibida, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora, string? asignacion = null)
    {
        if (evento == Guid.Empty) throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "Se requiere el evento de recepción.");
        AplicarRecepcion(evento, partida, recibida, fecha, compromiso, actor, ahora, asignacion);
    }
    public void ReconciliarRecepcion(Guid partida, decimal recibida, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora, string? asignacion = null)
        => AplicarRecepcion(null, partida, recibida, fecha, compromiso, actor, ahora, asignacion);

    private void AplicarRecepcion(Guid? evento, Guid partida, decimal recibida, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora, string? asignacion)
    {
        ExigirAbierto();
        var p = _partidasImportacion.Single(x => x.Id == partida);
        if (evento is Guid id && p.EventosRecepcion.Contains(id)) return;
        if (recibida < 0 || decimal.Round(recibida, 6) != recibida || fecha == default || compromiso < fecha || actor == Guid.Empty)
            throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "La recepción requiere cantidad, fecha y usuario válidos.");
        var pendientes = recibida;
        var partidas = PartidasRecepcion(p).OrderBy(x => x.Id).ToArray();
        var cambio = false;
        foreach (var item in partidas)
        {
            var cantidad = item == partidas[^1] ? pendientes : Math.Min(pendientes, item.Cantidad);
            cambio |= item.CantidadRecibida != cantidad;
            item.RegistrarRecepcion(evento, cantidad, fecha, compromiso); pendientes -= cantidad;
        }
        var diferencia = recibida - partidas.Sum(x => x.Cantidad);
        var incidencia = _incidencias.SingleOrDefault(i => i.Automatica && partidas.Any(x => x.Id == i.PartidaId));
        var corregirIncidencia = incidencia is null ? diferencia != 0 : incidencia.DiferenciaCantidad != Math.Abs(diferencia)
            || (diferencia == 0 && incidencia.Pendiente)
            || (diferencia != 0 && incidencia.Tipo != (diferencia < 0 ? TipoIncidencia.MercanciaFaltante : TipoIncidencia.MercanciaSobrante));
        if (!cambio && !corregirIncidencia) return;
        RecalcularIncidenciaRecepcion(p, fecha, compromiso, actor, ahora, asignacion);
        RevisionContenido++;
    }
    private IEnumerable<PartidaImportacion> PartidasRecepcion(PartidaImportacion p) => _partidasImportacion.Where(x =>
        x.OcLineaId == p.OcLineaId && Contenedor(x.ContenedorId).Estado == EstadoContenedor.Activo && Contenedor(x.ContenedorId).FechaEmbarqueReal != null);
    private void RecalcularIncidenciaRecepcion(PartidaImportacion p, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora, string? asignacion = null)
    {
        var partidas = PartidasRecepcion(p).OrderBy(x => x.Id).ToArray();
        p = partidas[0];
        var diferencia = partidas.Sum(x => x.CantidadRecibida ?? 0) - partidas.Sum(x => x.Cantidad);
        var i = _incidencias.SingleOrDefault(x => x.Automatica && _partidasImportacion.Any(partida => partida.Id == x.PartidaId && partida.OcLineaId == p.OcLineaId));
        if (i is not null) { i.ActualizarDiferencia(diferencia, actor, ahora, asignacion); return; }
        if (diferencia == 0) return;
        i = new(Guid.CreateVersion7(), this, new(diferencia < 0 ? TipoIncidencia.MercanciaFaltante : TipoIncidencia.MercanciaSobrante,
            fecha, ResponsableUsuarioId == Guid.Empty ? actor : ResponsableUsuarioId, "Diferencia entre cantidad embarcada y recibida",
            Descripcion: asignacion, PartidaId: p.Id, FechaCompromiso: compromiso, SiguienteAccion: "Aclarar la diferencia con el proveedor y el agente aduanal"), actor, ahora);
        i.MarcarAutomatica(Math.Abs(diferencia)); _incidencias.Add(i); _avisosIncidencias.Add(new(i, i.ResponsableUsuarioId, ahora));
    }
    public void EditarSeguimientoIncidencia(Guid id, DateOnly fecha, string accion, string motivo, Guid actor, DateTimeOffset ahora)
    {
        ExigirAbierto();
        var i = _incidencias.SingleOrDefault(x => x.Id == id) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia en el expediente.");
        i.EditarSeguimiento(fecha, accion, motivo, actor, ahora); RevisionContenido++;
    }
}
