using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Incidencias;

/// <summary>Ciclo de una incidencia (U-4): pendiente = Abierta o EnAtencion; Resuelta y Cancelada son finales.</summary>
public enum EstadoIncidencia
{
    Abierta = 1,
    EnAtencion = 2,
    Resuelta = 3,
    Cancelada = 4,
}

/// <summary>
/// Incidencia común del ERP (U-4 · 86e3yzemw): la rama de falla de un paso deja constancia de qué documento se
/// detuvo, en qué paso, por qué, quién la atiende, con qué evidencia y dónde se retoma. Sirve a cualquier módulo
/// por <c>Modulo</c> + <c>TipoDocumento</c> + <c>DocumentoId</c> (sin FK: es genérica, como los adjuntos).
/// Tabla <c>compartido.incidencias</c>. Comercio Exterior y Compras conservan sus incidencias de dominio.
/// <para>
/// No hay dos pendientes iguales: (empresa, paso, documento, detalle) es único mientras esté Abierta o EnAtencion
/// (índice parcial); abrir de nuevo devuelve la existente.
/// </para>
/// </summary>
public sealed partial class Incidencia : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MotivoMin = 5;
    public const int MotivoMax = 1000;
    public const int DetalleMax = 2000;
    public const string ActorSistema = "Sistema";

    public Guid EmpresaId { get; set; }

    /// <summary>Folio consecutivo legible (identidad de base de datos).</summary>
    public long Numero { get; private set; }

    public string Modulo { get; private set; } = string.Empty;
    public string TipoDocumento { get; private set; } = string.Empty;
    public Guid DocumentoId { get; private set; }
    public string DocumentoEtiqueta { get; private set; } = string.Empty;
    public Guid? SucursalId { get; private set; }

    public string Paso { get; private set; } = string.Empty;
    public string? CodigoMotivo { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public string? Detalle { get; private set; }

    /// <summary>Distingue varias incidencias del mismo paso y documento (p. ej. la línea). Vacío = una por documento.</summary>
    public string DetalleClave { get; private set; } = string.Empty;

    public Guid ResponsableUsuarioId { get; private set; }
    public string ResponsableNombre { get; private set; } = string.Empty;
    public DateOnly? FechaCompromiso { get; private set; }

    public Guid? AbiertaPorId { get; private set; }
    public string AbiertaPorNombre { get; private set; } = ActorSistema;
    public bool Automatica { get; private set; }
    public DateTimeOffset AbiertaEn { get; private set; }

    public EstadoIncidencia Estado { get; private set; } = EstadoIncidencia.Abierta;

    public string? PasoReanudacion { get; private set; }
    public string? RutaReanudacion { get; private set; }

    public Guid? ResueltaPorId { get; private set; }
    public string? ResueltaPorNombre { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }
    public string? Resolucion { get; private set; }

    private readonly List<IncidenciaMovimiento> _historial = [];
    public IReadOnlyCollection<IncidenciaMovimiento> Historial => _historial.AsReadOnly();

    public bool Pendiente => Estado is EstadoIncidencia.Abierta or EstadoIncidencia.EnAtencion;

    private Incidencia() { }

    private Incidencia(Guid id) : base(id) { }

    /// <summary>Datos para abrir una incidencia; <paramref name="Actor"/> nulo = la abre el sistema (rechazo automático).</summary>
    public sealed record Datos(
        Guid EmpresaId,
        string Modulo,
        string TipoDocumento,
        Guid DocumentoId,
        string DocumentoEtiqueta,
        Guid? SucursalId,
        string Paso,
        string? CodigoMotivo,
        string Motivo,
        string? Detalle,
        string? DetalleClave,
        Guid ResponsableUsuarioId,
        string ResponsableNombre,
        DateOnly? FechaCompromiso,
        string? PasoReanudacion,
        string? RutaReanudacion);

    public static Incidencia Abrir(Datos d, Guid? actorId, string? actorNombre, DateTimeOffset ahora)
    {
        if (d.EmpresaId == Guid.Empty)
            throw new BusinessRuleException("INCIDENCIA_EMPRESA_REQUERIDA", "No se pudo identificar la empresa de la incidencia.");
        var modulo = Clave(d.Modulo, "modulo", 40);
        var tipo = Clave(d.TipoDocumento, "tipoDocumento", 60);
        var paso = Clave(d.Paso, "paso", 100);
        if (d.DocumentoId == Guid.Empty)
            throw new BusinessRuleException("INCIDENCIA_DOCUMENTO_REQUERIDO", "Indique el documento de la incidencia.");
        if (d.ResponsableUsuarioId == Guid.Empty || string.IsNullOrWhiteSpace(d.ResponsableNombre))
            throw new BusinessRuleException("INCIDENCIA_RESPONSABLE_INVALIDO", "Indique un responsable válido.");
        var ruta = Opcional(d.RutaReanudacion, 300);
        if (ruta is not null && !ruta.StartsWith('/'))
            throw new BusinessRuleException("INCIDENCIA_RUTA_INVALIDA", "La ruta para retomar debe empezar con «/».");

        var i = new Incidencia(Guid.CreateVersion7())
        {
            EmpresaId = d.EmpresaId,
            Modulo = modulo,
            TipoDocumento = tipo,
            DocumentoId = d.DocumentoId,
            DocumentoEtiqueta = Requerido(d.DocumentoEtiqueta, 200, "INCIDENCIA_DOCUMENTO_REQUERIDO", "Indique el documento de la incidencia."),
            SucursalId = d.SucursalId == Guid.Empty ? null : d.SucursalId,
            Paso = paso,
            CodigoMotivo = Opcional(d.CodigoMotivo, 100),
            Motivo = Texto(d.Motivo, MotivoMax, "INCIDENCIA_MOTIVO_REQUERIDO", "Describa el motivo de la incidencia (5 a 1000 caracteres)."),
            Detalle = Opcional(d.Detalle, DetalleMax),
            DetalleClave = Opcional(d.DetalleClave, 100) ?? string.Empty,
            ResponsableUsuarioId = d.ResponsableUsuarioId,
            ResponsableNombre = Recortar(d.ResponsableNombre.Trim(), 200),
            FechaCompromiso = d.FechaCompromiso,
            AbiertaPorId = actorId,
            AbiertaPorNombre = Actor(actorId, actorNombre),
            Automatica = actorId is null,
            AbiertaEn = ahora,
            PasoReanudacion = d.PasoReanudacion is null ? null : Clave(d.PasoReanudacion, "pasoReanudacion", 100),
            RutaReanudacion = ruta,
        };
        i.Registrar("Abierta", null, EstadoIncidencia.Abierta, actorId, i.AbiertaPorNombre, null, ahora);
        return i;
    }

    /// <summary>El responsable (o quien gestiona) toma la incidencia: Abierta → EnAtencion.</summary>
    public void Atender(Guid actorId, string actorNombre, string? nota, DateTimeOffset ahora)
    {
        if (Estado != EstadoIncidencia.Abierta)
            throw Transicion("atender");
        Cambiar(EstadoIncidencia.EnAtencion, "Atendida", actorId, actorNombre, Opcional(nota, MotivoMax), ahora);
    }

    /// <summary>Resuelve la incidencia; <paramref name="actorId"/> nulo = la resolvió el sistema al retomarse el paso.</summary>
    public void Resolver(Guid? actorId, string? actorNombre, string resolucion, DateTimeOffset ahora)
    {
        if (!Pendiente)
            throw Transicion("resolver");
        var texto = Texto(resolucion, MotivoMax, "INCIDENCIA_RESOLUCION_REQUERIDA", "Describa cómo se resolvió (5 a 1000 caracteres).");
        var nombre = Actor(actorId, actorNombre);
        ResueltaPorId = actorId;
        ResueltaPorNombre = nombre;
        ResueltaEn = ahora;
        Resolucion = texto;
        Cambiar(EstadoIncidencia.Resuelta, "Resuelta", actorId, nombre, texto, ahora);
    }

    public void Cancelar(Guid actorId, string actorNombre, string motivo, DateTimeOffset ahora)
    {
        if (!Pendiente)
            throw Transicion("cancelar");
        var texto = Texto(motivo, MotivoMax, "INCIDENCIA_MOTIVO_REQUERIDO", "Indique por qué se cancela (5 a 1000 caracteres).");
        Cambiar(EstadoIncidencia.Cancelada, "Cancelada", actorId, actorNombre, texto, ahora);
    }

    public void Reasignar(Guid nuevoResponsableId, string nuevoResponsableNombre, Guid actorId, string actorNombre, string? nota, DateTimeOffset ahora)
    {
        if (!Pendiente)
            throw Transicion("reasignar");
        if (nuevoResponsableId == Guid.Empty || nuevoResponsableId == ResponsableUsuarioId || string.IsNullOrWhiteSpace(nuevoResponsableNombre))
            throw new BusinessRuleException("INCIDENCIA_RESPONSABLE_INVALIDO", "Elija un responsable distinto del actual.");
        var anterior = ResponsableNombre;
        ResponsableUsuarioId = nuevoResponsableId;
        ResponsableNombre = Recortar(nuevoResponsableNombre.Trim(), 200);
        var texto = $"De {anterior} a {ResponsableNombre}." + (Opcional(nota, 800) is { } n ? $" {n}" : string.Empty);
        Registrar("Reasignada", Estado, Estado, actorId, actorNombre, texto, ahora);
    }

    private void Cambiar(EstadoIncidencia nuevo, string accion, Guid? actorId, string actorNombre, string? nota, DateTimeOffset ahora)
    {
        var anterior = Estado;
        Estado = nuevo;
        Registrar(accion, anterior, nuevo, actorId, actorNombre, nota, ahora);
    }

    private void Registrar(string accion, EstadoIncidencia? anterior, EstadoIncidencia nuevo, Guid? actorId, string actorNombre, string? nota, DateTimeOffset ahora) =>
        _historial.Add(new IncidenciaMovimiento(Guid.CreateVersion7(), Id, _historial.Count + 1, accion, anterior, nuevo, actorId, actorNombre, nota, ahora));

    private BusinessRuleException Transicion(string accion) =>
        new("INCIDENCIA_TRANSICION_INVALIDA", $"No se puede {accion} una incidencia {EstadoTexto(Estado)}.");

    public static string EstadoTexto(EstadoIncidencia e) => e switch
    {
        EstadoIncidencia.Abierta => "abierta",
        EstadoIncidencia.EnAtencion => "en atención",
        EstadoIncidencia.Resuelta => "resuelta",
        _ => "cancelada",
    };

    private static string Actor(Guid? actorId, string? nombre) =>
        actorId is null ? ActorSistema : Recortar(string.IsNullOrWhiteSpace(nombre) ? "Usuario" : nombre.Trim(), 200);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex ClaveRegex();

    private static string Clave(string? valor, string campo, int max)
    {
        var v = (valor ?? string.Empty).Trim();
        if (v.Length == 0 || v.Length > max || !ClaveRegex().IsMatch(v))
            throw new BusinessRuleException("INCIDENCIA_CLAVE_INVALIDA", $"El campo {campo} solo admite minúsculas, dígitos, punto, guion y guion bajo (máx. {max}).");
        return v;
    }

    private static string Texto(string? valor, int max, string codigo, string mensaje)
    {
        var v = (valor ?? string.Empty).Trim();
        if (v.Length < MotivoMin || v.Length > max)
            throw new BusinessRuleException(codigo, mensaje);
        return v;
    }

    private static string Requerido(string? valor, int max, string codigo, string mensaje)
    {
        var v = (valor ?? string.Empty).Trim();
        if (v.Length == 0)
            throw new BusinessRuleException(codigo, mensaje);
        return Recortar(v, max);
    }

    private static string? Opcional(string? valor, int max)
    {
        var v = valor?.Trim();
        return string.IsNullOrEmpty(v) ? null : Recortar(v, max);
    }

    private static string Recortar(string v, int max) => v.Length <= max ? v : v[..max];
}

/// <summary>Renglón inmutable del historial de una incidencia (quién, cuándo, de qué estado a cuál y la nota).</summary>
public sealed class IncidenciaMovimiento : BaseEntity, INotAudited
{
    public Guid IncidenciaId { get; private set; }
    public int Orden { get; private set; }
    public string Accion { get; private set; } = string.Empty;
    public EstadoIncidencia? EstadoAnterior { get; private set; }
    public EstadoIncidencia EstadoNuevo { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string UsuarioNombre { get; private set; } = string.Empty;
    public string? Nota { get; private set; }
    public DateTimeOffset En { get; private set; }

    private IncidenciaMovimiento() { }

    internal IncidenciaMovimiento(Guid id, Guid incidenciaId, int orden, string accion, EstadoIncidencia? anterior, EstadoIncidencia nuevo,
        Guid? usuarioId, string usuarioNombre, string? nota, DateTimeOffset en) : base(id)
    {
        IncidenciaId = incidenciaId;
        Orden = orden;
        Accion = accion;
        EstadoAnterior = anterior;
        EstadoNuevo = nuevo;
        UsuarioId = usuarioId;
        UsuarioNombre = usuarioNombre;
        Nota = nota;
        En = en;
    }
}
