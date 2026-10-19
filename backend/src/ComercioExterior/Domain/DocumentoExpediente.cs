using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;
public sealed class DocumentoExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid TipoDocumentoId { get; private set; }
    // Version heredada es el token de concurrencia; NumeroVersion es la revisión del documento.
    public int NumeroVersion { get; private set; }
    public string? NumeroPedimento { get; private set; }
    internal void RegistrarNumeroPedimento(string? numero) => NumeroPedimento = numero;
    public Guid? AdjuntoId { get; private set; }
    public Guid? ReemplazaA { get; private set; }
    public string? Motivo { get; private set; }
    public bool NoAplica { get; private set; }
    public Guid ResponsableUsuarioId { get; private set; }
    public EstadoDocumento Estado { get; private set; } = EstadoDocumento.Vigente;

    private DocumentoExpediente()
    {
    }

    internal DocumentoExpediente(Guid id, Guid empresa, Guid expediente, Guid tipo, int numero, Guid? adjunto, Guid? reemplaza, string? motivo, bool noAplica, Guid actor) : base(id)
    {
        EmpresaId = empresa;
        ExpedienteId = expediente;
        TipoDocumentoId = tipo;
        NumeroVersion = numero;
        AdjuntoId = adjunto;
        ReemplazaA = reemplaza;
        Motivo = motivo;
        NoAplica = noAplica;
        ResponsableUsuarioId = actor;
    }

    internal void Reemplazar() => Estado = EstadoDocumento.Reemplazada;
}
