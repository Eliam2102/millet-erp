namespace Millet.SharedKernel.Application.Correo;

/// <summary>
/// Transporte único de correo saliente del ERP (86e3txv0n, ADR-0063). Los
/// módulos lo consumen detrás de sus propios puertos (CFDI, OC, estado de
/// cuenta, avisos de Almacén y CxP). Cada intento queda en
/// <c>core.correo_envio</c>; el reintento lo decide el módulo dueño del envío.
/// </summary>
public interface ICorreoSaliente
{
    /// <summary><c>true</c> cuando el ambiente no entrega correo (proveedor NoOp).</summary>
    bool EsSimulado { get; }

    /// <summary>Nunca lanza por un rechazo del proveedor: lo devuelve como <see cref="EstadoCorreoSaliente.Error"/>.</summary>
    Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken);
}

/// <summary>
/// Correo a enviar. <paramref name="Modulo"/>, <paramref name="Tipo"/> y
/// <paramref name="ReferenciaId"/> solo sirven al registro (qué se envió).
/// <paramref name="ParaOriginal"/> lo llena el modo pruebas al redirigir.
/// </summary>
public sealed record MensajeCorreo(
    Guid? EmpresaId,
    string Modulo,
    string Tipo,
    Guid? ReferenciaId,
    IReadOnlyList<string> Para,
    string Asunto,
    string Html,
    IReadOnlyList<AdjuntoCorreo>? Adjuntos = null,
    IReadOnlyList<string>? ParaOriginal = null)
{
    public IReadOnlyList<AdjuntoCorreo> AdjuntosOVacio => Adjuntos ?? [];
}

public sealed record AdjuntoCorreo(string Nombre, string ContentType, byte[] Contenido);

public enum EstadoCorreoSaliente : short
{
    /// <summary>El proveedor aceptó la solicitud (no equivale a entrega en el buzón).</summary>
    Aceptado = 1,
    Error = 2,

    /// <summary>Proveedor NoOp: no salió ningún correo.</summary>
    Simulado = 3,
}

public sealed record ResultadoCorreoSaliente(EstadoCorreoSaliente Estado, string? Codigo = null, string? Motivo = null)
{
    public static ResultadoCorreoSaliente Aceptado { get; } = new(EstadoCorreoSaliente.Aceptado);
    public static ResultadoCorreoSaliente Simulado { get; } = new(EstadoCorreoSaliente.Simulado);
    public static ResultadoCorreoSaliente Error(string codigo, string motivo) => new(EstadoCorreoSaliente.Error, codigo, motivo);

    public bool FueAceptado => Estado == EstadoCorreoSaliente.Aceptado;
}

/// <summary>Códigos de error del transporte.</summary>
public static class CodigosCorreoSaliente
{
    public const string PermisosInsuficientes = "CORREO_PERMISOS_INSUFICIENTES";
    public const string ErrorEnvio = "CORREO_ERROR_ENVIO";
    public const string AdjuntosExceden3Mb = "CORREO_ADJUNTOS_EXCEDEN_3MB";
    public const string SinDestinatarios = "CORREO_SIN_DESTINATARIOS";

    /// <summary>Límite del envío simple de Graph (petición de 4 MB con adjuntos en base64).</summary>
    public const long LimiteAdjuntosBytes = 3L * 1024 * 1024;
}
