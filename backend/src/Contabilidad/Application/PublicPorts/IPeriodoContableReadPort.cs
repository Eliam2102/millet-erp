namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Puerto de lectura PÚBLICO de periodos contables (F1-CON-03, C1.1). Contabilidad es el dueño del periodo; los consumidores
/// solo leen. La empresa sale de <c>ICurrentEmpresaContext</c>. Un ejercicio que no se ha creado NO se supone abierto:
/// responde <see cref="MotivoRechazoPeriodo.NoExiste"/>.
/// </summary>
// PLATFORM-TODO(<ContabilidadPeriodoConsumidores>): C1.2 reemplaza los NoOp de Facturación y Tesorería (IPeriodoContablePort)
// y de Almacén (IPeriodoContableReadPort) por adaptadores delgados que delegan en EstaAbiertoAsync.
public interface IPeriodoContableReadPort
{
    /// <summary>¿Está abierto el periodo ordinario (año, mes 1–12)? Firma que esperan Facturación, Tesorería y Almacén.</summary>
    Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct);

    /// <summary>
    /// Para el motor de pólizas (C1.3/C1.4). Periodo 13 (R19): solo pólizas manuales autorizadas, y solo después de cerrar el 12.
    /// </summary>
    Task<PeriodoValidacion> ValidarRegistroAsync(int ejercicio, int periodo, bool esManualAutorizada, CancellationToken ct);
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum MotivoRechazoPeriodo { NoExiste, Cerrado, Periodo13SoloAjusteAuditoria, Periodo13AntesDelCierreDeDiciembre }

public sealed record PeriodoValidacion(bool Valido, MotivoRechazoPeriodo? Motivo);
