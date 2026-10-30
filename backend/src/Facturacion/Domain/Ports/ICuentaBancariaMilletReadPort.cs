namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Lectura de una cuenta bancaria propia de Millet (dueño: Tesorería) para la
/// cuenta beneficiaria del complemento de pago (86e3tfdve). Facturación no lee
/// tablas de Tesorería: el adaptador vive en la composición (<c>Millet.Api</c>)
/// y consulta el puerto público <c>ConsultarCuentaBancariaQuery</c>. El filtro
/// global de empresa aplica en Tesorería.
/// </summary>
public interface ICuentaBancariaMilletReadPort
{
    Task<CuentaBancariaMillet?> ObtenerAsync(Guid cuentaBancariaId, CancellationToken cancellationToken);
}

/// <summary>Datos completos (sin enmascarar) de la cuenta; uso interno para el CFDI, nunca para logs.</summary>
public sealed record CuentaBancariaMillet(Guid Id, string Banco, string NumeroCuenta, string? Clabe, string Moneda, bool Activa);
