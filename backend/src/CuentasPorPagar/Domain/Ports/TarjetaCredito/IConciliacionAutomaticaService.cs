using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito;

/// <summary>
/// Servicio que aplica el algoritmo de match automático del §7 anexo
/// TC (F7-PR5). Recibe el <see cref="EstadoCuentaTc"/> con sus líneas
/// del banco + los <see cref="MovimientoTarjetaCredito"/> candidatos
/// dentro de la ventana de fechas, calcula el score 0-100 por línea,
/// y aplica la decisión (auto-match ≥90, sugerencia 60-89, sin match
/// &lt;60).
///
/// <para>
/// Vive en Application/Infrastructure pero el puerto está en Domain
/// para que los tests del dominio puedan stubbearlo.
/// </para>
/// </summary>
public interface IConciliacionAutomaticaService
{
    Task<ConciliacionResultado> ConciliarAsync(
        EstadoCuentaTc estadoCuenta,
        IReadOnlyList<MovimientoTarjetaCredito> candidatos,
        CancellationToken cancellationToken);
}

public sealed record ConciliacionResultado(
    int LineasTotal,
    int LineasMatchedAuto,
    int LineasSugerencia,
    int LineasSinMatch,
    int LineasOmitidasNoBuscanMatch,
    decimal TotalConciliadoMxn);
