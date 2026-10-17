using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.DatosMaestros.Domain;

public enum EstadoCuentaBancariaProveedor : short
{
    PorValidar = 0,
    Activa = 1,
    Inhabilitada = 2,
    Rechazada = 3,
}

/// <summary>
/// Cuenta bancaria de un proveedor (86e3pdmee · ADM-02). Un proveedor tiene
/// varias (MXN/USD, nacionales y extranjeras); Tesorería solo paga a una
/// <see cref="EstadoCuentaBancariaProveedor.Activa"/>. Decisión del owner
/// (17-oct): la cuenta nueva queda «Por validar» hasta que un administrador,
/// distinto de quien la capturó, la coteja contra el estado de cuenta o carta
/// bancaria vigente. La CLABE no se edita: un cambio es una cuenta nueva.
/// </summary>
public sealed class CuentaBancariaProveedor : BaseEntity, IAuditable
{
    public static readonly string[] Monedas = ["MXN", "USD"];

    public Guid ProveedorId { get; private set; }
    public string Banco { get; private set; } = string.Empty;
    /// <summary>CLABE de 18 dígitos (cuentas nacionales).</summary>
    public string? Clabe { get; private set; }
    /// <summary>Número de cuenta, IBAN o ABA para cuentas en el extranjero.</summary>
    public string? NumeroCuenta { get; private set; }
    public string? Swift { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public string Titular { get; private set; } = string.Empty;
    public bool Predeterminada { get; private set; }
    public EstadoCuentaBancariaProveedor Estado { get; private set; }

    public Guid? CapturadaPorId { get; private set; }
    public string? CapturadaPorNombre { get; private set; }
    public DateTimeOffset CapturadaEn { get; private set; }
    public Guid? ValidadaPorId { get; private set; }
    public string? ValidadaPorNombre { get; private set; }
    public DateTimeOffset? ValidadaEn { get; private set; }
    /// <summary>Adjunto del proveedor (tipo <c>estado_cuenta_bancaria</c>) usado para validar.</summary>
    public Guid? DocumentoAdjuntoId { get; private set; }
    public string? NotaValidacion { get; private set; }
    /// <summary>Motivo del último rechazo, inhabilitación o reactivación.</summary>
    public string? MotivoEstado { get; private set; }
    /// <summary>Quién y cuándo hizo el último rechazo, inhabilitación o reactivación (historial en la UI).</summary>
    public Guid? UltimoCambioPorId { get; private set; }
    public string? UltimoCambioPorNombre { get; private set; }
    public DateTimeOffset? UltimoCambioEn { get; private set; }

    /// <summary>Número que identifica la cuenta para detectar duplicados y para el pago.</summary>
    public string Identificador => Clabe ?? NumeroCuenta ?? string.Empty;

    private CuentaBancariaProveedor() { }

    public static CuentaBancariaProveedor Capturar(
        Guid proveedorId, string banco, string? clabe, string? numeroCuenta, string? swift,
        string moneda, string titular, Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        var c = new CuentaBancariaProveedor
        {
            Id = Guid.CreateVersion7(),
            ProveedorId = proveedorId,
            Banco = Texto(banco, 120, "PROVEEDOR_CUENTA_BANCO_INVALIDO", "El banco es obligatorio (máximo 120 caracteres)."),
            Titular = Texto(titular, 254, "PROVEEDOR_CUENTA_TITULAR_INVALIDO", "El titular es obligatorio (máximo 254 caracteres)."),
            Moneda = Monedas.Contains(moneda?.Trim().ToUpperInvariant())
                ? moneda!.Trim().ToUpperInvariant()
                : throw new BusinessRuleException("PROVEEDOR_CUENTA_MONEDA_INVALIDA", "La moneda debe ser MXN o USD."),
            Estado = EstadoCuentaBancariaProveedor.PorValidar,
            CapturadaPorId = usuarioId,
            CapturadaPorNombre = usuarioNombre,
            CapturadaEn = ahora,
        };
        if (!string.IsNullOrWhiteSpace(clabe))
            c.Clabe = SharedKernel.Domain.Bancos.Clabe.Crear(clabe).Valor;
        else if (!string.IsNullOrWhiteSpace(numeroCuenta))
            c.NumeroCuenta = SharedKernel.Domain.Bancos.NumeroCuenta.Crear(numeroCuenta).Valor;
        else
            throw new BusinessRuleException("PROVEEDOR_CUENTA_SIN_NUMERO", "Captura la CLABE (nacional) o el número de cuenta (extranjera).");
        if (!string.IsNullOrWhiteSpace(swift))
        {
            var s = swift.Trim().ToUpperInvariant();
            if (s.Length is not (8 or 11) || !s.All(char.IsAsciiLetterOrDigit))
                throw new BusinessRuleException("PROVEEDOR_CUENTA_SWIFT_INVALIDO", "El SWIFT/BIC debe tener 8 u 11 caracteres alfanuméricos.");
            c.Swift = s;
        }
        return c;
    }

    /// <summary>Cuenta heredada del dato único del proveedor: ya se usaba para pagar, entra Activa (owner, 17-oct).</summary>
    public static CuentaBancariaProveedor Heredada(Guid proveedorId, string banco, string clabe, string titular, DateTimeOffset ahora)
    {
        var c = Capturar(proveedorId, banco, clabe, null, null, "MXN", titular, null, null, ahora);
        c.Estado = EstadoCuentaBancariaProveedor.Activa;
        c.Predeterminada = true;
        c.MotivoEstado = "Migrada del dato bancario único del proveedor.";
        return c;
    }

    /// <summary>
    /// Un administrador coteja la cuenta contra el estado de cuenta o carta
    /// bancaria vigente. Quien capturó no puede validar (segregación).
    /// </summary>
    public void Validar(Guid? usuarioId, string? usuarioNombre, Guid documentoAdjuntoId, bool cotejoCompleto, string? nota, DateTimeOffset ahora)
    {
        if (Estado != EstadoCuentaBancariaProveedor.PorValidar)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_POR_VALIDAR", "Solo se validan cuentas «Por validar».");
        if (usuarioId is not null && usuarioId == CapturadaPorId)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_MISMA_PERSONA", "Quien capturó la cuenta no puede validarla; pide a otro administrador.");
        if (!cotejoCompleto)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_COTEJO_INCOMPLETO", "Confirma titular, número, banco, moneda y vigencia del documento antes de validar.");
        Estado = EstadoCuentaBancariaProveedor.Activa;
        ValidadaPorId = usuarioId;
        ValidadaPorNombre = usuarioNombre;
        ValidadaEn = ahora;
        DocumentoAdjuntoId = documentoAdjuntoId;
        NotaValidacion = string.IsNullOrWhiteSpace(nota) ? null : Texto(nota, 500, "PROVEEDOR_CUENTA_NOTA_INVALIDA", "La nota admite hasta 500 caracteres.");
        MotivoEstado = null;
    }

    public void Rechazar(string motivo, Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoCuentaBancariaProveedor.PorValidar)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_POR_VALIDAR", "Solo se rechazan cuentas «Por validar».");
        MotivoEstado = Motivo(motivo);
        RegistrarCambio(usuarioId, usuarioNombre, ahora);
        Estado = EstadoCuentaBancariaProveedor.Rechazada;
        Predeterminada = false;
    }

    public void Inhabilitar(string motivo, Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoCuentaBancariaProveedor.Activa or EstadoCuentaBancariaProveedor.PorValidar))
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_INHABILITABLE", "Solo se inhabilitan cuentas activas o por validar.");
        MotivoEstado = Motivo(motivo);
        RegistrarCambio(usuarioId, usuarioNombre, ahora);
        Estado = EstadoCuentaBancariaProveedor.Inhabilitada;
        Predeterminada = false;
    }

    /// <summary>Vuelve a Activa si ya había sido validada (o heredada); si nunca se validó, regresa a Por validar.</summary>
    public void Reactivar(string motivo, Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoCuentaBancariaProveedor.Inhabilitada)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_INHABILITADA", "Solo se reactivan cuentas inhabilitadas.");
        MotivoEstado = Motivo(motivo);
        RegistrarCambio(usuarioId, usuarioNombre, ahora);
        Estado = ValidadaEn is not null || CapturadaPorId is null
            ? EstadoCuentaBancariaProveedor.Activa
            : EstadoCuentaBancariaProveedor.PorValidar;
    }

    public void MarcarPredeterminada(bool valor)
    {
        if (valor && Estado is EstadoCuentaBancariaProveedor.Inhabilitada or EstadoCuentaBancariaProveedor.Rechazada)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_PREDETERMINABLE", "Una cuenta inhabilitada o rechazada no puede ser la predeterminada.");
        Predeterminada = valor;
    }

    private void RegistrarCambio(Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora)
    {
        UltimoCambioPorId = usuarioId;
        UltimoCambioPorNombre = usuarioNombre;
        UltimoCambioEn = ahora;
    }

    private static string Motivo(string motivo) =>
        string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length is < 5 or > 500
            ? throw new BusinessRuleException("PROVEEDOR_CUENTA_MOTIVO_INVALIDO", "El motivo debe tener entre 5 y 500 caracteres.")
            : motivo.Trim();

    private static string Texto(string? valor, int max, string codigo, string mensaje) =>
        string.IsNullOrWhiteSpace(valor) || valor.Trim().Length > max
            ? throw new BusinessRuleException(codigo, mensaje)
            : valor.Trim();
}
