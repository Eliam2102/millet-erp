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

    private static bool EsCeros(string t) => EsNumerico(t) && t.All(c => c == '0');

    /// <summary>Parte significativa: el código sin los segmentos numéricos finales en cero (<c>100.10.00.00</c> ⇒ <c>100.10</c>).</summary>
    public string PrefijoSignificativo(string codigo)
    {
        var t = Tokens(codigo);
        var fin = t.Length;
        while (fin >= 3 && EsCeros(t[fin - 1])) fin -= 2;
        return string.Concat(t[..fin]);
    }

    /// <summary>
    /// El código es hija directa por rama del padre: empieza con su parte significativa seguida de un separador
    /// y agrega exactamente un nivel significativo. Vale para ancho fijo (<c>100.10.30.00</c> bajo <c>100.10.00.00</c>)
    /// y para ancho libre (<c>4.1.1</c> bajo <c>4.1</c>).
    /// </summary>
    public bool EstaEnRama(string codigo, string codigoPadre)
    {
        var padre = PrefijoSignificativo(codigoPadre);
        var hija = PrefijoSignificativo(codigo);
        if (!hija.StartsWith(padre, StringComparison.Ordinal) || hija.Length <= padre.Length) return false;
        var resto = hija[padre.Length..];
        return Opciones.Codigo.Separadores.Any(s => resto.StartsWith(s, StringComparison.Ordinal))
            && Tokens(hija).Length == Tokens(padre).Length + 2;
    }

    /// <summary>
    /// Opción 2 (alta manual): siguiente código hijo libre de un padre. Ancho fijo: llena el primer segmento en cero
    /// con el mayor valor usado + 1 (mismo ancho). Ancho libre: agrega un segmento con el formato de las hermanas.
    /// <paramref name="existentes"/> debe incluir las inactivas (el código no se reutiliza, P9). Null + motivo si no se puede inferir.
    /// </summary>
    public (string? Codigo, string? Motivo) SiguienteHijo(string codigoPadre, IReadOnlyCollection<string> existentes)
    {
        var t = Tokens(codigoPadre);
        var nPrefijo = Tokens(PrefijoSignificativo(codigoPadre)).Length;
        var usados = existentes.ToHashSet(StringComparer.Ordinal);

        if (nPrefijo < t.Length)
        {
            // Ancho fijo: el segmento a llenar es el primero en cero tras la parte significativa.
            var idx = nPrefijo + 1;
            var ancho = t[idx].Length;
            var valores = existentes
                .Select(Tokens)
                .Where(e => e.Length == t.Length && e.Take(idx).SequenceEqual(t.Take(idx)) && EsNumerico(e[idx]) && !EsCeros(e[idx])
                    && e.Skip(idx + 1).Where((_, i) => i % 2 == 1).All(EsCeros))
                .Select(e => int.Parse(e[idx], CultureInfo.InvariantCulture));
            for (var n = valores.DefaultIfEmpty(0).Max() + 1; ; n++)
            {
                var seg = n.ToString(CultureInfo.InvariantCulture).PadLeft(ancho, '0');
                if (seg.Length > ancho)
                    return (null, $"La rama de {codigoPadre} ya no tiene códigos libres en ese nivel; escriba el código.");
                var candidato = string.Concat(t.Take(idx).Append(seg).Concat(t.Skip(idx + 1)));
                if (!usados.Contains(candidato)) return (candidato, null);
            }
        }

        // Ancho libre: prefijo + separador + número, con el separador y ancho de las hermanas existentes.
        var hermanas = existentes.Select(Tokens)
            .Where(e => e.Length == t.Length + 2 && e.Take(t.Length).SequenceEqual(t) && EsNumerico(e[^1]))
            .ToList();
        if (hermanas.Count == 0)
            return (null, $"{codigoPadre} aún no tiene cuentas hijas para deducir el formato; escriba el código.");
        var sep = hermanas[0][^2];
        var anchoLibre = hermanas.Max(e => e[^1].Length);
        for (var n = hermanas.Max(e => int.Parse(e[^1], CultureInfo.InvariantCulture)) + 1; ; n++)
        {
            var candidato = codigoPadre + sep + n.ToString(CultureInfo.InvariantCulture).PadLeft(anchoLibre, '0');
            if (!usados.Contains(candidato)) return (candidato, null);
        }
    }
}
