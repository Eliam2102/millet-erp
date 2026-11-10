using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>De dónde viene la prueba de conexión registrada.</summary>
public enum OrigenPruebaConexionPac : short
{
    /// <summary>«Probar conexión» sobre la configuración guardada.</summary>
    Prueba = 1,
    /// <summary>La prueba obligatoria al guardar la configuración activa (P01).</summary>
    Guardado = 2,
}

/// <summary>
/// M-232 (86e3zr7n2): registro de cada prueba de conexión con el timbrador (fecha, resultado y mensaje). Va aparte de
/// <see cref="ConfiguracionPac"/> a propósito: probar la conexión no cambia la configuración fiscal (ni su versión).
/// Solo se registra lo probado contra la configuración guardada (no un candidato distinto).
/// </summary>
public sealed class PruebaConexionPac : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public const int MensajeMaxLength = 500;

    public Guid EmpresaId { get; set; }
    public ProveedorPac Proveedor { get; private set; }
    public Guid ConfiguracionPacId { get; private set; }
    public OrigenPruebaConexionPac Origen { get; private set; }
    public bool Exitosa { get; private set; }
    public int StatusCode { get; private set; }
    public string Mensaje { get; private set; } = string.Empty;
    public DateTimeOffset RealizadaEn { get; private set; }
    public string? RealizadaPor { get; private set; }

    private PruebaConexionPac() { } // EF Core

    public PruebaConexionPac(Guid empresaId, ProveedorPac proveedor, Guid configuracionPacId, OrigenPruebaConexionPac origen,
        bool exitosa, int statusCode, string mensaje, DateTimeOffset realizadaEn, string? realizadaPor)
        : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        Proveedor = proveedor;
        ConfiguracionPacId = configuracionPacId;
        Origen = origen;
        Exitosa = exitosa;
        StatusCode = statusCode;
        var texto = (mensaje ?? string.Empty).Trim();
        Mensaje = texto.Length <= MensajeMaxLength ? texto : texto[..MensajeMaxLength];
        RealizadaEn = realizadaEn;
        RealizadaPor = realizadaPor is { Length: > 200 } ? realizadaPor[..200] : realizadaPor;
    }
}
