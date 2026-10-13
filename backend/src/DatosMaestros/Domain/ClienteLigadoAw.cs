using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.DatosMaestros.Domain;

public static class RfcCliente
{
    public static string Normalizar(string? rfc) => (rfc ?? string.Empty).Trim().ToUpperInvariant();
    public static bool EsLigable(string? rfc) => Normalizar(rfc) is not ("" or "XAXX010101000" or "XEXX010101000");
}

public sealed class ClienteLigadoAw : BaseEntity, IAuditable
{
    public string ReferenciaExterna { get; private set; } = string.Empty;
    public string Rfc { get; private set; } = string.Empty;
    public string SnapshotJson { get; private set; } = string.Empty;
    public Guid? ClienteId { get; private set; }
    public Guid? RevisadoPor { get; private set; }
    public DateTimeOffset? RevisadoEn { get; private set; }
    private ClienteLigadoAw() { }
    public ClienteLigadoAw(Guid id, string referencia, string rfc, string snapshotJson) : base(id)
    { ReferenciaExterna = referencia; Actualizar(rfc, snapshotJson); }
    public void Actualizar(string rfc, string snapshotJson)
    {
        if (!RfcCliente.EsLigable(rfc)) throw new BusinessRuleException("RFC_NO_LIGABLE", "Un RFC genérico nunca se liga con A+W.");
        if (RevisadoEn.HasValue) throw new BusinessRuleException("LIGADO_YA_REVISADO", "Este ligado ya fue revisado.");
        Rfc = RfcCliente.Normalizar(rfc); SnapshotJson = snapshotJson;
    }
    public void Revisar(Guid clienteId, Guid usuario, DateTimeOffset ahora)
    { ClienteId = clienteId; RevisadoPor = usuario; RevisadoEn = ahora; }
    public bool Descartado => RevisadoEn.HasValue && ClienteId is null;
    public void Descartar(Guid? usuario, DateTimeOffset ahora)
    { if (RevisadoEn.HasValue) throw new BusinessRuleException("LIGADO_YA_REVISADO", "Este ligado ya fue revisado."); ClienteId = null; RevisadoPor = usuario; RevisadoEn = ahora; }
}
