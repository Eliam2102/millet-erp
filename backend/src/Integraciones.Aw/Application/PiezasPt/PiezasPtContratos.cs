namespace Millet.Integraciones.Aw.Application.PiezasPt;

/// <summary>Escaneo de pieza terminada leído del origen A+W (solo lectura).</summary>
public sealed record EscaneoOrigen(string Etiqueta, string Auftnr, int? AeIntpos, int? UPos, string? TeileNr, int Esnr,
    bool Fertig, string? Area, DateTime BearbZeit, string? ClaveSucursalAw);

/// <summary>Lector de escaneos de PT del origen activo (Demo o Real). Nunca escribe en A+W.</summary>
public interface IPiezasPtOrigenReader
{
    /// <summary>"Demo" o "Real".</summary>
    Task<string> OrigenActualAsync(CancellationToken ct);

    /// <summary>Escaneos con <c>bearb_zeit &gt;= desde</c>, ordenados por (bearb_zeit, etikettnr), tras el cursor opcional.</summary>
    Task<IReadOnlyList<EscaneoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? despuesDe,
        IReadOnlyList<(int Desde, int Hasta)> estaciones, int max, CancellationToken ct);
}
