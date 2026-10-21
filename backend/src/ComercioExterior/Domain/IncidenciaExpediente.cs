using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum TipoIncidencia { CancelacionOperacion = 1, RectificacionPedimento, MercanciaRetenida, MercanciaRechazada, MercanciaDanada, MercanciaFaltante, RoturaTransporte, DocumentoIncorrecto, Otra }
public enum EstadoIncidencia { Abierta = 1, EnAtencion, Resuelta, Cancelada }
public sealed record DatosIncidencia(TipoIncidencia Tipo, DateOnly Fecha, Guid ResponsableUsuarioId, string Motivo,
    Guid? ParticipanteId = null, Guid[]? DocumentosIds = null, Guid[]? EvidenciasIds = null, string? Descripcion = null,
    string? AccionCorrectiva = null, string? EfectoPosterior = null, string? HitoClave = null, Guid? ContenedorId = null, Guid? PartidaId = null);

public sealed class IncidenciaExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public TipoIncidencia Tipo { get; private set; }
    public DateOnly Fecha { get; private set; }
    public Guid ResponsableUsuarioId { get; private set; }
    public Guid? ParticipanteId { get; private set; }
    public string Motivo { get; private set; } = "";
    public string? Descripcion { get; private set; }
    public Guid[] DocumentosIds { get; private set; } = [];
    public Guid[] EvidenciasIds { get; private set; } = [];
    public string? AccionCorrectiva { get; private set; }
    public string? EfectoPosterior { get; private set; }
    public string? HitoClave { get; private set; }
    public Guid? ContenedorId { get; private set; }
    public Guid? PartidaId { get; private set; }
    public EstadoIncidencia Estado { get; private set; } = EstadoIncidencia.Abierta;
    public string? PedimentoAnterior { get; private set; }
    public string? PedimentoNuevo { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    public bool Pendiente => Estado is EstadoIncidencia.Abierta or EstadoIncidencia.EnAtencion;
    private readonly List<EstadoIncidenciaHistorial> _historial = [];
    public IReadOnlyCollection<EstadoIncidenciaHistorial> Historial => _historial.AsReadOnly();
    private IncidenciaExpediente() { }
    internal IncidenciaExpediente(Guid id, Expediente e, DatosIncidencia d, Guid actor, DateTimeOffset ahora) : base(id)
    {
        if (!Enum.IsDefined(d.Tipo) || d.Fecha == default || d.ResponsableUsuarioId == Guid.Empty)
            throw new BusinessRuleException("CE_INCIDENCIA_DATOS_INVALIDOS", "Seleccione tipo, fecha y responsable de la incidencia.");
        if (d.Tipo == TipoIncidencia.Otra && string.IsNullOrWhiteSpace(d.Descripcion))
            throw new BusinessRuleException("CE_INCIDENCIA_DESCRIPCION_REQUERIDA", "Describa la incidencia de tipo Otra.");
        EmpresaId = e.EmpresaId; ExpedienteId = e.Id; Tipo = d.Tipo; Fecha = d.Fecha;
        ResponsableUsuarioId = d.ResponsableUsuarioId; ParticipanteId = d.ParticipanteId;
        Motivo = Expediente.MotivoValido(d.Motivo); Descripcion = Texto(d.Descripcion);
        DocumentosIds = (d.DocumentosIds ?? []).Distinct().ToArray(); EvidenciasIds = (d.EvidenciasIds ?? []).Distinct().ToArray();
        AccionCorrectiva = Texto(d.AccionCorrectiva); EfectoPosterior = Texto(d.EfectoPosterior);
        HitoClave = d.HitoClave; ContenedorId = d.ContenedorId; PartidaId = d.PartidaId;
        _historial.Add(new(this, actor, ahora, Motivo));
    }
    internal static string? Texto(string? valor)
    {
        if (valor?.Trim().Length > 2000) throw new BusinessRuleException("CE_INCIDENCIA_TEXTO_INVALIDO", "Los textos de la incidencia admiten hasta 2000 caracteres.");
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
    internal void ExigirPendiente()
    {
        if (!Pendiente) throw new BusinessRuleException("CE_INCIDENCIA_FINALIZADA", "La incidencia ya finalizó; se conserva su historial.");
    }
    internal void CambiarEstado(EstadoIncidencia estado, string? accion, string? efecto, Guid[] evidencias, string? motivo,
        Guid actor, DateTimeOffset ahora, string? anterior = null, string? nuevo = null)
    {
        ExigirPendiente();
        if (estado is not (EstadoIncidencia.EnAtencion or EstadoIncidencia.Resuelta or EstadoIncidencia.Cancelada) || estado == Estado || (estado == EstadoIncidencia.Resuelta && Estado != EstadoIncidencia.EnAtencion))
            throw new BusinessRuleException("CE_INCIDENCIA_TRANSICION_INVALIDA", "Seleccione una transición válida de la incidencia.");
        var tomada = Texto(accion); var posterior = Texto(efecto);
        var razon = estado == EstadoIncidencia.Cancelada ? Expediente.MotivoValido(motivo) : tomada;
        if (estado == EstadoIncidencia.Resuelta && (tomada is null || evidencias.Length == 0))
            throw new BusinessRuleException("CE_INCIDENCIA_RESOLUCION_INCOMPLETA", "Registre la acción tomada y al menos una evidencia para resolver la incidencia.");
        Estado = estado; AccionCorrectiva = tomada ?? AccionCorrectiva; EfectoPosterior = posterior ?? EfectoPosterior;
        EvidenciasIds = evidencias; MotivoCancelacion = estado == EstadoIncidencia.Cancelada ? razon : null;
        PedimentoAnterior = anterior; PedimentoNuevo = nuevo;
        _historial.Add(new(this, actor, ahora, razon));
    }
}

public sealed class EstadoIncidenciaHistorial : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid IncidenciaId { get; private set; }
    public EstadoIncidencia Estado { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public string? Motivo { get; private set; }
    private EstadoIncidenciaHistorial() { }
    internal EstadoIncidenciaHistorial(IncidenciaExpediente i, Guid actor, DateTimeOffset fecha, string? motivo) : base(Guid.CreateVersion7())
    { EmpresaId = i.EmpresaId; ExpedienteId = i.ExpedienteId; IncidenciaId = i.Id; Estado = i.Estado; ActorId = actor; Fecha = fecha; Motivo = motivo; }
}

// Aviso persistido en la aplicación, igual que AvisoHitoVencido; no implica correo enviado.
public sealed class AvisoIncidencia : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid IncidenciaId { get; private set; }
    public Guid DestinatarioUsuarioId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    private AvisoIncidencia() { }
    internal AvisoIncidencia(IncidenciaExpediente i, Guid destinatario, DateTimeOffset fecha) : base(Guid.CreateVersion7())
    { EmpresaId = i.EmpresaId; ExpedienteId = i.ExpedienteId; IncidenciaId = i.Id; DestinatarioUsuarioId = destinatario; Fecha = fecha; }
}
