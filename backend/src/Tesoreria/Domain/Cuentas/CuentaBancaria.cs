using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Cuentas;

/// <summary>
/// Cuenta bancaria propia de Millet (§4.1 del 01-diseño, TES-7 revisada).
/// Master data del módulo con CRUD propio en Tesorería (permiso
/// <c>tesoreria.cuentas.administrar</c>); el script one-shot en
/// <c>backend/scripts/</c> queda solo para bootstrap de ambientes (datos
/// bancarios reales nunca en el repo — cuidados-infra §3.3).
/// <see cref="NumeroCuenta"/> es inmutable post-creación: es la identidad
/// natural de la cuenta (único por empresa) y ancla del ON CONFLICT del
/// seed; una captura errónea se corrige desactivando y recreando.
///
/// <para>
/// <c>NumeroCuenta</c> y <c>Clabe</c> se persisten como texto plano pero
/// se validan al construir vía los VOs <see cref="Cuentas.NumeroCuenta"/>
/// y <see cref="Cuentas.Clabe"/> (ADR-0018); el masking PII (ADR-0006)
/// se aplica en las queries salvo permiso
/// <c>tesoreria.movimientos.ver-cuenta-completa</c>.
/// </para>
/// </summary>
public sealed class CuentaBancaria : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public string Banco { get; private set; } = default!;
    public string NumeroCuenta { get; private set; } = default!;
    public string? Clabe { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    /// <summary>Referencia de cuenta contable para la Contabilidad futura [TES-5].</summary>
    public string? CuentaContableRef { get; private set; }

    /// <summary>Perfil de parser de extracto asociado (§3.5; se usa desde PR-9).</summary>
    public string? PerfilExtracto { get; private set; }

    public bool Activa { get; private set; } = true;

    private CuentaBancaria() { }

    public CuentaBancaria(
        Guid empresaId,
        string banco,
        string numeroCuenta,
        string? clabe,
        string moneda,
        string? cuentaContableRef = null,
        string? perfilExtracto = null) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        Banco = ValidarBanco(banco);
        NumeroCuenta = Cuentas.NumeroCuenta.Crear(numeroCuenta).Valor;
        Clabe = clabe is null ? null : Cuentas.Clabe.Crear(clabe).Valor;
        Moneda = ValidarMoneda(moneda);
        CuentaContableRef = cuentaContableRef;
        PerfilExtracto = perfilExtracto;
        Activa = true;
    }

    /// <summary>
    /// Actualiza los datos editables de la cuenta. El número de cuenta NO
    /// se toca (inmutable); la moneda solo puede cambiar mientras la cuenta
    /// no tenga movimientos — esa precondición la valida el handler, que es
    /// quien puede consultar el libro (<c>CTA_MONEDA_CON_MOVIMIENTOS</c>).
    /// </summary>
    public void ActualizarDatos(
        string banco, string moneda, string? cuentaContableRef, string? perfilExtracto)
    {
        Banco = ValidarBanco(banco);
        Moneda = ValidarMoneda(moneda);
        CuentaContableRef = string.IsNullOrWhiteSpace(cuentaContableRef) ? null : cuentaContableRef.Trim();
        PerfilExtracto = string.IsNullOrWhiteSpace(perfilExtracto) ? null : perfilExtracto.Trim();
    }

    /// <summary>Reemplaza la CLABE (valida el VO) o la limpia con null.</summary>
    public void CambiarClabe(string? clabe) =>
        Clabe = clabe is null ? null : Cuentas.Clabe.Crear(clabe).Valor;

    public void Activar() => Activa = true;

    public void Desactivar() => Activa = false;

    private static string ValidarBanco(string banco)
    {
        if (string.IsNullOrWhiteSpace(banco))
            throw new BusinessRuleException("CTA_BANCO_VACIO", "El banco es obligatorio.");
        return banco.Trim();
    }

    private static string ValidarMoneda(string moneda)
    {
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Trim().Length != 3)
            throw new BusinessRuleException("CTA_MONEDA_INVALIDA", "La moneda debe ser código ISO de 3 letras.");
        return moneda.Trim().ToUpperInvariant();
    }
}
