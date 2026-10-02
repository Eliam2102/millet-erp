using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application;

/// <summary>
/// ÚNICO normalizador del catálogo (§20.2): lo usan el CRUD, la vista previa, el perfilado y
/// aplicar. Toda regla de formato sale de <see cref="CatalogoOpciones"/>; nada de Millet va en código.
/// </summary>
public sealed class FormatoCatalogo
{
    private static readonly Regex Espacios = new(@"[\s ]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly Regex _patron;
    private readonly Regex _separador;
    private readonly Dictionary<string, string> _columnas = [];
    private readonly Dictionary<string, NaturalezaCuenta> _naturalezas = [];
    private readonly Dictionary<string, TipoCuenta> _tipos = [];
    private readonly HashSet<string> _sinMapeo;

    public CatalogoOpciones Opciones { get; }

    public FormatoCatalogo(CatalogoOpciones opciones)
    {
        Opciones = opciones;
        _patron = new Regex(opciones.Codigo.Patron, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        var seps = string.Concat(opciones.Codigo.Separadores.Select(Regex.Escape));
        _separador = new Regex($"([{seps}])", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        foreach (var (canonica, alias) in opciones.Importacion.Columnas)
        {
            _columnas.TryAdd(canonica, canonica);
            foreach (var a in alias) _columnas.TryAdd(NormalizarCabecera(a), canonica);
        }
        foreach (var (valor, alias) in opciones.Naturaleza.Aliases)
        {
            var n = Enum.Parse<NaturalezaCuenta>(valor);
            _naturalezas.TryAdd(Clave(valor), n);
            foreach (var a in alias) _naturalezas.TryAdd(Clave(a), n);
        }
        foreach (var (valor, alias) in opciones.Tipo.Aliases)
        {
            var t = Enum.Parse<TipoCuenta>(valor);
            _tipos.TryAdd(Clave(valor), t);
            foreach (var a in alias) _tipos.TryAdd(Clave(a), t);
        }
        _sinMapeo = opciones.Importacion.ColumnasSinMapeo.Select(NormalizarCabecera).ToHashSet();
    }

    // ── Texto ────────────────────────────────────────────────────────────────

    /// <summary>Trim + colapso de espacios (incl. NBSP). Vacío ⇒ null.</summary>
    public static string? Texto(string? s)
    {
        if (s is null) return null;
        var t = Espacios.Replace(s.Trim('﻿', ' ', '\t', '\r', '\n', ' '), " ").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Sin acentos, minúsculas y trim; base de la comparación de alias.</summary>
    public static string Clave(string s)
    {
        var d = (Texto(s) ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToLowerInvariant();
    }

    public static string NormalizarCabecera(string s) =>
        Regex.Replace(Clave(s), @"[\s\-]+", "_", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim('_');

    // ── Columnas / enums ─────────────────────────────────────────────────────

    public string? ColumnaCanonica(string cabecera) =>
        _columnas.GetValueOrDefault(NormalizarCabecera(cabecera));

    public bool EsColumnaSinMapeo(string cabecera) => _sinMapeo.Contains(NormalizarCabecera(cabecera));

    public NaturalezaCuenta? ParseNaturaleza(string? s) =>
        s is not null && _naturalezas.TryGetValue(Clave(s), out var n) ? n : null;

    public TipoCuenta? ParseTipo(string? s) =>
        s is not null && _tipos.TryGetValue(Clave(s), out var t) ? t : null;

    public static CuentaControl? ParseControl(string? s) =>
        s is null ? null : Enum.GetValues<CuentaControl>().Cast<CuentaControl?>().FirstOrDefault(c => Clave(c.ToString()!) == Clave(s));

    public IReadOnlyList<string> AliasesColumna(string canonica) =>
        Opciones.Importacion.Columnas.GetValueOrDefault(canonica) ?? [];

    // ── Código ───────────────────────────────────────────────────────────────

    /// <summary>Trim, colapso de espacios y mayúsculas. Vacío ⇒ null.</summary>
    public static string? Codigo(string? s) => Texto(s)?.ToUpperInvariant();

    /// <summary>Motivo del rechazo (null = el código cumple longitud y patrón de configuración).</summary>
    public string? MotivoCodigoInvalido(string codigo)
    {
        if (codigo.Length < Opciones.Codigo.LongitudMin || codigo.Length > Opciones.Codigo.LongitudMax)
            return $"longitud {codigo.Length} fuera del rango {Opciones.Codigo.LongitudMin}-{Opciones.Codigo.LongitudMax}";
        return _patron.IsMatch(codigo) ? null : "no cumple el patrón configurado (Codigo.Patron)";
    }

    /// <summary>Tokens alternados segmento/separador; los segmentos están en posiciones pares.</summary>
    public string[] Tokens(string codigo) => _separador.Split(codigo);

    public int NumeroSegmentos(string codigo) => Tokens(codigo).Where((_, i) => i % 2 == 0).Count();

    private static bool EsNumerico(string t) => t.Length > 0 && t.All(char.IsAsciiDigit);

    /// <summary>
    /// Rellena con ceros a la izquierda los segmentos NUMÉRICOS que tengan longitud configurada
    /// (<c>Codigo.RellenoCeros</c>, por posición entre segmentos numéricos). Nunca en silencio:
    /// el llamador reporta <c>rellenado</c> como advertencia.
    /// </summary>
    public (string Codigo, bool Rellenado) Rellenar(string codigo)
    {
        var fijos = Opciones.Codigo.RellenoCeros;
        if (fijos.Count == 0) return (codigo, false);
        var tokens = Tokens(codigo);
        var k = 0;
        var cambio = false;
        for (var i = 0; i < tokens.Length; i += 2)
        {
            if (!EsNumerico(tokens[i])) continue;
            if (k < fijos.Count && tokens[i].Length < fijos[k])
            {
                tokens[i] = tokens[i].PadLeft(fijos[k], '0');
                cambio = true;
            }
            k++;
        }
        return (string.Concat(tokens), cambio);
    }

    /// <summary>
    /// P15: padre por segmentos = el código con el último segmento numérico distinto de cero puesto
    /// a ceros (ancho preservado). Los segmentos no numéricos son etiquetas y no cuentan. Raíz ⇒ null.
    /// </summary>
    public string? PadrePorSegmentos(string codigo)
    {
        var tokens = Tokens(codigo);
        var numericos = Enumerable.Range(0, tokens.Length).Where(i => i % 2 == 0 && EsNumerico(tokens[i])).ToList();
        var k = numericos.FindLastIndex(i => tokens[i].Any(c => c != '0'));
        if (k <= 0) return null;
        tokens[numericos[k]] = new string('0', tokens[numericos[k]].Length);
        return string.Concat(tokens);
    }
}
