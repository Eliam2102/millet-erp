using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>Tipos de movimiento de caja (12-cajas.md §7).</summary>
public enum TipoCajaMovimiento : short
{
    /// <summary>Cobro de cliente (lo genera <c>CobroMostrador</c>, CAJAS-PR4).</summary>
    CobroCliente = 1,

    /// <summary>Depósito manual de efectivo a la caja.</summary>
    Deposito = 2,

    /// <summary>Retiro parcial durante el día (v1: basta <c>caja.operar</c>, queda auditado — P4).</summary>
    Retiro = 3,

    /// <summary>Ajuste drenado de <c>caja_ajuste_pendiente</c> en la apertura (`[Decisión 12-C]`).</summary>
    AjusteCorreccion = 4,

    /// <summary>Reversa de un cobro cancelado con sesión abierta del ejecutor (CAJAS-PR4).</summary>
    ReversaCobro = 5,

    /// <summary>Fondo declarado al abrir la sesión.</summary>
    FondoApertura = 6,
}

/// <summary>
/// Movimiento de una sesión de caja (12-cajas.md §7). Un movimiento por cada
/// forma de pago de un cobro (`[Decisión 12-5]`); retiros/depósitos manuales;
/// fondo de apertura y ajustes. <c>Importe</c> lleva signo: entradas
/// positivas, salidas (retiro, reversa) negativas.
/// </summary>
public sealed class CajaMovimiento : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CajaSesionId { get; private set; }
    public TipoCajaMovimiento Tipo { get; private set; }

    /// <summary>Clave SAT c_FormaPago (01 efectivo, 03 transferencia, 04 TC…).</summary>
    public string FormaPago { get; private set; } = string.Empty;

    /// <summary>Importe con signo (entradas +, salidas −).</summary>
    public decimal Importe { get; private set; }

    /// <summary>Solo MXN en v1 (P6: USD exclusivamente por transferencia administrativa).</summary>
    public string Moneda { get; private set; } = "MXN";

    /// <summary>Cobro que originó el movimiento (CAJAS-PR4); null en manuales/fondo.</summary>
    public Guid? CobroMostradorId { get; private set; }

    public string? Referencia { get; private set; }
    public string Descripcion { get; private set; } = string.Empty;

    /// <summary>Quien capturó/originó el movimiento.</summary>
    public Guid UsuarioId { get; private set; }

    private CajaMovimiento() { }

    private CajaMovimiento(
        Guid id, Guid empresaId, Guid cajaSesionId, TipoCajaMovimiento tipo, string formaPago,
        decimal importe, string moneda, Guid? cobroMostradorId, string? referencia,
        string descripcion, Guid usuarioId) : base(id)
    {
        EmpresaId = empresaId;
        CajaSesionId = cajaSesionId;
        Tipo = tipo;
        FormaPago = formaPago;
        Importe = importe;
        Moneda = moneda;
        CobroMostradorId = cobroMostradorId;
        Referencia = referencia;
        Descripcion = descripcion;
        UsuarioId = usuarioId;
    }

    public static CajaMovimiento Crear(
        Guid empresaId,
        Guid cajaSesionId,
        TipoCajaMovimiento tipo,
        string formaPago,
        decimal importe,
        Guid usuarioId,
        string descripcion,
        string? referencia = null,
        Guid? cobroMostradorId = null,
        string moneda = "MXN")
    {
        if (cajaSesionId == Guid.Empty)
            throw new BusinessRuleException("MOVIMIENTO_SESION_INVALIDA", "La sesión es obligatoria.");
        if (string.IsNullOrWhiteSpace(formaPago) || formaPago.Trim().Length > 2)
            throw new BusinessRuleException("MOVIMIENTO_FORMA_PAGO_INVALIDA", "La forma de pago debe ser clave SAT c_FormaPago de 2 caracteres.");
        if (importe == 0)
            throw new BusinessRuleException("MOVIMIENTO_IMPORTE_INVALIDO", "El importe no puede ser cero.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("MOVIMIENTO_USUARIO_INVALIDO", "El usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new BusinessRuleException("MOVIMIENTO_DESCRIPCION_INVALIDA", "La descripción es obligatoria.");
        if (!string.Equals(moneda, "MXN", StringComparison.Ordinal))
            throw new BusinessRuleException("MOVIMIENTO_MONEDA_INVALIDA", "La caja opera solo MXN en v1 (P6).");

        var signoValido = tipo switch
        {
            TipoCajaMovimiento.Retiro => importe < 0,
            TipoCajaMovimiento.Deposito or TipoCajaMovimiento.FondoApertura or TipoCajaMovimiento.CobroCliente => importe > 0,
            _ => true, // AjusteCorreccion / ReversaCobro llevan el signo del origen
        };
        if (!signoValido)
            throw new BusinessRuleException(
                "MOVIMIENTO_SIGNO_INVALIDO",
                $"El importe de un movimiento {tipo} debe ser {(tipo == TipoCajaMovimiento.Retiro ? "negativo" : "positivo")}.");

        return new CajaMovimiento(
            Guid.CreateVersion7(), empresaId, cajaSesionId, tipo, formaPago.Trim(), importe,
            moneda, cobroMostradorId, Normalizar(referencia), descripcion.Trim(), usuarioId);
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
