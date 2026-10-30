namespace Millet.CuentasPorPagar.Domain.Ports.Portal;

/// <summary>Envía el código de acceso al portal de proveedores (Graph desde erpmillet@; NoOp en Development).</summary>
public interface IPortalCorreoPort
{
    Task EnviarCodigoAsync(string destinatario, string codigo, CancellationToken cancellationToken);
}

/// <summary>Emite el token propio del portal (audiencia distinta a la del ERP, sin permisos del ERP). Lo implementa el Api.</summary>
public interface IPortalTokenEmisor
{
    PortalToken Emitir(Guid proveedorId, Guid empresaId, string email);
}

public sealed record PortalToken(string Token, DateTimeOffset ExpiraEn);

/// <summary>Inicio de sesión del portal: no tiene usuario ni empresa todavía, así que corre fuera del filtro de empresa.</summary>
public interface IPortalProveedorAutenticacion
{
    /// <summary>Siempre termina sin error aunque el correo no esté autorizado (sin enumeración de usuarios).</summary>
    Task SolicitarCodigoAsync(string? email, CancellationToken cancellationToken);

    /// <summary>Devuelve null si el correo o el código no son válidos, vencieron o se agotaron los intentos.</summary>
    Task<PortalToken?> IniciarSesionAsync(string? email, string? codigo, CancellationToken cancellationToken);
}
