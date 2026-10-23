using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>Entidad del master cuyo dato fiscal se compara (U1.8b).</summary>
public enum TipoEntidadDiferenciaFiscal { Cliente, Producto }

/// <summary>
/// Revisión de una diferencia fiscal entre A+W (o la regla/mapeo, en productos) y el ERP (U1.8b, F1-FAC-14).
/// Una por (tipo, entidad, campo). Guarda la <see cref="Huella"/> de los valores comparados al revisar: el
/// reporte oculta la diferencia mientras la huella actual sea la misma y la vuelve a mostrar cuando cambia
/// cualquiera de los dos valores. Marcar revisado NO corrige nada (ni en el ERP ni en A+W).
/// </summary>
public sealed class DiferenciaFiscalAwRevision : BaseEntity, IAuditable
{
    public const int MaxCampo = 40;
    public const int MaxValor = 300;
    public const int MaxNota = 500;

    public TipoEntidadDiferenciaFiscal Tipo { get; private set; }
    public Guid EntidadId { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;
    public string Campo { get; private set; } = string.Empty;
    public string? ValorOrigen { get; private set; }
    public string? ValorErp { get; private set; }

    /// <summary>SHA-256 hex de (campo, valor origen, valor ERP) normalizados.</summary>
    public string Huella { get; private set; } = string.Empty;

    public Guid RevisadoPor { get; private set; }
    public string? RevisadoPorNombre { get; private set; }
    public DateTimeOffset RevisadoEn { get; private set; }
    public string? Nota { get; private set; }

    private DiferenciaFiscalAwRevision() { }

    public DiferenciaFiscalAwRevision(
        Guid id, TipoEntidadDiferenciaFiscal tipo, Guid entidadId, string referenciaExterna, string campo) : base(id)
    {
        if (entidadId == Guid.Empty || string.IsNullOrWhiteSpace(campo) || campo.Length > MaxCampo)
            throw new BusinessRuleException("DIFERENCIA_FISCAL_INVALIDA", "Indica la entidad y el campo de la diferencia.");
        Tipo = tipo;
        EntidadId = entidadId;
        ReferenciaExterna = Recortar(referenciaExterna, 50) ?? string.Empty;
        Campo = campo;
    }

    /// <summary>Registra (o actualiza) la revisión con los valores vistos por el usuario.</summary>
    public void Revisar(string huella, string? valorOrigen, string? valorErp, Guid usuario, string? usuarioNombre,
        DateTimeOffset ahora, string? nota)
    {
        if (usuario == Guid.Empty)
            throw new BusinessRuleException("USUARIO_REQUERIDO", "Inicia sesión para marcar la diferencia como revisada.");
        if (string.IsNullOrWhiteSpace(huella) || huella.Length != 64)
            throw new BusinessRuleException("DIFERENCIA_FISCAL_INVALIDA", "La huella de la diferencia no es válida.");
        if (nota is { Length: > MaxNota })
            throw new BusinessRuleException("DIFERENCIA_FISCAL_NOTA", $"La nota no puede exceder {MaxNota} caracteres.");
        Huella = huella;
        ValorOrigen = Recortar(valorOrigen, MaxValor);
        ValorErp = Recortar(valorErp, MaxValor);
        RevisadoPor = usuario;
        RevisadoPorNombre = Recortar(usuarioNombre, 200);
        RevisadoEn = ahora;
        Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
    }

    private static string? Recortar(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}

/// <summary>Quién originó la corrida persistida del reporte.</summary>
public enum OrigenCorridaDiferenciaFiscal { Manual, Programada }

/// <summary>
/// Corrida persistida del reporte de diferencias fiscales (worker semanal o "Ejecutar y guardar"). Es una
/// foto de diagnóstico: no se audita (ADR-0008) y no reemplaza al cálculo en vivo.
/// </summary>
public sealed class DiferenciaFiscalAwCorrida : BaseEntity, INotAudited
{
    public OrigenCorridaDiferenciaFiscal Origen { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset IniciadaEn { get; private set; }
    public DateTimeOffset? TerminadaEn { get; private set; }
    public bool Exitosa { get; private set; }
    public int ClientesComparados { get; private set; }
    public int ProductosComparados { get; private set; }
    public int Diferencias { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Avisos de la corrida (p. ej. lectura de clientes A+W deshabilitada), separados por salto de línea.</summary>
    public string? Avisos { get; private set; }

    private readonly List<DiferenciaFiscalAwCorridaFila> _filas = [];
    public IReadOnlyList<DiferenciaFiscalAwCorridaFila> Filas => _filas;

    private DiferenciaFiscalAwCorrida() { }

    public DiferenciaFiscalAwCorrida(Guid id, OrigenCorridaDiferenciaFiscal origen, string actor, DateTimeOffset iniciadaEn) : base(id)
    {
        Origen = origen;
        Actor = actor.Length <= 200 ? actor : actor[..200];
        IniciadaEn = iniciadaEn;
    }

    public void Completar(int clientes, int productos, IEnumerable<DiferenciaFiscalAwCorridaFila> filas,
        IEnumerable<string> avisos, DateTimeOffset ahora)
    {
        _filas.AddRange(filas);
        ClientesComparados = clientes;
        ProductosComparados = productos;
        Diferencias = _filas.Count;
        Avisos = string.Join('\n', avisos) is { Length: > 0 } a ? a : null;
        Exitosa = true;
        TerminadaEn = ahora;
    }

    public void Fallar(string error, DateTimeOffset ahora)
    {
        Error = error.Length <= 1000 ? error : error[..1000];
        Exitosa = false;
        TerminadaEn = ahora;
    }
}

/// <summary>Fila de una corrida persistida (diferencia vigente, no revisada, al momento de la corrida).</summary>
public sealed class DiferenciaFiscalAwCorridaFila : BaseEntity, INotAudited
{
    public Guid CorridaId { get; private set; }
    public TipoEntidadDiferenciaFiscal Tipo { get; private set; }
    public Guid EntidadId { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Campo { get; private set; } = string.Empty;
    public string Fuente { get; private set; } = string.Empty;
    public string? ValorOrigen { get; private set; }
    public string? ValorErp { get; private set; }
    public string Estado { get; private set; } = string.Empty;
    public string Huella { get; private set; } = string.Empty;

    private DiferenciaFiscalAwCorridaFila() { }

    public DiferenciaFiscalAwCorridaFila(Guid id, Guid corridaId, TipoEntidadDiferenciaFiscal tipo, Guid entidadId,
        string referenciaExterna, string nombre, string campo, string fuente, string? valorOrigen, string? valorErp,
        string estado, string huella) : base(id)
    {
        CorridaId = corridaId;
        Tipo = tipo;
        EntidadId = entidadId;
        ReferenciaExterna = Recortar(referenciaExterna, 50)!;
        Nombre = Recortar(nombre, 300)!;
        Campo = campo;
        Fuente = Recortar(fuente, 120)!;
        ValorOrigen = Recortar(valorOrigen, DiferenciaFiscalAwRevision.MaxValor);
        ValorErp = Recortar(valorErp, DiferenciaFiscalAwRevision.MaxValor);
        Estado = estado;
        Huella = huella;
    }

    private static string? Recortar(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}
