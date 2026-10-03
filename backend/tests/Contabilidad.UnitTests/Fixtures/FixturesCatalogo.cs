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

    /// <summary>
    /// FIX-formato-Laura: MISMO formato que la hoja «Plan de cuentas» del archivo de Contabilidad (encabezado de 8 columnas,
    /// filas vacías, títulos de reporte sin código, rubros) más la columna opcional «Cuenta padre» para el caso de padre
    /// explícito (fila 47 del real). Datos FICTICIOS; es la tabla que el cliente envía tras descartar la fila del nombre de empresa.
    /// <paramref name="prefijo"/> reemplaza «FIX» en los códigos (las pruebas HTTP usan un prefijo único).
    /// </summary>
    public static ImportacionRequest FormatoLaura(string prefijo = "FIX", string? fuente = null)
    {
        const string bal = "Estado de Posicion Financiera (Balance)", res = "Estado de Pérdidas y Ganancias (Resultado)", notas = "Notas a los Estados Financieros";
        string[] cols = ["Nivel Contable", "Numero", "Cuenta", "Tipo", "Naturaleza", "Reporte", "Nivel de cuenta SAT", "Código agrupador SAT", "Cuenta padre"];
        string?[][] filas =
        [
            ["", "FIX-100.00.00.00", "FIX ACTIVO", "Rubro", "", bal, "", "", ""],                                   // fila 2: rubro
            ["", "", "FIX Activo circulante", "Título", "", bal, "", "", ""],                                       // fila 3: título de reporte
            ["1", "FIX-101.00.00.00", "FIX Caja y bancos", "Activo circulante", "Deudora", bal, "1", "101", ""],
            ["2", "FIX-101.01.00.00", "FIX Bancos", "Activo circulante", "Deudora", bal, "2", "102", ""],             // nivel 2 con hijas: acumula
            ["3", "FIX-101.01.01.00", "FIX Banco uno", "Activo circulante", "Deudora", bal, "3", "102.01", ""],
            ["3", "FIX-101.01.02.00", "FIX Banco dos", "Activo circulante", "Deudora", bal, "3", "102.01", ""],
            ["2", "FIX-101.02.00.00", "FIX Caja chica", "Activo circulante", "Deudora", bal, "2", "101.01", ""],        // nivel 2 sin hijas: afectable
            ["", "", "", "", "", "", "", "", ""],                                                                     // vacía
            ["1", "FIX-102.00.00.00", "FIX Clientes", "Activo circulante", "Deudora", bal, "1", "105", ""],
            ["2", "FIX-102.01.00.00", "FIX Clientes nacionales", "Activo circulante", "Deudora", bal, "2", "105.01", ""],
            ["2", "FIX-102.02.00.00", "FIX Deudores diversos", "Activo circulante", "Deudora", bal, "2", "107.05", ""],
            ["1", "FIX-170.00.00.00", "FIX Activo fijo", "Activo fijo", "Deudora", bal, "1", "171", ""],
            ["2", "FIX-170.05.00.07", "FIX Cuenta con padre explícito", "Activo fijo", "Deudora", bal, "2", "171.01", "FIX-170.00.00.00"], // caso fila 47
            ["2", "FIX-170.01.00.00", "FIX Depreciación acumulada", "Activo fijo", "Acreedora", bal, "2", "171.02", ""],
            ["3", "FIX-170.01.00.001", "FIX Depreciación de equipo", "Activo fijo", "Acreedora", bal, "3", "171.02", "FIX-170.01.00.00"], // 13 caracteres tras el prefijo
            ["", "", "FIX Pasivo", "Titulo", "", bal, "", "", ""],                                                  // título de reporte
            ["1", "FIX-201.00.00.00", "FIX Proveedores", "Pasivo circulante", "Acreedora", bal, "1", "201", ""],
            ["2", "FIX-201.01.00.00", "FIX Proveedores nacionales", "Pasivo circulante", "Acreedora", bal, "2", "201.01", ""],
            ["2", "FIX-201.02.00.00", "FIX Acreedores diversos", "Pasivo circulante", "Acreedora", bal, "2", "205.06", ""],
            ["", "FIX-600.00.00.00", "FIX GASTOS", "Rubro", "", res, "", "", ""],
            ["", "", "FIX Gastos generales", "Título", "", res, "", "", ""],                                      // título de reporte
            ["1", "FIX-601.00.00.00", "FIX Gastos de operación", "Gastos", "Deudora", res, "1", "601", ""],
            ["2", "FIX-601.01.00.00", "FIX Sueldos y salarios", "Gastos", "Deudora", res, "2", "601.01", ""],      // acumula
            ["3", "FIX-601.01.01.00", "FIX Sueldos y salarios", "Gastos", "Deudora", res, "3", "601.01", ""],      // mismo nombre, afectable
            ["", "", "", "", "", "", "", "", ""],                                                                     // vacía
            ["", "FIX-700.00.00.00", "FIX CUENTAS DE ORDEN", "Acumula rubro", "", notas, "", "", ""],
            ["1", "FIX-701.00.00.00", "FIX Valores en custodia", "Cuentas de orden", "", notas, "1", "", ""],    // sin naturaleza: pendiente
            ["2", "FIX-701.01.00.00", "FIX Valores recibidos", "Otros gastos", "", notas, "2", "", ""],          // sin naturaleza: pendiente
        ];
        string? P(string? c) => c is null || prefijo == "FIX" ? c : c.Replace("FIX-", $"{prefijo}-", StringComparison.Ordinal);
        return new(fuente, "FIX-formato-Laura.xlsx", null, cols,
            [.. filas.Select(f => (IReadOnlyList<string?>)[.. f.Select((c, i) => c == "" ? null : i is 1 or 8 ? P(c) : c)])], null);
    }

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

    /// <summary>Configuración por defecto con la derivación de tipo APAGADA (comportamiento anterior a P19: tipo explícito).</summary>
    public static CatalogoOpciones SinDerivar()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.Tipo.DerivarPorJerarquia = false;
        return o;
    }

    public static ResultadoAnalisis Analizar(string csv, CatalogoOpciones? opciones = null, ExistenteCatalogo? ex = null, string? fuente = null) =>
        Analizar(Utf8(csv), opciones, ex, fuente);

    public static ResultadoAnalisis Analizar(byte[] bytes, CatalogoOpciones? opciones = null, ExistenteCatalogo? ex = null, string? fuente = null)
    {
        var f = new FormatoCatalogo(opciones ?? CatalogoOpciones.Predeterminadas());
        return new ImportadorCatalogo(f).Analizar(LectorTabla.Leer(Request(bytes)), fuente, ex ?? ExistenteCatalogo.Vacio);
    }
}
