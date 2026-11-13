using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// Cancelaciones que el receptor no tiene que aceptar (regla fiscal): total hasta 1,000 MXN, comprobantes
/// de egreso o traslado, dentro de las 24 h del timbrado, público en general y receptor extranjero.
/// La nómina no la emite el ERP. Función pura: solo informa; el SAT decide.
/// </summary>
public static class PoliticaAceptacionCancelacion
{
    public const decimal TopeSinAceptacionMxn = 1000m;

    public sealed record Resultado(bool RequiereAceptacion, IReadOnlyList<string> RazonesSinAceptacion);

    public static Resultado Evaluar(Comprobante c, DateTimeOffset ahora)
    {
        var razones = new List<string>();
        var importeMxn = Math.Round(c.Total * (c.TipoCambio ?? 1m), 2, MidpointRounding.AwayFromZero);
        if (importeMxn <= TopeSinAceptacionMxn) razones.Add("Total de 1,000 MXN o menos");
        if (c.Tipo == TipoComprobante.Egreso) razones.Add("Comprobante de egreso");
        if (c.Tipo == TipoComprobante.Traslado) razones.Add("Comprobante de traslado");
        if (c.FechaTimbrado is { } timbrado && ahora - timbrado <= TimeSpan.FromHours(24))
            razones.Add("Dentro de las 24 horas del timbrado");
        if (c.ReceptorRfc == DatosFiscalesReceptor.RfcGenericoNacional) razones.Add("Público en general");
        if (c.ReceptorRfc == DatosFiscalesReceptor.RfcGenericoExtranjero || (c.ReceptorPais.Length > 0 && c.ReceptorPais != "MEX"))
            razones.Add("Receptor extranjero");
        return new Resultado(razones.Count == 0, razones);
    }
}
