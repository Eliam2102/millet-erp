using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain;

/// <summary>
/// Configuración del módulo Almacén por empresa. Una fila por empresa,
/// con valores tipados explícitos (no key-value genérico). Molde de
/// <c>ComprasSettings</c> (ADR-0032).
///
/// <para>
/// <b><see cref="ReabastoAutomaticoActivo"/></b>: interruptor operativo del
/// motor de reorden (<c>ReordenWorker</c>). Con <c>true</c>, el worker genera
/// borradores de requisición en cada ciclo; con <c>false</c> (default), el
/// worker queda ocioso — las configuraciones de reabasto se conservan y los
/// borradores ya generados quedan vivos como RQs normales.
/// </para>
///
/// <para>
/// Precedencia: <c>ReordenWorker:Disabled</c> (config de infraestructura) es
/// el kill-switch duro — si está apagado por config, este flag ni se consulta.
/// El worker lee el flag de la empresa del <b>usuario de servicio del motor</b>
/// (<c>IUsuarioServicioReadPort</c>, mono-empresa) en cada ciclo.
/// </para>
/// </summary>
public sealed class AlmacenSettings : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public bool ReabastoAutomaticoActivo { get; private set; }

    /// <summary>Constructor de EF Core.</summary>
    private AlmacenSettings() { }

    public AlmacenSettings(Guid id, Guid empresaId, bool reabastoAutomaticoActivo)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("ALMACEN_SETTINGS_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("ALMACEN_SETTINGS_EMPRESA_INVALIDA", "EmpresaId es obligatorio.");

        EmpresaId = empresaId;
        ReabastoAutomaticoActivo = reabastoAutomaticoActivo;
    }

    /// <summary>
    /// Actualiza el interruptor del reabasto automático. Idempotente —
    /// re-aplicar el mismo valor no produce side effects (el interceptor
    /// de audit igual lo registra; la concurrencia EF gestiona Version).
    /// </summary>
    public void ActualizarReabastoAutomatico(bool valor)
    {
        ReabastoAutomaticoActivo = valor;
    }

    /// <summary>
    /// Factory para construir el row default. El default es
    /// <c>ReabastoAutomaticoActivo=false</c>: el motor arranca apagado,
    /// consistente con el opt-in del <c>ReordenWorker</c> (PR5.D).
    /// </summary>
    public static AlmacenSettings CrearDefault(Guid empresaId)
    {
        return new AlmacenSettings(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            reabastoAutomaticoActivo: false);
    }
}
