using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;

public enum TipoDocumentoActivo { FacturaCompra, Seguro, Garantia, Arrendamiento, Pedimento, Manual, Fotografia, Otro }
public sealed record DocumentoActivoDatos(TipoDocumentoActivo Tipo, Guid AdjuntoId, string Folio, string Emisor, DateOnly FechaEmision, DateOnly? VigenteDesde, DateOnly? VigenteHasta, string? Notas, Guid? ReemplazaA = null);
public sealed class DocumentoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    public Guid AggregateRootId => ActivoId;
    public TipoDocumentoActivo Tipo { get; private set; }
    public Guid AdjuntoId { get; private set; }
    public string Folio { get; private set; } = "";
    public string Emisor { get; private set; } = "";
    public DateOnly FechaEmision { get; private set; }
    public DateOnly? VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public string? Notas { get; private set; }
    public Guid? ReemplazaA { get; private set; }
    public int NumeroVersion { get; private set; }
    public bool Reemplazado { get; private set; }
    private DocumentoActivo() { }
    public DocumentoActivo(Activo activo, DocumentoActivoDatos d, DocumentoActivo? anterior = null) : base(Guid.CreateVersion7())
    {
        if (!Enum.IsDefined(d.Tipo) || d.AdjuntoId == Guid.Empty || string.IsNullOrWhiteSpace(d.Folio) || d.Folio.Length > 100 || string.IsNullOrWhiteSpace(d.Emisor) || d.Emisor.Length > 200 || d.FechaEmision == default || d.Notas?.Length > 2000)
            throw new BusinessRuleException("AF_DOCUMENTO_INVALIDO", "Capture archivo, folio, emisor y fecha de emisión válidos.");
        if ((RequiereVigencia(d.Tipo) && (d.VigenteDesde == null || d.VigenteHasta == null)) || d.VigenteHasta < d.VigenteDesde)
            throw new BusinessRuleException("AF_VIGENCIA_REQUERIDA", "Seguro, garantía y arrendamiento requieren vigencia desde y hasta, en orden.");
        if (d.ReemplazaA != anterior?.Id || anterior != null && (anterior.ActivoId != activo.Id || anterior.Tipo != d.Tipo || anterior.Reemplazado || anterior.AdjuntoId == d.AdjuntoId))
            throw new BusinessRuleException("AF_REEMPLAZO_INVALIDO", "Seleccione una revisión vigente del mismo tipo y un archivo nuevo.");
        EmpresaId = activo.EmpresaId; ActivoId = activo.Id; Tipo = d.Tipo; AdjuntoId = d.AdjuntoId;
        Folio = d.Folio.Trim(); Emisor = d.Emisor.Trim(); FechaEmision = d.FechaEmision; VigenteDesde = d.VigenteDesde; VigenteHasta = d.VigenteHasta;
        Notas = d.Notas; ReemplazaA = d.ReemplazaA; NumeroVersion = (anterior?.NumeroVersion ?? 0) + 1;
        if (anterior != null) anterior.Reemplazado = true;
    }
    public static bool RequiereVigencia(TipoDocumentoActivo tipo) => tipo is TipoDocumentoActivo.Seguro or TipoDocumentoActivo.Garantia or TipoDocumentoActivo.Arrendamiento;
}
