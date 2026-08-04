using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>
/// Ajuste pendiente de una caja (`[Decisión 12-C]`, 12-cajas.md §7): una
/// corrección sobre sesión cerrada (p. ej. cancelación de un cobro) procesada
/// por un perfil sin sesión abierta. La <b>próxima apertura</b> de la caja lo
/// drena automáticamente como movimiento <c>AjusteCorreccion</c> de la sesión
/// nueva, con referencia al cobro original. La sesión origen cerrada jamás se
/// modifica. Las filas nacen en CAJAS-PR4 (cancelación de cobros); el drenado
/// en apertura entra desde CAJAS-PR3.
/// </summary>
public sealed class CajaAjustePendiente : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CajaId { get; private set; }

    /// <summary>Cobro original que motiva el ajuste (referencia opaca hasta CAJAS-PR4).</summary>
    public Guid CobroMostradorId { get; private set; }

    /// <summary>Importe con signo (una reversa de cobro es negativa).</summary>
    public decimal Importe { get; private set; }

    public string FormaPago { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;
    public Guid CreadoPor { get; private set; }

    /// <summary>Sesión que lo drenó; null mientras esté pendiente.</summary>
    public Guid? AplicadoEnSesionId { get; private set; }

    private CajaAjustePendiente() { }

    private CajaAjustePendiente(
        Guid id, Guid empresaId, Guid cajaId, Guid cobroMostradorId, decimal importe,
        string formaPago, string motivo, Guid creadoPor) : base(id)
    {
        EmpresaId = empresaId;
        CajaId = cajaId;
        CobroMostradorId = cobroMostradorId;
        Importe = importe;
        FormaPago = formaPago;
        Motivo = motivo;
        CreadoPor = creadoPor;
    }

    public static CajaAjustePendiente Crear(
        Guid empresaId,
        Guid cajaId,
        Guid cobroMostradorId,
        decimal importe,
        string formaPago,
        string motivo,
        Guid creadoPor)
    {
        if (cajaId == Guid.Empty)
            throw new BusinessRuleException("AJUSTE_CAJA_INVALIDA", "La caja es obligatoria.");
        if (cobroMostradorId == Guid.Empty)
            throw new BusinessRuleException("AJUSTE_COBRO_INVALIDO", "El cobro original es obligatorio.");
        if (importe == 0)
            throw new BusinessRuleException("AJUSTE_IMPORTE_INVALIDO", "El importe no puede ser cero.");
        if (string.IsNullOrWhiteSpace(formaPago) || formaPago.Trim().Length > 2)
            throw new BusinessRuleException("AJUSTE_FORMA_PAGO_INVALIDA", "La forma de pago debe ser clave SAT c_FormaPago de 2 caracteres.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("AJUSTE_MOTIVO_INVALIDO", "El motivo es obligatorio.");
        if (creadoPor == Guid.Empty)
            throw new BusinessRuleException("AJUSTE_USUARIO_INVALIDO", "El usuario creador es obligatorio.");

        return new CajaAjustePendiente(
            Guid.CreateVersion7(), empresaId, cajaId, cobroMostradorId, importe,
            formaPago.Trim(), motivo.Trim(), creadoPor);
    }

    /// <summary>Marca el ajuste como drenado por la sesión indicada (idempotencia del drenado).</summary>
    public void MarcarAplicado(Guid sesionId)
    {
        if (AplicadoEnSesionId is not null)
            throw new BusinessRuleException("AJUSTE_YA_APLICADO", "El ajuste ya fue drenado por otra sesión.");
        AplicadoEnSesionId = sesionId;
    }
}
