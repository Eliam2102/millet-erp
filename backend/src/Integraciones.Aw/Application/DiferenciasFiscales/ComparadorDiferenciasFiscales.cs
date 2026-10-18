using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;

namespace Millet.Integraciones.Aw.Application.DiferenciasFiscales;

/// <summary>Campos fiscales que compara el reporte (U1.8b). El valor es el código estable de la API.</summary>
public static class CamposDiferenciaFiscal
{
    public const string RazonSocial = "RazonSocial";
    public const string Rfc = "Rfc";
    public const string RegimenFiscal = "RegimenFiscal";
    public const string CodigoPostalFiscal = "CodigoPostalFiscal";
    public const string ClaveProdServSat = "ClaveProdServSat";
    public const string ClaveUnidadSat = "ClaveUnidadSat";
    public const string ObjetoImp = "ObjetoImp";

    public static readonly IReadOnlyList<string> DeCliente = [RazonSocial, Rfc, RegimenFiscal, CodigoPostalFiscal];
    public static readonly IReadOnlyList<string> DeProducto = [ClaveProdServSat, ClaveUnidadSat, ObjetoImp];

    public static bool EsValido(TipoEntidadDiferenciaFiscal tipo, string campo) =>
        (tipo == TipoEntidadDiferenciaFiscal.Cliente ? DeCliente : DeProducto).Contains(campo);

    public static string Etiqueta(string campo) => campo switch
    {
        RazonSocial => "Razón social",
        Rfc => "RFC",
        RegimenFiscal => "Régimen fiscal",
        CodigoPostalFiscal => "CP fiscal",
        ClaveProdServSat => "Clave de producto SAT",
        ClaveUnidadSat => "Clave de unidad SAT",
        ObjetoImp => "Objeto de impuesto",
        _ => campo,
    };
}

/// <summary>
/// Resultado de comparar un campo. Diferente: ambos tienen valor y no coinciden. Faltante: el ERP no tiene
/// valor. SinDatoEnOrigen: solo clientes, el ERP tiene valor y A+W no.
/// </summary>
public enum EstadoDiferenciaFiscal { Diferente, Faltante, SinDatoEnOrigen }

public static class EstadoDiferenciaFiscalTexto
{
    public static string Etiqueta(EstadoDiferenciaFiscal e) => e switch
    {
        EstadoDiferenciaFiscal.Diferente => "Diferente",
        EstadoDiferenciaFiscal.Faltante => "Faltante",
        _ => "Sin dato en A+W",
    };
}

/// <summary>Una diferencia detectada. <see cref="Huella"/> identifica los valores comparados (ya normalizados).</summary>
public sealed record DiferenciaFiscalDetectada(
    TipoEntidadDiferenciaFiscal Tipo,
    Guid EntidadId,
    string Referencia,
    string Nombre,
    string Campo,
    string Fuente,
    string? ValorOrigen,
    string? ValorErp,
    EstadoDiferenciaFiscal Estado,
    string Huella);

/// <summary>Valores fiscales vigentes del cliente en el ERP (dueño del dato fiscal, regla P160).</summary>
public sealed record ClienteFiscalErp(Guid Id, string Referencia, string RazonSocial, string? Rfc, string? RegimenFiscal, string? CodigoPostalFiscal);

/// <summary>
/// Valores fiscales vigentes del producto A+W en el ERP. <see cref="DiferenciaIntencional"/> = la ficha es
/// excepción de producto o ya la confirmó Fiscal: una diferencia contra la regla es deliberada y no se reporta
/// (los faltantes sí).
/// </summary>
public sealed record ProductoFiscalErp(
    Guid Id, string Referencia, string Descripcion, string? ClaveProdServSat, string? ClaveUnidadSat, string? ObjetoImp,
    bool DiferenciaIntencional);

/// <summary>Valor esperado de un campo de producto y de dónde sale (regla fiscal o mapeo de unidad).</summary>
public sealed record ValorEsperado(string? Valor, string Fuente);

/// <summary>Lo esperado para un producto según su regla fiscal por familia y el mapeo de unidad A+W → SAT.</summary>
public sealed record EsperadoFiscalProducto(ValorEsperado ClaveProdServSat, ValorEsperado ClaveUnidadSat, ValorEsperado ObjetoImp);

/// <summary>
/// Comparación pura A+W/regla vs ERP del reporte de diferencias fiscales (U1.8b, F1-FAC-14). Sin E/S.
/// <para>Clientes (A+W vs ERP), reglas de Millet ("Documentación funcional de los sistemas satélite" §4.2):
/// razón social = NAME1 + NAME2; RFC = UST_ID sin guiones, puntos ni espacios; régimen = KA_BONITAET.FREMD_KEY;
/// CP = PLZ. El texto <c>&lt;indf&gt;</c> de A+W es "sin dato".</para>
/// <para>Normalización para decidir si son iguales (no cambia lo que se muestra): mayúsculas, sin acentos,
/// espacios colapsados; en razón social además se ignoran puntos y comas ("S.A." = "SA"), pero NO se quita el
/// régimen societario (decisión: para CFDI 4.0 importa y lo revisa una persona).</para>
/// <para>Productos (regla/mapeo vs ERP): A+W no tiene datos SAT. Campo vacío en el ERP = "Faltante" siempre;
/// un valor distinto del esperado = "Diferente", salvo excepción de producto o ficha confirmada.</para>
/// </summary>
public static class ComparadorDiferenciasFiscales
{
    public const string FuenteAw = "A+W";

    public static IReadOnlyList<DiferenciaFiscalDetectada> CompararCliente(ClienteFiscalErp erp, AwClienteOrigenFila aw)
    {
        var nombre = erp.RazonSocial;
        var resultado = new List<DiferenciaFiscalDetectada>(4);
        void Comparar(string campo, string? origen, string? local, Func<string?, string?> clave)
        {
            if (Evaluar(TipoEntidadDiferenciaFiscal.Cliente, erp.Id, erp.Referencia, nombre, campo, FuenteAw,
                    origen, local, clave(origen), clave(local), diferenciaIntencional: false) is { } d)
                resultado.Add(d);
        }

        Comparar(CamposDiferenciaFiscal.RazonSocial, RazonSocialAw(aw.Name1, aw.Name2), Limpiar(erp.RazonSocial), ClaveRazonSocial);
        Comparar(CamposDiferenciaFiscal.Rfc, NormalizarRfc(aw.UstId), NormalizarRfc(erp.Rfc), v => v);
        Comparar(CamposDiferenciaFiscal.RegimenFiscal, ClaveCodigo(aw.RegimenFiscalOrigen), ClaveCodigo(erp.RegimenFiscal), v => v);
        Comparar(CamposDiferenciaFiscal.CodigoPostalFiscal, ClaveCodigo(aw.Plz), ClaveCodigo(erp.CodigoPostalFiscal), v => v);
        return resultado;
    }

    public static IReadOnlyList<DiferenciaFiscalDetectada> CompararProducto(ProductoFiscalErp erp, EsperadoFiscalProducto esperado)
    {
        var resultado = new List<DiferenciaFiscalDetectada>(3);
        void Comparar(string campo, ValorEsperado e, string? local)
        {
            var origen = ClaveCodigo(e.Valor);
            var actual = ClaveCodigo(local);
            if (Evaluar(TipoEntidadDiferenciaFiscal.Producto, erp.Id, erp.Referencia, erp.Descripcion, campo, e.Fuente,
                    origen, actual, origen, actual, erp.DiferenciaIntencional) is { } d)
                resultado.Add(d);
        }

        Comparar(CamposDiferenciaFiscal.ClaveProdServSat, esperado.ClaveProdServSat, erp.ClaveProdServSat);
        Comparar(CamposDiferenciaFiscal.ClaveUnidadSat, esperado.ClaveUnidadSat, erp.ClaveUnidadSat);
        Comparar(CamposDiferenciaFiscal.ObjetoImp, esperado.ObjetoImp, erp.ObjetoImp);
        return resultado;
    }

    private static DiferenciaFiscalDetectada? Evaluar(
        TipoEntidadDiferenciaFiscal tipo, Guid id, string referencia, string nombre, string campo, string fuente,
        string? origen, string? local, string? claveOrigen, string? claveLocal, bool diferenciaIntencional)
    {
        EstadoDiferenciaFiscal? estado = (claveOrigen, claveLocal) switch
        {
            (_, null) when tipo == TipoEntidadDiferenciaFiscal.Producto => EstadoDiferenciaFiscal.Faltante,
            (null, null) => null,
            (not null, null) => EstadoDiferenciaFiscal.Faltante,
            // Producto sin regla/mapeo: no hay contra qué comparar lo capturado.
            (null, not null) when tipo == TipoEntidadDiferenciaFiscal.Producto => null,
            (null, not null) => EstadoDiferenciaFiscal.SinDatoEnOrigen,
            var (o, l) when string.Equals(o, l, StringComparison.Ordinal) => null,
            _ when diferenciaIntencional => null,
            _ => EstadoDiferenciaFiscal.Diferente,
        };
        return estado is { } e
            ? new(tipo, id, referencia, nombre, campo, fuente, origen, local, e, Huella(tipo, campo, claveOrigen, claveLocal))
            : null;
    }

    /// <summary>NAME1 + NAME2 de A+W (Millet §4.2), sin espacios sobrantes; <c>&lt;indf&gt;</c>/vacío = sin dato.</summary>
    public static string? RazonSocialAw(string? name1, string? name2) =>
        Limpiar(string.Join(' ', new[] { Limpiar(name1), Limpiar(name2) }.OfType<string>()));

    /// <summary>RFC sin guiones, puntos ni espacios (Millet §4.2), en mayúsculas; vacío/<c>&lt;indf&gt;</c> = null.</summary>
    public static string? NormalizarRfc(string? rfc) =>
        Limpiar(rfc) is { } r ? Limpiar(new string(r.Where(c => c is not ('-' or '.') && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant()) : null;

    /// <summary>Clave de comparación de razón social: sin acentos, mayúsculas, sin puntos/comas, espacios colapsados.</summary>
    public static string? ClaveRazonSocial(string? v)
    {
        if (Limpiar(v) is not { } s) return null;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '.' or ',') continue;
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToUpperInvariant(c));
        }
        return Limpiar(string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }

    /// <summary>Claves de catálogo (régimen, CP, claves SAT): recorte y mayúsculas.</summary>
    public static string? ClaveCodigo(string? v) => Limpiar(v)?.ToUpperInvariant();

    /// <summary>SHA-256 (hex) de tipo, campo y los dos valores normalizados: identifica "esta" diferencia.</summary>
    public static string Huella(TipoEntidadDiferenciaFiscal tipo, string campo, string? claveOrigen, string? claveErp)
    {
        // \u001f separa campos y \u0000 marca null para que "" y null no colisionen.
        var texto = string.Join('\u001f', tipo.ToString(), campo, claveOrigen ?? "\u0000", claveErp ?? "\u0000");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
    }

    /// <summary>Recorta; vacío o <c>&lt;indf&gt;</c> (indefinido de A+W) = null.</summary>
    public static string? Limpiar(string? v) =>
        string.IsNullOrWhiteSpace(v) || string.Equals(v.Trim(), "<indf>", StringComparison.OrdinalIgnoreCase) ? null : v.Trim();
}
