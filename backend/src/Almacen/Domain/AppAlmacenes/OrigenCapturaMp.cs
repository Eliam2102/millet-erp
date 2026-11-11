using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Domain.AppAlmacenes;

/// <summary>
/// M-335: quién captura cada evento de materia prima (recepción, consumo, merma, requisición): el ERP (captura interina) o la app de
/// Almacenes. Bitácora de solo inserción: el origen vigente es el último cambio del evento; revertir es otro cambio. Sin cambios el
/// evento está en transición (<see cref="Ambos"/>): se aceptan los dos orígenes, como antes de esta tarea.
/// </summary>
public sealed class OrigenCapturaMp
{
    public const string Recepcion = "recepcion";
    public const string Consumo = "consumo";
    public const string Merma = "merma";
    public const string Requisicion = "requisicion";
    public static readonly IReadOnlyList<string> Eventos = [Recepcion, Consumo, Merma, Requisicion];

    public const string Erp = "ERP";
    public const string App = "APP";
    public const string Ambos = "AMBOS";

    public Guid Id { get; private set; }
    public string Evento { get; private set; } = string.Empty;
    public string Origen { get; private set; } = string.Empty;
    public string? OrigenAnterior { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid CambiadoPor { get; private set; }
    public DateTimeOffset CambiadoAt { get; private set; }

    private OrigenCapturaMp() { }

    public OrigenCapturaMp(string evento, string origen, string origenAnterior, string motivo, Guid cambiadoPor)
    {
        if (!Eventos.Contains(evento))
            throw new BusinessRuleException("ORIGEN_CAPTURA_EVENTO_INVALIDO", $"Evento desconocido: {evento}.");
        if (origen is not (Erp or App))
            throw new BusinessRuleException("ORIGEN_CAPTURA_ORIGEN_INVALIDO", "El origen debe ser ERP o APP.");
        if (origen == origenAnterior)
            throw new BusinessRuleException("ORIGEN_CAPTURA_SIN_CAMBIO", $"El evento ya se captura en {Nombre(origen)}.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 500)
            throw new BusinessRuleException("ORIGEN_CAPTURA_MOTIVO", "El motivo del cambio es requerido (hasta 500 caracteres).");
        Id = Guid.CreateVersion7();
        Evento = evento;
        Origen = origen;
        OrigenAnterior = origenAnterior;
        Motivo = motivo.Trim();
        CambiadoPor = cambiadoPor;
        CambiadoAt = DateTimeOffset.UtcNow;
    }

    public static string Nombre(string origen) => origen switch { Erp => "el ERP", App => "la app de Almacenes", _ => "los dos orígenes" };

    public static string NombreEvento(string evento) => evento switch
    {
        Recepcion => "recepción", Consumo => "consumo", Merma => "merma", Requisicion => "requisición", _ => evento,
    };

    /// <summary>El evento de materia prima que captura un mensaje de la app (contrato v9); null = no está sujeto al cambio.</summary>
    public static string? EventoDeMensaje(string tipo) => tipo switch
    {
        "ENTRADA_ALMACEN" => Recepcion,
        "AJUSTE_SALIDA" => Consumo,
        "ROTURA" => Merma,
        "REQUISICION_ENVIADA" => Requisicion,
        _ => null,
    };

    public static IReadOnlyList<string> TiposDeMensaje(string evento) => evento switch
    {
        Recepcion => ["ENTRADA_ALMACEN"],
        Consumo => ["AJUSTE_SALIDA"],
        Merma => ["ROTURA"],
        Requisicion => ["REQUISICION_ENVIADA"],
        _ => [],
    };
}
