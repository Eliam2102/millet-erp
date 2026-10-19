using System.Text.RegularExpressions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.DatosMaestros.Domain;

public static partial class RfcCliente
{
    public static string Normalizar(string? rfc) => (rfc ?? string.Empty).Trim().ToUpperInvariant();
    public static bool EsLigable(string? rfc) => Normalizar(rfc) is not ("" or "XAXX010101000" or "XEXX010101000");

    /// <summary>Clave de ligado (M2.2): RFC o UST_ID sin espacios, guiones ni puntos, con forma de RFC y no genérico; null si no sirve para ligar.</summary>
    public static string? ClaveLigado(string? valor)
    {
        var v = Normalizar(valor).Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
        return EsLigable(v) && FormaRfc().IsMatch(v) ? v : null;
    }

    [GeneratedRegex(@"^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaRfc();
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
}
