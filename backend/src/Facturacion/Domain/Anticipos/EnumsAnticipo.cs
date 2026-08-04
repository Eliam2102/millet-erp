namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// Tipo de anticipo de cliente (§4.3, §6.1 levantamiento). Determina la moneda
/// del saldo amortizable y el asiento contable (MXP vs USD). No confundir con el
/// <c>TipoComprobante</c> del CFDI — la factura de anticipo siempre es Ingreso.
/// </summary>
public enum TipoAnticipo : short
{
    /// <summary>Anticipo de clientes en pesos (CLIENTES_MXP).</summary>
    ClientesMxp = 1,

    /// <summary>Anticipo de clientes en dólares (CLIENTES_USD).</summary>
    ClientesUsd = 2,
}

/// <summary>
/// Estado del saldo amortizable de un <see cref="Anticipo"/> (§6.6 levantamiento).
/// Vive en el agregado <c>Anticipo</c>, separado del CFDI inmutable
/// (<see cref="FacturaAnticipo"/>), porque el saldo evoluciona (vinculaciones,
/// NCs de amortización) sin tocar el comprobante timbrado.
/// </summary>
public enum EstadoAnticipo : short
{
    /// <summary>Saldo amortizable &gt; 0. Estado inicial al cobrar.</summary>
    Abierto = 1,

    /// <summary>Saldo amortizable = 0 — la última NC llevó el saldo a cero (M3, F4-PR2).</summary>
    Amortizado = 2,

    /// <summary>El CFDI del anticipo fue cancelado; bloquea aplicación (F5).</summary>
    Cancelado = 3,
}
