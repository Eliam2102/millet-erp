using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Puerto público de Contabilidad (F1-CON-03, D8): estado del periodo contable de una fecha (periodos 1–12) o de un periodo por
/// número (1–13; el 13 de ajustes nunca se resuelve por fecha). Reemplaza la suposición «siempre abierto» de los NoOp.
/// La empresa sale de <c>ICurrentEmpresaContext</c>. Solo lectura. Para rechazar un movimiento usar
/// <c>VerificadorPeriodoContable</c>, que aplica la falla cerrada (D9): inexistente, no abierto o cerrado no admiten movimientos.
/// </summary>
// PLATFORM-TODO(C1.2): Facturación y Tesorería sustituyen sus NoOpPeriodoContable*Port por un adaptador delgado hacia aquí.
public interface IPeriodoContableConsultaPort
{
    Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct);

    Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct);
}

/// <summary><c>Existe</c> = false: el ejercicio de ese año no se ha creado (el estado se reporta <c>NoAbierto</c>).</summary>
public sealed record EstadoPeriodoContable(int Anio, int Numero, EstadoPeriodo Estado, bool Existe)
{
    public bool AdmiteMovimientos => Existe && Estado == EstadoPeriodo.Abierto;
}
