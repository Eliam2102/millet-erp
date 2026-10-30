namespace Millet.Tesoreria.Domain.Conciliaciones.Actas;

public sealed record DatosPartidaTransito(
    DateOnly Fecha,
    string Descripcion,
    string? Referencia,
    decimal Monto,
    string Sentido,
    string Tipo,
    string Responsable);

public sealed record DatosActaConciliacion(
    Guid ConciliacionId,
    string EmpresaNombre,
    string EmpresaRfc,
    string Banco,
    string NumeroCuentaEnmascarado,
    string Moneda,
    int PeriodoAnio,
    int PeriodoMes,
    decimal SaldoExtracto,
    decimal SaldoLibros,
    decimal TotalCargosBanco,
    decimal TotalAbonosBanco,
    int TotalMovimientosExtracto,
    int TotalEmparejados,
    IReadOnlyList<DatosPartidaTransito> PartidasTransito,
    string FirmanteNombre,
    string FirmantePuesto,
    DateTimeOffset FechaFirma)
{
    public decimal Diferencia => SaldoExtracto - SaldoLibros;
}
