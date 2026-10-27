using System.Globalization;

namespace Millet.Documentos;

public static class ImporteConLetra
{
    private const decimal Maximo = 999_999_999_999.99m;
    private static readonly string[] UnidadesEs = ["CERO", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIÚN", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
    private static readonly string[] DecenasEs = ["", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];
    private static readonly string[] CentenasEs = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];
    private static readonly string[] UnidadesEn = ["ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN"];
    private static readonly string[] DecenasEn = ["", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY"];

    public static string Espanol(decimal importe, string moneda)
    {
        var (entero, centavos, usd) = Preparar(importe, moneda);
        var unidad = usd ? (entero == 1 ? "DÓLAR" : "DÓLARES") : (entero == 1 ? "PESO" : "PESOS");
        var enlace = entero >= 1_000_000 && entero % 1_000_000 == 0 ? " DE " : " ";
        return $"{NumeroEs(entero)}{enlace}{unidad} {centavos:00}/100 {(usd ? "USD" : "M.N.")}";
    }

    public static string Ingles(decimal importe, string moneda)
    {
        var (entero, centavos, usd) = Preparar(importe, moneda);
        var unidad = usd ? (entero == 1 ? "DOLLAR" : "DOLLARS") : (entero == 1 ? "PESO" : "PESOS");
        return $"{NumeroEn(entero)} {unidad} {centavos:00}/100 {(usd ? "USD" : "M.N.")}";
    }

    private static (long Entero, int Centavos, bool Usd) Preparar(decimal importe, string moneda)
    {
        var usd = moneda?.Trim().ToUpperInvariant() switch
        {
            "MXN" => false,
            "USD" => true,
            _ => throw new ArgumentException("La moneda no está soportada. Use MXN o USD.", nameof(moneda))
        };
        if (importe < 0 || importe > Maximo)
            throw new ArgumentOutOfRangeException(nameof(importe), "El importe debe estar entre 0 y 999,999,999,999.99.");
        var redondeado = decimal.Round(importe, 2, MidpointRounding.AwayFromZero);
        var entero = (long)redondeado;
        return (entero, (int)((redondeado - entero) * 100), usd);
    }

    private static string NumeroEs(long n)
    {
        if (n < 30) return UnidadesEs[n];
        if (n < 100) return DecenasEs[n / 10] + (n % 10 == 0 ? "" : " Y " + NumeroEs(n % 10));
        if (n == 100) return "CIEN";
        if (n < 1000) return CentenasEs[n / 100] + RestoEs(n % 100);
        if (n < 1_000_000) return (n / 1000 == 1 ? "MIL" : NumeroEs(n / 1000) + " MIL") + RestoEs(n % 1000);
        return (n / 1_000_000 == 1 ? "UN MILLÓN" : NumeroEs(n / 1_000_000) + " MILLONES") + RestoEs(n % 1_000_000);
    }

    private static string RestoEs(long n) => n == 0 ? "" : " " + NumeroEs(n);

    private static string NumeroEn(long n)
    {
        if (n < 20) return UnidadesEn[n];
        if (n < 100) return DecenasEn[n / 10] + (n % 10 == 0 ? "" : "-" + NumeroEn(n % 10));
        if (n < 1000) return UnidadesEn[n / 100] + " HUNDRED" + RestoEn(n % 100);
        foreach (var (divisor, nombre) in new[] { (1_000_000_000L, "BILLION"), (1_000_000L, "MILLION"), (1000L, "THOUSAND") })
            if (n >= divisor) return NumeroEn(n / divisor) + " " + nombre + RestoEn(n % divisor);
        throw new InvalidOperationException(n.ToString(CultureInfo.InvariantCulture));
    }

    private static string RestoEn(long n) => n == 0 ? "" : " " + NumeroEn(n);
}
