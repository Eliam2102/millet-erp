namespace Millet.ActivosFijos.Domain;

public sealed record TasasActivo(decimal? Contable, decimal? Fiscal, string? Observacion)
{
    public static TasasActivo Resolver(Activo a, ClaseActivo c, ClaseActivo? mejorada = null)
    {
        if (!c.Depreciable) return new(0, 0, "No depreciable");
        if (c.Clave == "160") return a.VidaUtilMeses is int meses
            ? new(decimal.Round(1200m / meses, 4), decimal.Round(1200m / meses, 4), null)
            : new(null, null, "Capture la vida útil");
        if (c.Clave == "170") return a.ActivoMejoradoId != null && mejorada != null && mejorada.Clave != "170"
            ? new(mejorada.Depreciable ? mejorada.TasaContableAnual : 0, mejorada.Depreciable ? mejorada.TasaFiscalAnual : 0,
                !mejorada.TasaConfirmada ? "Tasa por confirmar con Millet" : null)
            : new(null, null, "Indique el activo que mejora");
        return new(c.TasaContableAnual, c.TasaFiscalAnual, c.TasaFiscalAnual == null || c.TasaContableAnual == null
            ? "Tasa por confirmar: capture la tasa" : !c.TasaConfirmada
                ? "Tasa por confirmar con Millet" : null);
    }
}

public sealed record FilaFiscal(Guid ActivoId, string Numero, string Descripcion, string Clase, decimal MontoOriginalInversion,
    decimal? TasaFiscal, int MesesUso, decimal DeduccionHistorica, decimal? Factor, decimal? DeduccionActualizada,
    decimal DeduccionAcumuladaHistorica, string? Observacion);

public static class CalculoFiscal
{
    public static FilaFiscal Calcular(Activo a, ClaseActivo clase, int ejercicio, int mesCorte, bool mismoMes,
        IReadOnlyDictionary<DateOnly, decimal> inpc, ClaseActivo? claseMejorada = null)
    {
        var tasas = TasasActivo.Resolver(a, clase, claseMejorada);
        var corte = new DateOnly(ejercicio, mesCorte, 1);
        var inicio = a.InicioUso == null ? corte.AddMonths(1) : CalculoDepreciacion.PrimerMes(a, mismoMes);
        var fin = a.FechaBaja is DateOnly baja && CalculoDepreciacion.Mes(baja) <= corte ? CalculoDepreciacion.Mes(baja).AddMonths(-1) : corte;
        var primeroEjercicio = new DateOnly(ejercicio, 1, 1);
        var desde = inicio > primeroEjercicio ? inicio : primeroEjercicio;
        var meses = Math.Max(0, (fin.Year - desde.Year) * 12 + fin.Month - desde.Month + 1);
        decimal acumulada = 0, historica = 0;
        // Redondea al final del periodo fiscal, sin acumular el redondeo mensual de la póliza.
        if (tasas.Fiscal is decimal tasa && tasa > 0 && a.Estado != EstadoActivo.PorCapitalizar)
            for (var mes = inicio; mes <= fin; mes = mes.AddMonths(1))
            {
                var inversion = Math.Max(0, a.ValorOriginal + a.Historial.Where(h => CalculoDepreciacion.Mes(h.Fecha) < mes).Sum(h => h.Importe));
                var deduccion = Math.Min(Math.Max(0, inversion - acumulada), inversion * tasa / 1200m);
                acumulada += deduccion;
                if (mes.Year == ejercicio) historica += deduccion;
            }
        historica = decimal.Round(historica, 2, MidpointRounding.AwayFromZero);
        var moi = a.ValorOriginal + a.Historial.Where(h => h.Fecha <= CalculoDepreciacion.Cierre(corte)).Sum(h => h.Importe);
        decimal? factor = null, actualizada = null;
        var observacion = tasas.Observacion;
        if (historica > 0 && meses > 0)
        {
            var mitad = desde.AddMonths(Math.Max(0, meses / 2 - 1));
            if (inpc.TryGetValue(CalculoDepreciacion.Mes(a.FechaAdquisicion), out var compra) && inpc.TryGetValue(mitad, out var indice))
            { factor = Math.Max(1, decimal.Round(indice / compra, 4, MidpointRounding.AwayFromZero)); actualizada = decimal.Round(historica * factor.Value, 2, MidpointRounding.AwayFromZero); }
            else observacion = string.Join(" · ", new[] { observacion, "INPC faltante" }.Where(x => x != null));
        }
        else if (tasas.Fiscal != null) { factor = 1; actualizada = 0; }
        return new(a.Id, a.Numero, a.Descripcion, clase.Clave, moi, tasas.Fiscal, meses,
            historica, factor, actualizada,
            decimal.Round(acumulada, 2, MidpointRounding.AwayFromZero), observacion);
    }
}
