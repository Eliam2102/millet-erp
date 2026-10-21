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
            datos.PartidaId is Guid partida && !_partidas.Any(x => x.Id == partida && (datos.ContenedorId == null || x.ContenedorId == datos.ContenedorId)))
            throw new BusinessRuleException("CE_INCIDENCIA_REFERENCIA_INVALIDA", "Seleccione participantes, documentos, hito, contenedor y partida del mismo expediente.");
        ExigirEvidencias(datos.EvidenciasIds ?? [], adjuntosVigentes);
        var i = new IncidenciaExpediente(id, this, datos, actor, ahora);
        _incidencias.Add(i); _avisosIncidencias.Add(new(i, ResponsableUsuarioId, ahora)); RevisionContenido++;
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
