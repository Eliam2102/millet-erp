using System.Globalization;
using System.Text;

namespace Millet.DatosMaestros.Domain;

public sealed record ClienteSap(string CardCode, string Nombre, string? Rfc, string? Moneda = null, decimal? Saldo = null);
public sealed record ClienteErpRef(Guid Id, string? ReferenciaExterna, string RazonSocial, string? Rfc);
public sealed record LigadoSap(ClienteSap Sap, Guid ClienteId);
public sealed record RevisionSap(ClienteSap Sap, Guid ClienteId, string Motivo);
public sealed record ReporteEquivalenciaSap(IReadOnlyList<LigadoSap> Ligados, IReadOnlyList<RevisionSap> ARevision, IReadOnlyList<ClienteSap> SinCliente);

/// <summary>
/// Brecha 10 (86e3x4g77): CardCode de SAP = <c>Cliente.ReferenciaExterna</c> (KU_KUNDEN.ID de A+W). Se liga solo con la misma
/// clave y (RFC no genérico igual o nombre igual); clave existente sin coincidencia va a revisión; clave inexistente, a sin cliente.
/// El ligado apunta al cliente que tiene la clave aunque esté unificado en un principal: el saldo (y su moneda) no se mueve al grupo.
/// </summary>
public static class EquivalenciaClientesSap
{
    public static ReporteEquivalenciaSap Clasificar(IEnumerable<ClienteSap> sap, IEnumerable<ClienteErpRef> erp)
    {
        var porClave = erp.Where(c => !string.IsNullOrWhiteSpace(c.ReferenciaExterna))
            .ToDictionary(c => c.ReferenciaExterna!.Trim(), StringComparer.OrdinalIgnoreCase);
        List<LigadoSap> ligados = []; List<RevisionSap> revision = []; List<ClienteSap> sinCliente = [];
        foreach (var s in sap)
        {
            if (!porClave.TryGetValue(s.CardCode.Trim(), out var c)) { sinCliente.Add(s); continue; }
            var rfc = RfcCliente.EsLigable(s.Rfc) && RfcCliente.EsLigable(c.Rfc) && RfcCliente.Normalizar(s.Rfc) == RfcCliente.Normalizar(c.Rfc);
            if (rfc || Nombre(s.Nombre) is { Length: > 0 } n && n == Nombre(c.RazonSocial)) ligados.Add(new(s, c.Id));
            else revision.Add(new(s, c.Id, "La clave existe pero no coincide el RFC ni el nombre."));
        }
        return new(ligados, revision, sinCliente);
    }

    private static string Nombre(string texto) => string.Concat(texto.Normalize(NormalizationForm.FormD)
        .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))).ToLowerInvariant();
}
