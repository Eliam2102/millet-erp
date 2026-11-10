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

/// <summary>Una fila de <c>vw_erp_albaran_pos</c>: albarán de A+W de una posición, ya referida al pedido donde se produjeron las piezas.</summary>
public sealed record AlbaranPosicionOrigen(string NumeroPedido, int Posicion, int Folio, DateOnly Fecha, decimal Piezas);

/// <summary>Lector de albaranes de A+W del origen activo (Demo o Real). Solo SELECT; nunca escribe en A+W.</summary>
public interface IAlbaranesAwOrigenReader
{
    /// <summary>"Demo" o "Real".</summary>
    Task<string> OrigenActualAsync(CancellationToken ct);

    /// <summary>Todos los albaranes de las posiciones de pedidos con algún albarán de fecha &gt;= <paramref name="desde"/>.</summary>
    Task<IReadOnlyList<AlbaranPosicionOrigen>> LeerAsync(DateOnly desde, CancellationToken ct);
}
