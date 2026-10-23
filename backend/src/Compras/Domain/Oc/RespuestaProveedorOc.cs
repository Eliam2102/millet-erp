using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Respuesta del proveedor sobre una OC autorizada (F1-COM-11). Historial inmutable:
/// una nueva fecha prometida es otra respuesta y esta se conserva.
/// </summary>
public sealed class RespuestaProveedorOc : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;
    public int Numero { get; private set; }
    public TipoRespuestaProveedorOc? Tipo { get; private set; }
    public DateOnly? FechaRespuesta { get; private set; }
    public string? Evidencia { get; private set; } = "";
    public string? ContactoProveedor { get; private set; }
    public string? Motivo { get; private set; }
    public bool TieneDiferencias { get; private set; }
    public Guid RegistradaPorId { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    private readonly List<CompromisoEntregaOc> _compromisos = [];
    public IReadOnlyCollection<CompromisoEntregaOc> Compromisos => _compromisos.AsReadOnly();

    private RespuestaProveedorOc() { }

    internal RespuestaProveedorOc(Guid id, Guid ordenCompraId, int numero, TipoRespuestaProveedorOc? tipo,
        DateOnly? fechaRespuesta, string? evidencia, string? contactoProveedor, string? motivo,
        bool tieneDiferencias, Guid registradaPorId, DateTimeOffset registradaEn,
        IEnumerable<CompromisoEntregaPropuesto> compromisos) : base(id)
    {
        OrdenCompraId = ordenCompraId;
        Numero = numero;
        Tipo = tipo;
        FechaRespuesta = fechaRespuesta;
        Evidencia = string.IsNullOrWhiteSpace(evidencia) ? null : evidencia.Trim();
        ContactoProveedor = string.IsNullOrWhiteSpace(contactoProveedor) ? null : contactoProveedor.Trim();
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        TieneDiferencias = tieneDiferencias;
        RegistradaPorId = registradaPorId;
        RegistradaEn = registradaEn;
        foreach (var c in compromisos)
            _compromisos.Add(new CompromisoEntregaOc(Guid.CreateVersion7(), id, ordenCompraId,
                c.LineaOrdenCompraId, c.Cantidad, c.FechaPrometida));
    }
}
