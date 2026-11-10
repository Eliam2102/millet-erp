using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// M-160: aviso a A+W (factura, pago, estatus) que no se pudo escribir en <c>aw_retorno</c> porque A+W no respondía.
/// Guarda el evento tal cual llegó (tipo + JSON) y se reintenta con espera creciente (1, 5, 15, 60 min y luego cada hora,
/// sin tope). Al aplicarse queda <see cref="EstadoAplicado"/>; una falla que no es de conexión lo deja <see cref="EstadoDetenido"/>.
/// La idempotencia de <c>aw_retorno</c> (UUID + tipo, estatus, último importe) evita duplicar si se aplica dos veces.
/// </summary>
public sealed class AwRetornoPendiente : BaseEntity, INotAudited
{
    public const string EstadoPendiente = "Pendiente";
    public const string EstadoAplicado = "Aplicado";
    public const string EstadoDetenido = "Detenido";
    public const int MaxError = 2000;

    public string TipoEvento { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public int Intentos { get; private set; }
    public DateTimeOffset ProximoIntentoUtc { get; private set; }
    public string? UltimoError { get; private set; }
    public string Estado { get; private set; } = EstadoPendiente;
    public DateTimeOffset CreadoEnUtc { get; private set; }
    public DateTimeOffset? AplicadoEnUtc { get; private set; }

    private AwRetornoPendiente() { } // EF Core

    /// <summary>El primer intento ya falló (el del evento): el siguiente va en 1 minuto.</summary>
    public static AwRetornoPendiente Crear(string tipoEvento, string payload, string error, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), TipoEvento = tipoEvento, Payload = payload, Intentos = 1, CreadoEnUtc = ahora,
        UltimoError = Recortar(error), ProximoIntentoUtc = ahora + Espera(1),
    };

    /// <summary>Espera después del intento número <paramref name="intentos"/>: 1, 5, 15, 60 min y luego cada hora.</summary>
    public static TimeSpan Espera(int intentos) => TimeSpan.FromMinutes(intentos switch
    {
        <= 1 => 1,
        2 => 5,
        3 => 15,
        _ => 60,
    });

    public void RegistrarFallo(string error, DateTimeOffset ahora)
    {
        Intentos++;
        UltimoError = Recortar(error);
        ProximoIntentoUtc = ahora + Espera(Intentos);
    }

    public void MarcarAplicado(DateTimeOffset ahora)
    {
        Intentos++;
        Estado = EstadoAplicado;
        AplicadoEnUtc = ahora;
    }

    /// <summary>Falla que no es de conexión (payload, equivalencia de pago): ya no se reintenta solo; la revisa una persona.</summary>
    public void Detener(string error)
    {
        Intentos++;
        Estado = EstadoDetenido;
        UltimoError = Recortar(error);
    }

    private static string Recortar(string error) => error.Length <= MaxError ? error : error[..MaxError];
}
