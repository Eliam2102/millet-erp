using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;

/// <summary>
/// M-373: historial de correcciones de un hito ya resuelto (fecha real u omisión). Conserva el valor
/// anterior y el nuevo, quién corrigió, cuándo y por qué. No se edita ni se borra.
/// </summary>
public sealed class CorreccionHitoExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid HitoId { get; private set; }
    public DateOnly? FechaRealAnterior { get; private set; }
    public bool OmitidoAnterior { get; private set; }
    public string? MotivoAnterior { get; private set; }
    public DateOnly? FechaRealNueva { get; private set; }
    public bool OmitidoNuevo { get; private set; }
    public string Motivo { get; private set; } = "";
    public Guid UsuarioId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    private CorreccionHitoExpediente() { }
    internal CorreccionHitoExpediente(HitoExpediente hito, DateOnly? fechaAnterior, bool omitidoAnterior, string? motivoAnterior, string motivo, Guid usuario, DateTimeOffset fecha)
        : base(Guid.CreateVersion7())
    {
        EmpresaId = hito.EmpresaId; ExpedienteId = hito.ExpedienteId; HitoId = hito.Id;
        FechaRealAnterior = fechaAnterior; OmitidoAnterior = omitidoAnterior; MotivoAnterior = motivoAnterior;
        FechaRealNueva = hito.FechaReal; OmitidoNuevo = hito.Omitido;
        Motivo = motivo; UsuarioId = usuario; Fecha = fecha;
    }
}
