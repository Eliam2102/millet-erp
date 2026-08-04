namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de los catálogos SAT (<c>c_FormaPago</c>, <c>c_UsoCFDI</c>,
/// <c>c_RegimenFiscal</c>, <c>c_Moneda</c>, …). A diferencia de los demás
/// puertos de F0, este tiene un adapter <b>real</b> sobre los catálogos ya
/// seedeados en el esquema <c>compartido</c> por <c>Millet.Catalogos</c>
/// (cuidados-infra §8: "adapter real, no recrear"). La validación local previa
/// (§12.2 levantamiento) lo usa antes de llamar al PAC, para no consumir
/// timbres en errores corregibles.
/// </summary>
public interface ICatalogosSatReadPort
{
    /// <summary>¿Existe una forma de pago SAT activa con esta clave (01, 02, 03, …)?</summary>
    Task<bool> ExisteFormaPagoAsync(string claveSat, CancellationToken cancellationToken);

    /// <summary>¿Existe un uso CFDI SAT activo con esta clave (G01, G03, P01, …)?</summary>
    Task<bool> ExisteUsoCfdiAsync(string claveSat, CancellationToken cancellationToken);

    /// <summary>¿Existe un régimen fiscal SAT activo con este código (601, 612, 626, …)?</summary>
    Task<bool> ExisteRegimenFiscalAsync(string codigo, CancellationToken cancellationToken);

    /// <summary>¿Existe una moneda SAT activa con este código ISO 4217 (MXN, USD, …)?</summary>
    Task<bool> ExisteMonedaAsync(string codigo, CancellationToken cancellationToken);
}
