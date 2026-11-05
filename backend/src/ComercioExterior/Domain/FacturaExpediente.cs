using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed class FacturaExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid PedidoFacturableId { get; private set; }
    public int VersionEmbarque { get; private set; }
    public string Uuid { get; private set; } = "";
    public string Folio { get; private set; } = "";
    public DateTimeOffset? FechaTimbrado { get; private set; }
    public bool Cancelada { get; private set; }
    public string? UuidOriginal { get; private set; }
    public DateTimeOffset ActualizadaEn { get; private set; }
    private FacturaExpediente() { }
    internal FacturaExpediente(Guid factura, Expediente e, Guid pedido, int version, string uuid, string folio, DateTimeOffset? timbrado, bool cancelada, string? original, DateTimeOffset ocurrido) : base(factura)
    {
        EmpresaId = e.EmpresaId; ExpedienteId = e.Id; PedidoFacturableId = pedido; VersionEmbarque = version;
        Uuid = uuid; Folio = folio; FechaTimbrado = timbrado; Cancelada = cancelada; UuidOriginal = original; ActualizadaEn = ocurrido;
    }
    internal bool Actualizar(bool cancelada, DateTimeOffset ocurrido)
    {
        if (ocurrido < ActualizadaEn || Cancelada || Cancelada == cancelada) return false;
        Cancelada = cancelada; ActualizadaEn = ocurrido; return true;
    }
}
public sealed partial class Expediente
{
    private readonly List<FacturaExpediente> _facturas = [];
    public IReadOnlyCollection<FacturaExpediente> Facturas => _facturas.AsReadOnly();
    public void RegistrarFactura(Guid factura, Guid pedido, int version, string uuid, string folio, DateTimeOffset? timbrado, bool cancelada, string? original, DateTimeOffset ocurrido)
    {
        if (factura == Guid.Empty || pedido == Guid.Empty || version < 1 || string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(folio))
            throw new BusinessRuleException("CE_FACTURA_INVALIDA", "El evento de factura requiere factura, pedido, versión del embarque, UUID y folio.");
        var actual = _facturas.SingleOrDefault(f => f.Id == factura);
        if (actual is null) _facturas.Add(new(factura, this, pedido, version, uuid, folio, timbrado, cancelada, original, ocurrido));
        else if (!actual.Actualizar(cancelada, ocurrido)) return;
        RevisionContenido++;
    }
}
