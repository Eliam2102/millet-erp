namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// V05/V52: códigos aprobados por Millet para ventas nuevas. Vacío por omisión;
/// true = bloqueado, false = desbloqueado. No se deduce significado de los números.
/// </summary>
public sealed class MapeoEstatusClientesAwOptions
{
    public const string SectionName = "IntegracionesAw:Clientes:MapeoEstatus";
    public string? Version { get; set; }
    public Dictionary<int, bool> KzStatus { get; set; } = [];
    public Dictionary<int, bool> KzGesperrt { get; set; } = [];
    public bool Habilitado => KzStatus.Count > 0 || KzGesperrt.Count > 0;

    // Ocho caracteres dejan espacio para la versión del mapper (0-borrador)
    // dentro de VersionMapeo (20 caracteres) del registro de sincronización.
    public bool EsValido => !Habilitado || (!string.IsNullOrWhiteSpace(Version)
        && Version.Length <= 8 && Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));

    public bool TieneCodigoSinMapeo(int? estado, int? bloqueo) => Habilitado
        ? estado is { } e && !KzStatus.ContainsKey(e) || bloqueo is { } b && !KzGesperrt.ContainsKey(b)
        // Conserva el diagnóstico previo: cualquier KZ_STATUS informado está
        // pendiente; un KZ_GESPERRT cero sin estado no abre un pendiente nuevo.
        : estado is not null || bloqueo is not null and not 0;

    /// <summary>
    /// Un bloqueo aprobado prevalece. Para reactivar no debe haber otro código
    /// informado sin equivalencia; NULL no constituye una señal de desbloqueo.
    /// </summary>
    public bool? Resolver(int? estado, int? bloqueo)
    {
        if (!Habilitado) return null;
        bool? e = estado is { } s && KzStatus.TryGetValue(s, out var se) ? se : null;
        bool? b = bloqueo is { } g && KzGesperrt.TryGetValue(g, out var sg) ? sg : null;
        if (e == true || b == true) return true;
        if (TieneCodigoSinMapeo(estado, bloqueo)) return null;
        return e == false || b == false ? false : null;
    }
}
