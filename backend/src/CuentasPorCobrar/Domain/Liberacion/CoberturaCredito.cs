namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>Tramo de cobertura del crédito frente al monto del pedido (C7.3).</summary>
public enum TramoCobertura
{
    /// <summary>Disponible ≥ monto: libera por crédito.</summary>
    Plena = 1,

    /// <summary>50 % ≤ cobertura &lt; 100 %: pendiente de autorización de CxC.</summary>
    Parcial = 2,

    /// <summary>Cobertura &lt; 50 %: retenido; no se ofrece autorización parcial.</summary>
    Insuficiente = 3,
}

/// <summary>
/// C7.3 (ClickUp 86e3jvv6n, cuestionario 04 R3) — cálculo puro de la
/// cobertura: crédito DISPONIBLE ÷ monto del pedido (no anticipos).
///
/// <para>Supuestos C7.3 (por confirmar con Eliam):</para>
/// <list type="bullet">
///   <item>(a) 50 % exacto = pendiente (umbral inclusivo).</item>
///   <item>(b) el porcentaje se TRUNCA a 2 decimales para guardar y a
///   entero para mostrar (66.666… → 66.66 → «66 %»); nunca se redondea
///   hacia arriba, así 99.999 % no aparenta cobertura plena.</item>
///   <item>El valor guardado se acota a [0, 100]: un disponible negativo
///   es 0 % y un disponible mayor al pedido es 100 % (cabe en
///   <c>numeric(5,2)</c>).</item>
/// </list>
/// El tramo se decide con los montos crudos (disponible ≥ monto = plena),
/// no con el porcentaje truncado.
/// </summary>
public static class CoberturaCredito
{
    /// <summary>Umbral inclusivo para ofrecer la autorización de CxC.</summary>
    public const decimal UmbralAutorizacionPorcentaje = 50m;

    public static decimal Porcentaje(decimal disponible, decimal montoPedido)
    {
        if (montoPedido <= 0)
            throw new ArgumentOutOfRangeException(nameof(montoPedido), "El monto del pedido debe ser > 0.");
        if (disponible <= 0) return 0m;
        if (disponible >= montoPedido) return 100m;

        var bruto = disponible / montoPedido * 100m;
        return Math.Truncate(bruto * 100m) / 100m;
    }

    public static TramoCobertura Evaluar(decimal disponible, decimal montoPedido)
    {
        if (disponible >= montoPedido) return TramoCobertura.Plena;
        return Porcentaje(disponible, montoPedido) >= UmbralAutorizacionPorcentaje
            ? TramoCobertura.Parcial
            : TramoCobertura.Insuficiente;
    }

    /// <summary>Supuesto (b): entero truncado para mostrar («66 %»).</summary>
    public static int PorcentajeEntero(decimal porcentaje) => (int)Math.Truncate(porcentaje);
}
