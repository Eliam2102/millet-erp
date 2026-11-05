using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Application.Conciliaciones;

public sealed record ConciliacionResumenResponse(
    Guid Id,
    Guid CuentaBancariaId,
    string Banco,
    string NumeroCuenta,
    string Moneda,
    int PeriodoAnio,
    int PeriodoMes,
    decimal SaldoExtracto,
    decimal SaldoLibros,
    decimal Diferencia,
    EstadoConciliacion Estado,
    int TotalLineas,
    int LineasEmparejadas,
    int LineasPendientes,
    int LineasEnRevision,
    int LineasEnTransito,
    DateTimeOffset? CerradaEn,
    string? FirmanteNombre,
    // 86e3ke6yn: solo al importar un extracto; null en el resto de las consultas.
    ResumenDepositosAutomaticos? Depositos = null,
    // Solo al importar un extracto: líneas agregadas y líneas que ya estaban (no se duplican).
    int? LineasNuevas = null,
    int? LineasDuplicadas = null);

public sealed record ExtractoLineaMatchResponse(
    Guid Id,
    Guid MovimientoBancarioId,
    decimal Monto,
    DateTimeOffset FechaMatch);

public sealed record ExtractoLineaResponse(
    Guid Id,
    int NumeroLinea,
    DateOnly Fecha,
    string Descripcion,
    string? Referencia,
    decimal? Cargo,
    decimal? Abono,
    decimal Monto,
    SentidoMovimiento Sentido,
    EstadoExtractoLinea Estado,
    MatchTipo? MatchTipo,
    Guid? MatchMovimientoId,
    IReadOnlyList<ExtractoLineaMatchResponse> Matches,
    IReadOnlyList<Guid> CandidatosRevisionIds,
    bool PartidaTransito,
    PartidaTransitoTipo? PartidaTransitoTipo,
    string? PartidaTransitoResponsable);

public sealed record BitacoraConciliacionResponse(
    Guid Id,
    Guid? ExtractoLineaId,
    Guid? MovimientoBancarioId,
    string Accion,
    string Motivo,
    Guid UsuarioId,
    DateTimeOffset Fecha,
    string? UsuarioNombre = null);

public sealed record MovimientoBancarioConciliacionDto(
    Guid Id,
    DateOnly FechaValor,
    SentidoMovimiento Sentido,
    decimal Monto,
    string Moneda,
    string? ReferenciaBancaria,
    Guid? ConceptoId,
    string? ConceptoNombre,
    EstadoConciliacionMovimiento EstadoConciliacion);

public sealed record ConciliacionDetalleResponse(
    Guid Id,
    Guid CuentaBancariaId,
    string Banco,
    string NumeroCuenta,
    string Moneda,
    int PeriodoAnio,
    int PeriodoMes,
    decimal SaldoExtracto,
    decimal SaldoLibros,
    decimal Diferencia,
    EstadoConciliacion Estado,
    Guid? CerradaPor,
    DateTimeOffset? CerradaEn,
    string? MotivoReapertura,
    string? FirmanteNombre,
    string? FirmantePuesto,
    DateTimeOffset? ActaGeneradaEn,
    IReadOnlyList<ExtractoLineaResponse> Lineas,
    IReadOnlyList<BitacoraConciliacionResponse> Bitacora,
    IReadOnlyList<MovimientoBancarioConciliacionDto> MovimientosPeriodo);
