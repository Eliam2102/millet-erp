namespace Millet.Tesoreria.Domain.Conciliaciones.Emparejamiento;

/// <summary>
/// Opciones de configuración para el motor de emparejamiento automático (P9.2 / T9.2).
/// Paso 3: tolerancia de ±$1 en ventana de 3 días, configurable.
/// </summary>
public sealed record EmparejamientoOptions(
    decimal ToleranciaImporte = 1.00m,
    int VentanaDias = 3,
    bool HabilitarPaso1ReferenciaExacta = true,
    bool HabilitarPaso2ImporteFecha = true,
    bool HabilitarPaso3Tolerancia = true);

/// <summary>
/// Resumen de resultados de la ejecución del emparejamiento automático.
/// </summary>
public sealed record ResultadoEmparejamiento(
    int TotalLineasProcesadas,
    int EmparejadasPaso1,
    int EmparejadasPaso2,
    int EmparejadasPaso3,
    int EnRevision,
    int Pendientes)
{
    public int TotalEmparejadas => EmparejadasPaso1 + EmparejadasPaso2 + EmparejadasPaso3;
}
