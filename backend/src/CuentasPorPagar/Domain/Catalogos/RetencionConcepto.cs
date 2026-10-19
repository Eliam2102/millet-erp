using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Domain.Catalogos;

/// <summary>Matriz fiscal compartida. Son supuestos SAT sujetos a validación Fiscal (D03/D12).</summary>
public sealed class RetencionConcepto : BaseEntity, IAuditable
{
    public const string AvisoFiscal = "Supuesto SAT, valida Fiscal (D03)";
    public string Concepto { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public string Impuesto { get; private set; } = "";
    public decimal Tasa { get; private set; }
    public string Fuente { get; private set; } = "";
    public bool Activa { get; private set; }
    public string MotivoCambio { get; private set; } = "";
    private RetencionConcepto() { }
    public static RetencionConcepto Crear(string concepto, string descripcion, string impuesto, decimal tasa, string fuente, string motivo)
    {
        var r = new RetencionConcepto { Id = Guid.CreateVersion7() };
        r.Actualizar(concepto, descripcion, impuesto, tasa, fuente, true, motivo); return r;
    }
    public void Actualizar(string concepto, string descripcion, string impuesto, decimal tasa, string fuente, bool activa, string motivo)
    {
        if (string.IsNullOrWhiteSpace(concepto) || concepto.Trim().Length > 80 ||
            string.IsNullOrWhiteSpace(descripcion) || descripcion.Trim().Length > 300 ||
            impuesto is not ("001" or "002" or "003") || tasa is < 0 or > 1 ||
            string.IsNullOrWhiteSpace(fuente) || fuente.Length > 1000 ||
            string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length is < 10 or > 500)
            throw new BusinessRuleException("CXP_RETENCION_INVALIDA", "Revisa concepto, descripción, impuesto SAT, tasa (0–1), fuente y motivo (10–500 caracteres).");
        Concepto = concepto.Trim().ToUpperInvariant(); Descripcion = descripcion.Trim(); Impuesto = impuesto;
        Tasa = tasa; Fuente = fuente.Trim(); Activa = activa; MotivoCambio = motivo.Trim();
    }
}

public static class ComparadorRetenciones
{
    public static IReadOnlyList<RetencionCfdi> Proponer(decimal baseNeta, IEnumerable<RetencionConcepto> reglas) =>
        reglas.Where(r => r.Activa).Select(r => new RetencionCfdi(r.Impuesto, r.Tasa,
            decimal.Round(baseNeta * r.Tasa, 2, MidpointRounding.AwayFromZero))).ToArray();

    /// <summary>Concepto para proveedores sin retención (compra de bienes): espera cero retenciones.</summary>
    public const string ConceptoSinRetencion = "SIN_RETENCION";

    public static string? Alerta(string? concepto, decimal baseNeta, decimal total,
        IReadOnlyList<RetencionCfdi>? detalle, IReadOnlyList<RetencionConcepto> reglas)
    {
        if (concepto == ConceptoSinRetencion)
            return total > 0.01m || detalle?.Any(r => r.Importe > 0.01m) == true
                ? "El CFDI trae retenciones pero el proveedor está como «Sin retención». Fiscal debe revisarlas antes de autorizar el pago. " + RetencionConcepto.AvisoFiscal + "."
                : null;
        // Supuesto (noche, 17-oct): sin concepto y sin retenciones no se alerta; el proveedor da el concepto (R4).
        if (string.IsNullOrWhiteSpace(concepto) && total <= 0.01m && detalle?.Any(r => r.Importe > 0.01m) != true) return null;
        if (string.IsNullOrWhiteSpace(concepto)) return "Por confirmar: selecciona el concepto fiscal para comparar retenciones. " + RetencionConcepto.AvisoFiscal + ".";
        if (reglas.Count == 0) return "Por confirmar: no hay retenciones activas para este concepto. " + RetencionConcepto.AvisoFiscal + ".";
        var esperado = Proponer(baseNeta, reglas);
        var distinto = Math.Abs(esperado.Sum(r => r.Importe) - total) > 0.01m;
        if (detalle is not null)
        {
            distinto |= detalle.Any(r => !esperado.Any(e => e.Impuesto == r.Impuesto &&
                (r.Tasa is null || Math.Abs(e.Tasa!.Value - r.Tasa.Value) <= 0.000001m)));
            distinto |= esperado.Any(e => Math.Abs(e.Importe - detalle.Where(r => r.Impuesto == e.Impuesto &&
                (r.Tasa is null || Math.Abs(e.Tasa!.Value - r.Tasa.Value) <= 0.000001m)).Sum(r => r.Importe)) > 0.01m);
        }
        return distinto ? "Las retenciones capturadas o del CFDI difieren del catálogo del concepto. Fiscal debe revisarlas antes de autorizar el pago. " + RetencionConcepto.AvisoFiscal + "." : null;
    }
}
