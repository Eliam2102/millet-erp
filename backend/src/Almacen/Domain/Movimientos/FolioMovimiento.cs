using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// VO del folio interno de un movimiento (A2 del 01-diseno):
/// formato <c>M-{prefijo_tipo}{año}-{secuencial:6}</c>. Ejemplos:
/// <c>M-ENT2026-000001</c>, <c>M-SAL2026-000042</c>,
/// <c>M-AJP2026-000007</c>.
///
/// <para>
/// La secuencia es atómica por <c>(tipo_prefijo, año)</c>, persistida
/// en la tabla <c>folio_secuencias_movimiento</c>. El handler que
/// pasa el movimiento a <see cref="EstadoMovimiento.Registrado"/>
/// reserva el siguiente número antes del INSERT del movimiento, en la
/// misma transacción que el trigger de saldo.
/// </para>
/// </summary>
public readonly record struct FolioMovimiento
{
    public string Valor { get; }

    public FolioMovimiento(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new BusinessRuleException(
                "FOLIO_MOV_INVALIDO", "El folio del movimiento es requerido.");
        if (valor.Length > 30)
            throw new BusinessRuleException(
                "FOLIO_MOV_INVALIDO",
                "El folio del movimiento no puede exceder 30 caracteres.");

        Valor = valor;
    }

    public static FolioMovimiento Construir(TipoMovimiento tipo, int año, int secuencial)
    {
        if (año is < 2000 or > 2999)
            throw new BusinessRuleException(
                "FOLIO_MOV_ANIO_INVALIDO", "Año inválido para folio de movimiento.");
        if (secuencial <= 0)
            throw new BusinessRuleException(
                "FOLIO_MOV_SEC_INVALIDA", "Secuencial debe ser positivo.");

        var prefijo = tipo.PrefijoFolio();
        return new FolioMovimiento($"M-{prefijo}{año}-{secuencial:D6}");
    }

    public override string ToString() => Valor;
}
