using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    public void ConciliarRecepcion(Guid evento, Guid partida, decimal recibida, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora)
    {
        ExigirAbierto();
        var p = _partidasImportacion.Single(x => x.Id == partida);
        if (p.EventosRecepcion.Contains(evento)) return;
        if (evento == Guid.Empty || recibida < 0 || decimal.Round(recibida, 6) != recibida || fecha == default || compromiso < fecha || actor == Guid.Empty)
            throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "La recepción requiere evento, cantidad, fecha y usuario válidos.");
        p.RegistrarRecepcion(evento, recibida, fecha, compromiso);
        RecalcularIncidenciaRecepcion(p, recibida, fecha, compromiso, actor, ahora);
        RevisionContenido++;
    }
    private void RecalcularIncidenciaRecepcion(PartidaImportacion p, decimal recibida, DateOnly fecha, DateOnly compromiso, Guid actor, DateTimeOffset ahora)
    {
        var diferencia = recibida - p.Cantidad;
        var i = _incidencias.SingleOrDefault(x => x.Automatica && x.PartidaId == p.Id && x.Pendiente);
        if (i is not null) { i.ActualizarDiferencia(diferencia, actor, ahora); return; }
        if (diferencia == 0) return;
        i = new(Guid.CreateVersion7(), this, new(diferencia < 0 ? TipoIncidencia.MercanciaFaltante : TipoIncidencia.MercanciaSobrante,
            fecha, ResponsableUsuarioId == Guid.Empty ? actor : ResponsableUsuarioId, "Diferencia entre cantidad embarcada y recibida",
            ContenedorId: p.ContenedorId, PartidaId: p.Id, FechaCompromiso: compromiso, SiguienteAccion: "Aclarar la diferencia con el proveedor y el agente aduanal"), actor, ahora);
        i.MarcarAutomatica(Math.Abs(diferencia)); _incidencias.Add(i); _avisosIncidencias.Add(new(i, i.ResponsableUsuarioId, ahora));
    }
    public void EditarSeguimientoIncidencia(Guid id, DateOnly fecha, string accion, string motivo, Guid actor, DateTimeOffset ahora)
    {
        ExigirAbierto();
        var i = _incidencias.SingleOrDefault(x => x.Id == id) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia en el expediente.");
        i.EditarSeguimiento(fecha, accion, motivo, actor, ahora); RevisionContenido++;
    }
}
