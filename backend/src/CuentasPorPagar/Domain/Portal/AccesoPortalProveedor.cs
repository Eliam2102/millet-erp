using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Portal;

/// <summary>
/// Correo autorizado de un proveedor para entrar al portal de proveedores.
/// El correo es único entre los accesos (un correo = un proveedor): el inicio de
/// sesión se resuelve solo con el correo y el código.
/// </summary>
public sealed class AccesoPortalProveedor : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ProveedorId { get; private set; }
    public string Email { get; private set; } = default!;
    public bool Activo { get; private set; } = true;

    private AccesoPortalProveedor() { }

    public AccesoPortalProveedor(Guid empresaId, Guid proveedorId, string email) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        ProveedorId = proveedorId;
        Email = Normalizar(email);
        if (!EsCorreoValido(Email))
            throw new BusinessRuleException("PORTAL_CORREO_INVALIDO", "El correo del acceso al portal no es válido.");
    }

    public void CambiarActivo(bool activo) => Activo = activo;

    public static string Normalizar(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool EsCorreoValido(string email) =>
        email.Length is > 3 and <= 254 && email.Count(c => c == '@') == 1 && !email.Any(char.IsWhiteSpace)
        && email.IndexOf('@') > 0 && email.LastIndexOf('.') > email.IndexOf('@') + 1 && !email.EndsWith('.');
}

/// <summary>
/// Código de un solo uso (6 dígitos) enviado por correo. Se guarda solo el hash;
/// vence a los 10 minutos y admite 5 intentos (<see cref="MaxIntentos"/>).
/// </summary>
public sealed class CodigoPortal : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public const int MaxIntentos = 5;
    public const int MaxPorHora = 3;
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    public Guid EmpresaId { get; set; }
    public Guid AccesoId { get; private set; }
    public string CodigoHash { get; private set; } = default!;
    public DateTimeOffset ExpiraEn { get; private set; }
    public int Intentos { get; private set; }
    public bool Consumido { get; private set; }

    private CodigoPortal() { }

    public CodigoPortal(Guid empresaId, Guid accesoId, string codigo, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        AccesoId = accesoId;
        CodigoHash = Hash(Id, codigo);
        ExpiraEn = ahora + Vigencia;
    }

    // ponytail: SHA-256 con el id del código como sal; el límite de 5 intentos y los 10 min de vida acotan la fuerza bruta.
    public static string Hash(Guid id, string codigo) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{id:N}:{codigo}")));

    public bool Vigente(DateTimeOffset ahora) => !Consumido && Intentos < MaxIntentos && ahora < ExpiraEn;

    /// <summary>Registra el intento; devuelve true solo si el código coincide y estaba vigente.</summary>
    public bool Verificar(string codigo, DateTimeOffset ahora)
    {
        if (!Vigente(ahora)) return false;
        var ok = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(CodigoHash), System.Text.Encoding.ASCII.GetBytes(Hash(Id, codigo)));
        if (ok) Consumido = true; else Intentos++;
        return ok;
    }
}
