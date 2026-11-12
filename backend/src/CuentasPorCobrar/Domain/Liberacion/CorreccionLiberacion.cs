using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>M-238: dato que se corrigió para volver a evaluar un pedido retenido.</summary>
public enum TipoCorreccionLiberacion : short
{
    /// <summary>El pedido estaba ligado a un cliente equivocado; se reevalúa con el cliente correcto.</summary>
    Cliente = 1,

    /// <summary>Se corrigió la condición del cliente (línea de crédito, plazo o bloqueo) antes de reevaluar.</summary>
    Condicion = 2,

    /// <summary>Un cobro o movimiento estaba mal aplicado y se corrigió en cartera antes de reevaluar.</summary>
    SaldoAplicado = 3,
}

/// <summary>
/// M-238: rastro de «corregir y reevaluar» un pedido retenido. La decisión es inmutable, así que la reevaluación es
/// una decisión nueva (<see cref="DecisionReevaluadaId"/>) con la misma cascada serie → crédito → override; este
/// registro liga las dos evaluaciones (antes y después) con quién corrigió, qué y por qué. Una retenida se reevalúa
/// una sola vez; si sigue retenida, la nueva decisión se puede corregir otra vez (cadena).
/// </summary>
public sealed class CorreccionLiberacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string PedidoRef { get; private set; } = default!;

    /// <summary>Decisión <see cref="ResultadoLiberacion.Retenido"/> que se corrigió (la evaluación de antes).</summary>
    public Guid DecisionRetenidaId { get; private set; }

    /// <summary>Decisión nueva que salió de reevaluar (la evaluación de después).</summary>
    public Guid DecisionReevaluadaId { get; private set; }

    public TipoCorreccionLiberacion Tipo { get; private set; }

    /// <summary>Qué se corrigió y por qué (obligatorio).</summary>
    public string Detalle { get; private set; } = default!;

    public Guid CorregidaPor { get; private set; }
    public DateTimeOffset CorregidaEn { get; private set; }

    private CorreccionLiberacion() { }

    public static CorreccionLiberacion Crear(
        Guid empresaId, string pedidoRef, Guid decisionRetenidaId, Guid decisionReevaluadaId,
        TipoCorreccionLiberacion tipo, string detalle, Guid corregidaPor, DateTimeOffset corregidaEn)
    {
        if (string.IsNullOrWhiteSpace(pedidoRef))
            throw new BusinessRuleException("CL_PEDIDO_VACIO", "La referencia del pedido es obligatoria.");
        if (!Enum.IsDefined(tipo))
            throw new BusinessRuleException("CL_TIPO_INVALIDO", "El tipo de corrección no es válido.");
        var d = detalle?.Trim() ?? string.Empty;
        if (d.Length is < 5 or > 254)
            throw new BusinessRuleException("CL_DETALLE_INVALIDO",
                "Indica qué se corrigió y por qué (entre 5 y 254 caracteres).");
        if (corregidaPor == Guid.Empty)
            throw new BusinessRuleException("CL_USUARIO_VACIO", "El usuario que corrige es obligatorio.");
        if (decisionRetenidaId == decisionReevaluadaId)
            throw new BusinessRuleException("CL_DECISIONES_IGUALES", "La reevaluación debe ser una decisión distinta.");

        return new CorreccionLiberacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            PedidoRef = pedidoRef.Trim(),
            DecisionRetenidaId = decisionRetenidaId,
            DecisionReevaluadaId = decisionReevaluadaId,
            Tipo = tipo,
            Detalle = d,
            CorregidaPor = corregidaPor,
            CorregidaEn = corregidaEn,
        };
    }
}
