using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    private readonly List<IncidenciaExpediente> _incidencias = [];
    public IReadOnlyCollection<IncidenciaExpediente> Incidencias => _incidencias.AsReadOnly();
    private readonly List<AvisoIncidencia> _avisosIncidencias = [];
    public IReadOnlyCollection<AvisoIncidencia> AvisosIncidencias => _avisosIncidencias.AsReadOnly();
    public string? NumeroPedimentoVigenteImportacion { get; private set; }
    public string? PedimentoVigente(string? originalImportacion = null) => Tipo == TipoExpediente.Exportacion
        ? NumeroPedimentoExportacion : NumeroPedimentoVigenteImportacion ?? originalImportacion;
    public void ExigirSinIncidencias()
    {
        var pendientes = _incidencias.Where(i => i.Pendiente).ToArray();
        if (pendientes.Length > 0)
            throw new BusinessRuleException("CE_INCIDENCIAS_PENDIENTES", "Resuelva o cancele las incidencias antes de cerrar: " + string.Join("; ", pendientes.Select(i => $"{i.Fecha:dd/MM/yyyy}: {i.Motivo}")));
    }
    public void AbrirIncidencia(Guid id, DatosIncidencia datos, IReadOnlyCollection<Guid> adjuntosVigentes, Guid actor, DateTimeOffset ahora, DateOnly hoy)
    {
        ExigirAbierto();
        if (datos.Fecha > hoy) throw new BusinessRuleException("CE_INCIDENCIA_FECHA_INVALIDA", "La fecha de incidencia no puede ser futura.");
        if (datos.ParticipanteId is Guid p && !_participantes.Any(x => x.Id == p) ||
            (datos.DocumentosIds ?? []).Any(d => !_documentos.Any(x => x.Id == d)) ||
            datos.HitoClave is not null && !_hitos.Any(x => x.Clave == datos.HitoClave) ||
            datos.ContenedorId is Guid c && !_contenedores.Any(x => x.Id == c) ||
            datos.PartidaId is Guid partida && !_partidas.Any(x => x.Id == partida && (datos.ContenedorId == null || x.ContenedorId == datos.ContenedorId)) && !_partidasImportacion.Any(x => x.Id == partida && (datos.ContenedorId == null || x.ContenedorId == datos.ContenedorId)))
            throw new BusinessRuleException("CE_INCIDENCIA_REFERENCIA_INVALIDA", "Seleccione participantes, documentos, hito, contenedor y partida del mismo expediente.");
        ExigirEvidencias(datos.EvidenciasIds ?? [], adjuntosVigentes);
        var i = new IncidenciaExpediente(id, this, datos, actor, ahora);
        _incidencias.Add(i); _avisosIncidencias.Add(new(i, ResponsableUsuarioId, ahora)); RevisionContenido++;
    }
    public int GenerarAvisosIncidencias(DateOnly hoy, DateTimeOffset ahora)
    {
        if (Estado != EstadoExpediente.Abierto) return 0;
        var pendientes = _incidencias.Where(i => i.Pendiente && i.FechaCompromiso < hoy && !_avisosIncidencias.Any(a => a.IncidenciaId == i.Id && a.EsVencimiento)).ToArray();
        foreach (var i in pendientes) _avisosIncidencias.Add(new(i, i.ResponsableUsuarioId, ahora, true));
        return pendientes.Length;
    }
    public void AutorizarAjuste(Guid id, Guid actor, DateTimeOffset fecha, bool facturada, decimal cantidadPedida)
    {
        ExigirAbierto();
        var i = _incidencias.SingleOrDefault(i => i.Id == id && i.Tipo == TipoIncidencia.AjusteCantidades) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "Seleccione una incidencia de ajuste de cantidades.");
        i.ExigirPendiente();
        if (actor == Guid.Empty || actor == i.CreadoPorId) throw new BusinessRuleException("CE_AJUSTE_SEPARACION_FUNCIONES", "El ajuste debe autorizarlo una persona distinta de quien lo solicitó.");
        if (facturada) throw new BusinessRuleException("CE_AJUSTE_PARTIDA_FACTURADA", "La partida ya está facturada. Corrija la diferencia mediante una nota de crédito en Facturación.");
        var p = _partidas.SingleOrDefault(p => p.Id == i.PartidaId);
        var imp = _partidasImportacion.SingleOrDefault(p => p.Id == i.PartidaId);
        var actual = p?.Cantidad ?? imp?.Cantidad;
        if (actual != i.CantidadAnterior || i.CantidadNueva is not decimal nueva)
            throw new BusinessRuleException("CE_AJUSTE_OBSOLETO", "La cantidad de la partida cambió. Registre una nueva solicitud de ajuste.");
        var acumulado = p is not null ? _partidas.Where(x => x.PedidoFacturableId == p.PedidoFacturableId && x.Posicion == p.Posicion && x.Estado != EstadoPartida.Liberada && Contenedor(x.ContenedorId).Estado == EstadoContenedor.Activo).Sum(x => x.Cantidad)
            : _partidasImportacion.Where(x => x.OcLineaId == imp!.OcLineaId && Contenedor(x.ContenedorId).Estado == EstadoContenedor.Activo).Sum(x => x.Cantidad);
        if (acumulado - actual + nueva > cantidadPedida) throw new BusinessRuleException("CE_CANTIDAD_EXCEDE_PEDIDO", "El ajuste excede la cantidad pedida de la partida.");
        if (p is not null) p.AjustarCantidad(nueva); else imp!.AjustarCantidad(nueva);
        i.AutorizarAjuste(actor, fecha);
        if (imp?.CantidadRecibida is not null) RecalcularIncidenciaRecepcion(imp, imp.FechaRecepcion!.Value, imp.CompromisoRecepcion!.Value, actor, fecha);
        VersionarEmbarque(actor); RevisionContenido++;
    }
    private static void ExigirEvidencias(IEnumerable<Guid> ids, IReadOnlyCollection<Guid> vigentes)
    {
        if (ids.Any(id => !vigentes.Contains(id))) throw new BusinessRuleException("CE_ADJUNTO_INVALIDO", "La evidencia debe estar vigente y pertenecer a este expediente.");
    }
    public void CambiarIncidencia(Guid id, EstadoIncidencia estado, string? accion, string? efecto, Guid[] nuevasEvidencias,
        string? motivo, Guid actor, DateTimeOffset ahora, IReadOnlyCollection<Guid> vigentes,
        RequisitoDocumento? pedimento = null, Guid? adjuntoPedimento = null, string? nuevoPedimento = null, string? originalImportacion = null)
    {
        ExigirAbierto();
        var i = _incidencias.SingleOrDefault(x => x.Id == id) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia en el expediente.");
        i.ExigirPendiente();
        if (i.Tipo == TipoIncidencia.AjusteCantidades && estado == EstadoIncidencia.Resuelta)
            throw new BusinessRuleException("CE_AJUSTE_REQUIERE_AUTORIZACION", "Use Autorizar ajuste para resolver y aplicar el cambio de cantidad.");
        var evidencias = i.EvidenciasIds.Concat(nuevasEvidencias).Distinct().ToArray();
        // Las referencias históricas se conservan aunque un archivo venza; los nuevos deben estar vigentes.
        ExigirEvidencias(nuevasEvidencias, vigentes);
        string? anterior = null; string? nuevo = null;
        if (estado == EstadoIncidencia.Resuelta && i.Tipo == TipoIncidencia.RectificacionPedimento)
        {
            anterior = _documentos.Where(d => d.TipoDocumentoId == pedimento?.Id && d.Estado == EstadoDocumento.Vigente).Select(d => d.NumeroPedimento).SingleOrDefault() ?? PedimentoVigente(originalImportacion);
            nuevo = nuevoPedimento?.Trim();
            if (string.IsNullOrWhiteSpace(anterior) || string.IsNullOrWhiteSpace(nuevo) || nuevo.Length > 100 || nuevo == anterior || pedimento is null || adjuntoPedimento is not Guid a || !vigentes.Contains(a))
                throw new BusinessRuleException("CE_RECTIFICACION_PEDIMENTO_INVALIDA", "Registre el pedimento original, un número nuevo distinto y el archivo vigente del nuevo pedimento.");
            if (_documentos.Any(d => d.AdjuntoId == adjuntoPedimento))
                throw new BusinessRuleException("CE_ADJUNTO_YA_VERSIONADO", "Cargue un archivo nuevo para la rectificación del pedimento.");
            evidencias = evidencias.Append(a).Distinct().ToArray();
        }
        if (estado == EstadoIncidencia.Resuelta && !evidencias.Any(vigentes.Contains))
            throw new BusinessRuleException("CE_INCIDENCIA_RESOLUCION_INCOMPLETA", "Adjunte al menos una evidencia vigente para resolver la incidencia.");
        i.CambiarEstado(estado, accion, efecto, evidencias, motivo, actor, ahora, anterior, nuevo);
        if (nuevo is not null)
        {
            RegistrarDocumento(Guid.CreateVersion7(), pedimento!, adjuntoPedimento!.Value, i.Motivo, actor, nuevo);
            if (Tipo == TipoExpediente.Exportacion) { NumeroPedimentoExportacion = nuevo; DocumentoPedimentoExportacionId = _documentos.Single(d => d.TipoDocumentoId == pedimento!.Id && d.Estado == EstadoDocumento.Vigente).Id; }
        }
        RevisionContenido++;
    }
}
