using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// Estado de un periodo contable (F1-CON-03, D2): <c>NoAbierto → Abierto → Cerrado</c>; <c>Cerrado → Abierto</c> solo por
/// reapertura. El valor es fijo por ABI: agregar al final.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoPeriodo : short { NoAbierto = 0, Abierto = 1, Cerrado = 2 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccionPeriodo : short { Abrir = 1, Cerrar = 2, Reabrir = 3 }

/// <summary>
/// Ejercicio contable (D1): uno por año natural y empresa. Al crearse genera los 12 periodos ordinarios y el 13 de ajuste,
/// todos en <see cref="EstadoPeriodo.NoAbierto"/> (D2). Su versión es el candado de la apertura en lote.
/// Calendario de año natural, confirmado en el diseño contable D13 (bóveda de Obsidian).
/// </summary>
public sealed class EjercicioContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int AnioMinimo = 2000, AnioMaximo = 2999;

    public Guid EmpresaId { get; set; }
    public int Anio { get; private set; }

    private EjercicioContable() { }

    public EjercicioContable(Guid id, int anio) : base(id)
    {
        if (anio is < AnioMinimo or > AnioMaximo)
            throw new BusinessRuleException("CONTAB_EJERCICIO_ANIO_INVALIDO", $"El año del ejercicio debe estar entre {AnioMinimo} y {AnioMaximo}.");
        Anio = anio;
    }

    public IReadOnlyList<PeriodoContable> GenerarPeriodos() =>
        [.. Enumerable.Range(1, PeriodoContable.NumeroAjuste).Select(n => new PeriodoContable(Guid.CreateVersion7(), Id, Anio, n))];
}

/// <summary>
/// Periodo contable (C1.1). 1–12 = meses; 13 = ajustes de auditoría (D3): fechas 31-dic, nunca se resuelve por fecha (una fecha
/// de diciembre cae en el 12), solo se abre con el 12 cerrado y solo admite movimientos <see cref="OrigenMovimiento.Manual"/>.
/// Cada transición devuelve su renglón de bitácora con la versión que tendrá el periodo al guardarse (la versión la incrementa
/// el interceptor de metadatos en <c>SaveChanges</c>).
/// Reglas D4 (supuestos a validar con Contabilidad): cierre secuencial (los anteriores del ejercicio cerrados) y reapertura solo
/// si el siguiente no está cerrado («reabra primero febrero»). Abrir no exige orden.
/// </summary>
public sealed class PeriodoContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int NumeroAjuste = 13;
    public const int MotivoMinimo = 10, MotivoMaximo = 500;

    public Guid EmpresaId { get; set; }
    public Guid EjercicioId { get; private set; }
    public int Anio { get; private set; }
    public int Numero { get; private set; }
    public DateOnly FechaInicio { get; private set; }
    public DateOnly FechaFin { get; private set; }
    public EstadoPeriodo Estado { get; private set; }
    public string? AbiertoPor { get; private set; }
    public DateTimeOffset? AbiertoEn { get; private set; }
    public string? CerradoPor { get; private set; }
    public DateTimeOffset? CerradoEn { get; private set; }
    public string? ReabiertoPor { get; private set; }
    public DateTimeOffset? ReabiertoEn { get; private set; }

    public bool EsAjuste => Numero == NumeroAjuste;

    /// <summary>«2026-09» o «2026-13» para los mensajes.</summary>
    public string Clave => Etiqueta(Anio, Numero);

    private PeriodoContable() { }

    internal PeriodoContable(Guid id, Guid ejercicioId, int anio, int numero) : base(id)
    {
        if (numero is < 1 or > NumeroAjuste)
            throw new BusinessRuleException("CONTAB_PERIODO_NUMERO_INVALIDO", "El número de periodo debe estar entre 1 y 13.");
        EjercicioId = ejercicioId;
        Anio = anio;
        Numero = numero;
        FechaInicio = numero == NumeroAjuste ? new DateOnly(anio, 12, 31) : new DateOnly(anio, numero, 1);
        FechaFin = numero == NumeroAjuste ? new DateOnly(anio, 12, 31) : FechaInicio.AddMonths(1).AddDays(-1);
        Estado = EstadoPeriodo.NoAbierto;
    }

    public static string Etiqueta(int anio, int numero) => $"{anio}-{numero:00}";

    /// <summary>Periodo ordinario (1–12) de una fecha. El 13 nunca se resuelve por fecha (D3).</summary>
    public static (int Anio, int Numero) PorFecha(DateOnly fecha) => (fecha.Year, fecha.Month);

    /// <summary>D3: el periodo de ajuste solo admite movimientos manuales (la condición «póliza autorizada» es de C1.3).</summary>
    public bool AdmiteOrigen(OrigenMovimiento origen) => NumeroAdmiteOrigen(Numero, origen);

    public static bool NumeroAdmiteOrigen(int numero, OrigenMovimiento origen) => numero != NumeroAjuste || origen == OrigenMovimiento.Manual;

    private static readonly string[] Meses =
        ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    public static string Nombre(int numero) => numero == NumeroAjuste ? "Ajustes de auditoría" : Meses[numero - 1];

    /// <summary>Solo desde <c>NoAbierto</c>. El 13 exige el 12 cerrado (R19: después del cierre ordinario).</summary>
    public PeriodoContableBitacora Abrir(PeriodoContable? periodo12, string? motivo, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoPeriodo.NoAbierto)
            throw new BusinessRuleException("CONTAB_PERIODO_NO_ABRIBLE", Estado == EstadoPeriodo.Abierto
                ? $"El periodo {Clave} ya está abierto."
                : $"El periodo {Clave} está cerrado: para volver a registrar en él hay que reabrirlo.");
        if (EsAjuste && periodo12?.Estado != EstadoPeriodo.Cerrado)
            throw new BusinessRuleException("CONTAB_PERIODO_13_REQUIERE_12_CERRADO",
                $"El periodo 13 de ajustes de {Anio} solo se abre después de cerrar diciembre.");
        if (motivo?.Length > MotivoMaximo)
            throw new BusinessRuleException("CONTAB_PERIODO_MOTIVO_INVALIDO", $"El motivo admite hasta {MotivoMaximo} caracteres.");
        AbiertoPor = usuarioNombre;
        AbiertoEn = ahora;
        return Transicion(AccionPeriodo.Abrir, EstadoPeriodo.Abierto, motivo, usuarioId, usuarioNombre, ahora);
    }

    /// <summary>
    /// Solo desde <c>Abierto</c> y con los anteriores del ejercicio cerrados (D4). Cerrar uno ya cerrado es un conflicto
    /// explícito (409), nunca un éxito silencioso (D5).
    /// </summary>
    public PeriodoContableBitacora Cerrar(IEnumerable<PeriodoContable> anteriores, string motivo, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado == EstadoPeriodo.Cerrado)
            throw new ConflictException("CONTAB_PERIODO_YA_CERRADO", $"El periodo {Clave} ya está cerrado.");
        if (Estado != EstadoPeriodo.Abierto)
            throw new BusinessRuleException("CONTAB_PERIODO_NO_ABIERTO", $"El periodo {Clave} no se ha abierto: no hay nada que cerrar.");
        var abierto = anteriores.Where(p => p.Numero < Numero && p.Estado != EstadoPeriodo.Cerrado).OrderBy(p => p.Numero).FirstOrDefault();
        if (abierto is not null)
            throw new BusinessRuleException("CONTAB_PERIODO_ANTERIOR_ABIERTO",
                $"Cierre primero el periodo {abierto.Clave} ({Nombre(abierto.Numero)}): los periodos se cierran en orden.");
        ValidarMotivo(motivo);
        CerradoPor = usuarioNombre;
        CerradoEn = ahora;
        return Transicion(AccionPeriodo.Cerrar, EstadoPeriodo.Cerrado, motivo, usuarioId, usuarioNombre, ahora);
    }

    /// <summary>Solo desde <c>Cerrado</c> y si el siguiente no está cerrado (D4). No reabre el inventario (D10/D18).</summary>
    public PeriodoContableBitacora Reabrir(PeriodoContable? siguiente, string motivo, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoPeriodo.Cerrado)
            throw new BusinessRuleException("CONTAB_PERIODO_NO_CERRADO", $"El periodo {Clave} no está cerrado: no hay nada que reabrir.");
        if (siguiente?.Estado == EstadoPeriodo.Cerrado)
            throw new BusinessRuleException("CONTAB_PERIODO_SIGUIENTE_CERRADO",
                $"Reabra primero el periodo {siguiente.Clave} ({Nombre(siguiente.Numero)}): está cerrado después de este.");
        ValidarMotivo(motivo);
        ReabiertoPor = usuarioNombre;
        ReabiertoEn = ahora;
        return Transicion(AccionPeriodo.Reabrir, EstadoPeriodo.Abierto, motivo, usuarioId, usuarioNombre, ahora);
    }

    private static void ValidarMotivo(string? motivo)
    {
        if (motivo is null || motivo.Trim().Length is < MotivoMinimo or > MotivoMaximo)
            throw new BusinessRuleException("CONTAB_PERIODO_MOTIVO_INVALIDO",
                $"Indique el motivo (entre {MotivoMinimo} y {MotivoMaximo} caracteres): queda en la bitácora del periodo.");
    }

    private PeriodoContableBitacora Transicion(AccionPeriodo accion, EstadoPeriodo nuevo, string? motivo, Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        var anterior = Estado;
        Estado = nuevo;
        return new PeriodoContableBitacora(Guid.CreateVersion7(), Id, accion, anterior, nuevo,
            string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(), usuarioId, usuarioNombre, ahora, Version + 1);
    }
}

/// <summary>
/// Bitácora del periodo (D6): una fila por transición, solo inserción. <see cref="VersionResultante"/> es la versión del periodo
/// tras la transición; el índice único (periodo, versión) impide registrar dos veces el mismo evento.
/// </summary>
// PLATFORM-TODO(<OutboxContabilidad>): publicar PeriodoContableCerradoEvent / PeriodoContableReabiertoEvent junto con esta fila
// cuando haya outbox en Contabilidad y suscriptores (C1.2/C1.5).
public sealed class PeriodoContableBitacora : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PeriodoId { get; private set; }
    public AccionPeriodo Accion { get; private set; }
    public EstadoPeriodo EstadoAnterior { get; private set; }
    public EstadoPeriodo EstadoNuevo { get; private set; }
    public string? Motivo { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string UsuarioNombre { get; private set; } = string.Empty;
    public DateTimeOffset OcurridoEn { get; private set; }
    public int VersionResultante { get; private set; }

    private PeriodoContableBitacora() { }

    internal PeriodoContableBitacora(
        Guid id, Guid periodoId, AccionPeriodo accion, EstadoPeriodo estadoAnterior, EstadoPeriodo estadoNuevo, string? motivo,
        Guid? usuarioId, string usuarioNombre, DateTimeOffset ocurridoEn, int versionResultante) : base(id)
    {
        PeriodoId = periodoId;
        Accion = accion;
        EstadoAnterior = estadoAnterior;
        EstadoNuevo = estadoNuevo;
        Motivo = motivo;
        UsuarioId = usuarioId;
        UsuarioNombre = usuarioNombre;
        OcurridoEn = ocurridoEn;
        VersionResultante = versionResultante;
    }
}
