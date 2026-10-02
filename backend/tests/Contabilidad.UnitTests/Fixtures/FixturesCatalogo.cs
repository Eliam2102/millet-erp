#pragma warning disable CA1822 // fixtures: helpers de instancia por ergonomía en las pruebas
using System.Text;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;

namespace Millet.Contabilidad.UnitTests.Fixtures;

/// <summary>
/// Fixtures FIX-* — DATOS FICTICIOS Y NO REALES. Ningún código, nombre ni formato de aquí es de Millet.
/// Este archivo también se enlaza (Compile Link) en Api.IntegrationTests para usar los mismos casos.
/// </summary>
public static class FixturesCatalogo
{
    /// <summary>Una configuración de formato ficticia y los códigos que le corresponden (mismos casos para todas).</summary>
    public sealed record FormatoPrueba(
        string Nombre, Func<CatalogoOpciones> Config, string Raiz, string Titulo, string Hoja1, string Hoja2,
        string FueraDePatron, string SinCeros, string ConCeros, bool PadreExplicito)
    {
        public override string ToString() => Nombre;

        /// <summary>CSV (columnas canónicas) con raíz, título, 2 afectables. El padre va explícito solo si el formato lo exige.</summary>
        public string Csv(params (string Codigo, string Nombre, string? Padre, string Nat, string Tipo)[] filas) =>
            "codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\n"
            + string.Join('\n', filas.Select(f => $"{f.Codigo};{f.Nombre};{f.Padre};{f.Nat};{f.Tipo}"));

        public string CsvFeliz() => Csv(
            (Raiz, "FIX Raiz", Pad(null), "Deudora", "Titulo"),
            (Titulo, "FIX Titulo", Pad(Raiz), "Deudora", "Titulo"),
            (Hoja1, "FIX Hoja 1", Pad(Titulo), "Deudora", "Afectable"),
            (Hoja2, "FIX Hoja 2", Pad(Titulo), "Acreedora", "Afectable"));

        public string? Pad(string? padre) => PadreExplicito ? padre : null;
    }

    private static CatalogoOpciones Opc(Action<CatalogoOpciones>? ajustar = null)
    {
        var o = new CatalogoOpciones();
        ajustar?.Invoke(o);
        o.AplicarDefaults();
        return o;
    }

    /// <summary>Tres formatos ficticios: segmentos fijos con prefijo, numérico fijo (padre por columna) y 9.99.99.</summary>
    public static IEnumerable<FormatoPrueba> Formatos { get; } =
    [
        new("A: prefijo + 9.99.99.99 por segmentos", () => Opc(o => o.Codigo.RellenoCeros = [3, 2, 2, 2]),
            "FIX-100.00.00.00", "FIX-100.10.00.00", "FIX-100.10.01.00", "FIX-100.10.02.00",
            "FIX 100 10", "FIX-100.1.00.00", "FIX-100.10.01.00", PadreExplicito: false),
        new("B: numérico de 4 dígitos, padre por columna", () => Opc(o =>
            {
                o.Codigo.Patron = @"^\d{4}$";
                o.Codigo.LongitudMin = 4; o.Codigo.LongitudMax = 4;
                o.Codigo.RellenoCeros = [4];
                o.Jerarquia.Modo = CatalogoOpciones.ModoPorColumna;
            }),
            "1000", "1100", "1101", "1102", "11A1", "101", "0101", PadreExplicito: true),
        new("C: 9.99.99 por segmentos", () => Opc(o =>
            {
                o.Codigo.Patron = @"^\d\.\d{2}\.\d{2}$";
                o.Codigo.LongitudMin = 7; o.Codigo.LongitudMax = 7;
                o.Codigo.RellenoCeros = [1, 2, 2];
            }),
            "1.00.00", "1.10.00", "1.10.01", "1.10.02", "1.10", "1.1.1", "1.01.01", PadreExplicito: false),
    ];

    public static IEnumerable<object[]> FormatosTeoria() => Formatos.Select(f => new object[] { f });

    // ── Archivos (FIX-*) ─────────────────────────────────────────────────────

    public static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);
    public static byte[] Utf8ConBom(string s) => [0xEF, 0xBB, 0xBF, .. Utf8(s)];
    public static byte[] Utf16ConBom(string s) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(s)];

    static FixturesCatalogo() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static byte[] Windows1252(string s) => Encoding.GetEncoding(1252).GetBytes(s);

    public static ImportacionRequest Request(byte[] bytes, string? fuente = null, string? huella = null) =>
        new(fuente, "FIX-archivo.csv", Convert.ToBase64String(bytes), null, null, huella);

    public static ImportacionRequest Request(string csv, string? fuente = null, string? huella = null) =>
        Request(Utf8(csv), fuente, huella);

    /// <summary>FIX-hoja-base: mismas CABECERAS que la hoja base provisional (P16) pero con datos ficticios.</summary>
    public static string HojaBase() =>
        "Número;Cuenta;Código agrupador SAT;Nivel Contable;Nivel de cuenta SAT;Tipo\n"
        + "FIX-100.00.00.00;FIX Activo;100;1;1;FIX-CATEGORIA-A\n"
        + "FIX-100.10.00.00;FIX Activo circulante;100.01;2;2;FIX-CATEGORIA-A\n"
        + "FIX-100.10.01.00;FIX Caja;100.01;3;3;FIX-CATEGORIA-B\n"
        + "FIX-100.10.02.00;FIX Bancos;100.01;2;3;FIX-CATEGORIA-B\n"      // nivel contable discrepante (3 derivado)
        + "FIX-100.10.02.005;FIX Cuenta de ancho distinto;100.01;4;3;\n"; // último segmento de 3 dígitos

    public static string Sucio_Vacias() => "codigo;nombre;naturaleza;tipo_cuenta\n\n;;;\nFIX-1;FIX Uno;Deudora;Afectable\n;;;\n";

    /// <summary>Generador determinista de n filas (FIX-NNN.BB.00.00; B=00 es el título del bloque de 50).</summary>
    public static string Volumen(int n, string prefijo = "FIX")
    {
        var sb = new StringBuilder("codigo;nombre;naturaleza;tipo_cuenta\n");
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (i / 50, i % 50);
            sb.Append($"{prefijo}-{a:000}.{b:00}.00.00;FIX Cuenta {i};Deudora;{(b == 0 ? "Titulo" : "Afectable")}\n");
        }
        return sb.ToString();
    }

    // ── Atajos ───────────────────────────────────────────────────────────────

    public static ResultadoAnalisis Analizar(string csv, CatalogoOpciones? opciones = null, ExistenteCatalogo? ex = null, string? fuente = null) =>
        Analizar(Utf8(csv), opciones, ex, fuente);

    public static ResultadoAnalisis Analizar(byte[] bytes, CatalogoOpciones? opciones = null, ExistenteCatalogo? ex = null, string? fuente = null)
    {
        var f = new FormatoCatalogo(opciones ?? CatalogoOpciones.Predeterminadas());
        return new ImportadorCatalogo(f).Analizar(LectorTabla.Leer(Request(bytes)), fuente, ex ?? ExistenteCatalogo.Vacio);
    }
}
