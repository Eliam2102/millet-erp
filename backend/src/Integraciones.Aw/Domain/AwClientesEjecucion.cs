using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

public enum AwClientesEjecucionTipo : short { Barrido = 0, Referencia = 1 }

public enum AwClientesEjecucionEstado : short
{
    Pendiente = 0,
    EnCurso = 1,
    Completa = 2,
    Parcial = 3,
    Fallida = 4,
    Cancelada = 5,
}

/// <summary>
/// Ejecución recuperable de la sincronización de clientes A+W (ADM-06, doc 05 §7).
/// El progreso (<see cref="CursorActual"/> + contadores) se persiste tras cada lote, de modo
/// que una caída deja la ejecución <c>EnCurso</c> y se reanuda sin duplicar. Sin auditoría
/// por fila (es telemetría operativa, no dato de negocio).
/// </summary>
public sealed class AwClientesEjecucion : BaseEntity, INotAudited
{
    public const int MensajeErrorMaxLength = 500;

    private readonly List<AwClientesEjecucionError> _errores = [];

    public string Origen { get; private set; } = string.Empty;
    public AwClientesEjecucionTipo Tipo { get; private set; }
    public AwClientesEjecucionEstado Estado { get; private set; }
    public string? CursorActual { get; private set; }
    public int Leidos { get; private set; }
    public int Creados { get; private set; }
    public int Actualizados { get; private set; }
    public int SinCambios { get; private set; }
    public int Pendientes { get; private set; }
    public int Conflictos { get; private set; }
    public int Errores { get; private set; }
    public DateTimeOffset IniciadaEnUtc { get; private set; }
    public DateTimeOffset? TerminadaEnUtc { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public Guid? ReintentoDeId { get; private set; }
    public string? ErrorGeneral { get; private set; }

    public IReadOnlyCollection<AwClientesEjecucionError> ErroresPorReferencia => _errores;

    private AwClientesEjecucion() { } // EF Core

    public static AwClientesEjecucion Crear(
        string origen, AwClientesEjecucionTipo tipo, string actor, DateTimeOffset ahora, Guid? reintentoDeId = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Origen = origen,
            Tipo = tipo,
            Estado = AwClientesEjecucionEstado.Pendiente,
            IniciadaEnUtc = ahora,
            Actor = actor,
            ReintentoDeId = reintentoDeId,
        };

    /// <summary>Viva = ocupa el cupo de un barrido por origen (índice único parcial).</summary>
    public bool EstaViva => Estado is AwClientesEjecucionEstado.Pendiente or AwClientesEjecucionEstado.EnCurso;

    public void Iniciar() => Estado = AwClientesEjecucionEstado.EnCurso;

    /// <summary>Pendiente es ortogonal a la acción: se suma además de Creado/Actualizado/SinCambios.</summary>
    public void AcumularFila(AwClientesFilaResultado accion, bool esPendiente)
    {
        Leidos++;
        if (esPendiente) Pendientes++;
        switch (accion)
        {
            case AwClientesFilaResultado.Creado: Creados++; break;
            case AwClientesFilaResultado.Actualizado: Actualizados++; break;
            case AwClientesFilaResultado.SinCambios: SinCambios++; break;
            case AwClientesFilaResultado.Conflicto: Conflictos++; break;
        }
    }

    public void RegistrarError(string referencia, string codigo, string mensaje, DateTimeOffset ahora)
    {
        Leidos++;
        Errores++;
        _errores.Add(new AwClientesEjecucionError(Id, referencia, codigo, mensaje, ahora));
    }

    public void AvanzarCursor(string? cursor) => CursorActual = cursor;

    /// <summary>Barrido terminado: Parcial si alguna fila falló, Completa si no.</summary>
    public void Finalizar(DateTimeOffset ahora)
    {
        Estado = Errores > 0 ? AwClientesEjecucionEstado.Parcial : AwClientesEjecucionEstado.Completa;
        TerminadaEnUtc = ahora;
    }

    public void Fallar(string mensaje, DateTimeOffset ahora)
    {
        Estado = AwClientesEjecucionEstado.Fallida;
        ErrorGeneral = Recortar(mensaje);
        TerminadaEnUtc = ahora;
    }

    public static string Recortar(string s)
        => s.Length <= MensajeErrorMaxLength ? s : s[..MensajeErrorMaxLength];
}

/// <summary>Resultado de aplicar una fila, ya reducido a lo que cuenta la ejecución.</summary>
public enum AwClientesFilaResultado { Creado, Actualizado, SinCambios, Pendiente, Conflicto }

/// <summary>Error por referencia: código + mensaje corto, nunca el payload completo.</summary>
public sealed class AwClientesEjecucionError : BaseEntity, INotAudited
{
    public Guid EjecucionId { get; private set; }
    public string Referencia { get; private set; } = string.Empty;
    public string Codigo { get; private set; } = string.Empty;
    public string Mensaje { get; private set; } = string.Empty;
    public DateTimeOffset OcurridoEnUtc { get; private set; }

    private AwClientesEjecucionError() { } // EF Core

    internal AwClientesEjecucionError(Guid ejecucionId, string referencia, string codigo, string mensaje, DateTimeOffset ahora)
        : base(Guid.CreateVersion7())
    {
        EjecucionId = ejecucionId;
        Referencia = referencia.Length <= 40 ? referencia : referencia[..40];
        Codigo = codigo.Length <= 60 ? codigo : codigo[..60];
        Mensaje = AwClientesEjecucion.Recortar(mensaje);
        OcurridoEnUtc = ahora;
    }
}
