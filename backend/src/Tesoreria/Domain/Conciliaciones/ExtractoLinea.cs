using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// Línea importada de un extracto bancario (§5 DDL extracto_linea).
/// </summary>
public sealed class ExtractoLinea : BaseEntity, IAuditable
{
    public Guid ConciliacionId { get; private set; }
    public int NumeroLinea { get; private set; }
    public DateOnly Fecha { get; private set; }
    public string Descripcion { get; private set; } = default!;
    public string? Referencia { get; private set; }
    public decimal? Cargo { get; private set; }
    public decimal? Abono { get; private set; }
    public ExtractoFuente Fuente { get; private set; } = ExtractoFuente.Archivo;
    public EstadoExtractoLinea Estado { get; private set; } = EstadoExtractoLinea.Pendiente;
    public MatchTipo? MatchTipo { get; private set; }

    /// <summary>Id del movimiento principal emparejado (para búsquedas rápidas y compatibilidad 1:1).</summary>
    public Guid? MatchMovimientoId { get; private set; }

    /// <summary>Firma o hash único de la línea para deduplicar al reimportar (CA9.5).</summary>
    public string? LineaHash { get; private set; }

    // Partidas en tránsito (P9.4)
    public bool PartidaTransito { get; private set; }
    public PartidaTransitoTipo? PartidaTransitoTipo { get; private set; }
    public string? PartidaTransitoResponsable { get; private set; }
    public Guid? PartidaTransitoAutorizadaPor { get; private set; }

    /// <summary>Candidatos sugeridos cuando la línea entra a revisión por ambigüedad (>1 opción).</summary>
    public List<Guid> CandidatosRevisionIds { get; private set; } = new();

    private readonly List<ExtractoLineaMatch> _matches = new();
    public IReadOnlyCollection<ExtractoLineaMatch> Matches => _matches.AsReadOnly();

    public decimal Monto => (Cargo ?? Abono) ?? 0m;
    public SentidoMovimiento Sentido => Abono.HasValue && Abono.Value > 0 ? SentidoMovimiento.Ingreso : SentidoMovimiento.Egreso;

    private ExtractoLinea() { }

    public static ExtractoLinea Crear(
        Guid conciliacionId,
        int numeroLinea,
        DateOnly fecha,
        string descripcion,
        string? referencia,
        decimal? cargo,
        decimal? abono,
        ExtractoFuente fuente = ExtractoFuente.Archivo,
        string? lineaHash = null)
    {
        if (cargo is null && abono is null)
            throw new BusinessRuleException("EXTRACTO_LINEA_SIN_IMPORTE", "La línea de extracto debe tener cargo o abono.");
        if ((cargo ?? 0) <= 0 && (abono ?? 0) <= 0)
            throw new BusinessRuleException("EXTRACTO_LINEA_IMPORTE_INVALIDO", "El cargo o abono debe ser mayor a cero.");
        if (cargo is not null && cargo.Value > 0 && abono is not null && abono.Value > 0)
            throw new BusinessRuleException("EXTRACTO_LINEA_AMBOS_IMPORTES", "La línea no puede tener cargo y abono simultáneamente.");

        return new ExtractoLinea
        {
            Id = Guid.CreateVersion7(),
            ConciliacionId = conciliacionId,
            NumeroLinea = numeroLinea,
            Fecha = fecha,
            Descripcion = descripcion?.Trim() ?? string.Empty,
            Referencia = NormalizarReferencia(referencia),
            Cargo = cargo,
            Abono = abono,
            Fuente = fuente,
            Estado = EstadoExtractoLinea.Pendiente,
            LineaHash = lineaHash,
        };
    }

    public void Emparejar(Guid movimientoId, MatchTipo tipo, decimal monto, Guid usuarioId, DateTimeOffset ahora)
    {
        _matches.Clear();
        _matches.Add(new ExtractoLineaMatch(Id, movimientoId, monto, usuarioId, ahora));
        MatchMovimientoId = movimientoId;
        MatchTipo = tipo;
        Estado = EstadoExtractoLinea.Emparejada;
        CandidatosRevisionIds.Clear();
        PartidaTransito = false;
        PartidaTransitoTipo = null;
        PartidaTransitoResponsable = null;
        PartidaTransitoAutorizadaPor = null;
    }

    public void EmparejarMultiple(IEnumerable<(Guid MovimientoId, decimal Monto)> matches, MatchTipo tipo, Guid usuarioId, DateTimeOffset ahora)
    {
        _matches.Clear();
        Guid? primerMov = null;
        foreach (var m in matches)
        {
            primerMov ??= m.MovimientoId;
            _matches.Add(new ExtractoLineaMatch(Id, m.MovimientoId, m.Monto, usuarioId, ahora));
        }
        MatchMovimientoId = primerMov;
        MatchTipo = tipo;
        Estado = EstadoExtractoLinea.Emparejada;
        CandidatosRevisionIds.Clear();
        PartidaTransito = false;
        PartidaTransitoTipo = null;
        PartidaTransitoResponsable = null;
        PartidaTransitoAutorizadaPor = null;
    }

    public void MarcarEnRevision(IEnumerable<Guid> candidatosIds)
    {
        Estado = EstadoExtractoLinea.EnRevision;
        CandidatosRevisionIds = candidatosIds.Distinct().ToList();
    }

    public void Desemparejar()
    {
        _matches.Clear();
        MatchMovimientoId = null;
        MatchTipo = null;
        Estado = EstadoExtractoLinea.Pendiente;
        CandidatosRevisionIds.Clear();
        PartidaTransito = false;
        PartidaTransitoTipo = null;
        PartidaTransitoResponsable = null;
        PartidaTransitoAutorizadaPor = null;
    }

    public void MarcarPartidaTransito(PartidaTransitoTipo tipo, string responsable, Guid autorizadaPor)
    {
        if (string.IsNullOrWhiteSpace(responsable))
            throw new BusinessRuleException("TRANSITO_RESPONSABLE_VACIO", "El responsable de la partida en tránsito es obligatorio.");
        if (autorizadaPor == Guid.Empty)
            throw new BusinessRuleException("TRANSITO_AUTORIZADOR_VACIO", "El autorizador (Gerente de Tesorería) es obligatorio.");

        _matches.Clear();
        MatchMovimientoId = null;
        MatchTipo = null;
        PartidaTransito = true;
        PartidaTransitoTipo = tipo;
        PartidaTransitoResponsable = responsable.Trim();
        PartidaTransitoAutorizadaPor = autorizadaPor;
        Estado = EstadoExtractoLinea.EnTransito;
        CandidatosRevisionIds.Clear();
    }

    private static string? NormalizarReferencia(string? refStr)
    {
        var r = refStr?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(r) ? null : r;
    }
}
