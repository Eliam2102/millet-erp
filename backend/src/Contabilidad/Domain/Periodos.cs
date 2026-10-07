using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoPeriodo : short { Abierto = 0, Cerrado = 1 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccionPeriodo : short { Creado = 0, Cerrado = 1, Reabierto = 2 }

/// <summary>
/// Periodo contable (F1-CON-03, ficha C1.1; Módulo 10 R18/R19). Un ejercicio tiene 13 periodos: 1 a 12 son los meses del
/// año natural y el 13 es solo para ajustes de auditoría después del cierre ordinario (sin fechas propias). Lo crea
/// <c>CrearEjercicioCommand</c> de una vez. Reabrir contabilidad no reabre el inventario: Almacén tiene su propio cierre (D18).
/// </summary>
public sealed class PeriodoContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int PeriodoAjustes = 13;
    public const int MaxMotivo = 500;

    public Guid EmpresaId { get; set; }
    public int Ejercicio { get; private set; }
    public int Numero { get; private set; }
    public EstadoPeriodo Estado { get; private set; } = EstadoPeriodo.Abierto;
    /// <summary>Null en el periodo 13 (ajustes de auditoría).</summary>
    public DateOnly? FechaInicio { get; private set; }
    public DateOnly? FechaFin { get; private set; }
    public string? CerradoPor { get; private set; }
    public DateTimeOffset? CerradoEn { get; private set; }
    public string? ReabiertoPor { get; private set; }
    public DateTimeOffset? ReabiertoEn { get; private set; }

    public bool EsPeriodoAjustes => Numero == PeriodoAjustes;
    public bool Abierto => Estado == EstadoPeriodo.Abierto;

    private PeriodoContable() { }

    public PeriodoContable(Guid id, int ejercicio, int numero) : base(id)
    {
        ValidarEjercicio(ejercicio);
        if (numero is < 1 or > PeriodoAjustes)
            throw new BusinessRuleException("CONTAB_PERIODO_NUMERO_INVALIDO", "El periodo debe estar entre 1 y 13.");
        Ejercicio = ejercicio;
        Numero = numero;
        if (numero < PeriodoAjustes)
        {
            FechaInicio = new DateOnly(ejercicio, numero, 1);
            FechaFin = FechaInicio.Value.AddMonths(1).AddDays(-1);
        }
    }

    public static void ValidarEjercicio(int ejercicio)
    {
        if (ejercicio is < 2000 or > 2100)
            throw new BusinessRuleException("CONTAB_EJERCICIO_INVALIDO", "El ejercicio debe ser un año entre 2000 y 2100.");
    }

    public void Cerrar(string usuario, DateTimeOffset ahora)
    {
        if (!Abierto)
            throw new BusinessRuleException("CONTAB_PERIODO_YA_CERRADO", $"El periodo {Etiqueta} ya está cerrado.");
        Estado = EstadoPeriodo.Cerrado;
        CerradoPor = usuario;
        CerradoEn = ahora;
    }

    /// <summary>R18: un periodo cerrado sí se reabre, con motivo; lo autoriza y lo hace el Contador General (permiso).</summary>
    public void Reabrir(string usuario, string? motivo, DateTimeOffset ahora)
    {
        if (Abierto)
            throw new BusinessRuleException("CONTAB_PERIODO_YA_ABIERTO", $"El periodo {Etiqueta} ya está abierto.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CONTAB_PERIODO_MOTIVO_REQUERIDO", "Para reabrir un periodo hay que indicar el motivo.");
        Estado = EstadoPeriodo.Abierto;
        ReabiertoPor = usuario;
        ReabiertoEn = ahora;
    }

    public string Etiqueta => EsPeriodoAjustes ? $"13 (ajustes) de {Ejercicio}" : $"{Numero:00}/{Ejercicio}";
}

/// <summary>Historial de un periodo: una fila por acción (crear, cerrar, reabrir). Solo se agrega; nunca se edita ni se borra.</summary>
public sealed class PeriodoContableEvento : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PeriodoId { get; private set; }
    public AccionPeriodo Accion { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset Fecha { get; private set; }
    public string? Motivo { get; private set; }

    private PeriodoContableEvento() { }

    public PeriodoContableEvento(Guid id, Guid periodoId, AccionPeriodo accion, string usuario, DateTimeOffset fecha, string? motivo)
        : base(id)
    {
        if (motivo?.Length > PeriodoContable.MaxMotivo)
            throw new BusinessRuleException("CONTAB_PERIODO_MOTIVO_INVALIDO", $"El motivo admite hasta {PeriodoContable.MaxMotivo} caracteres.");
        PeriodoId = periodoId;
        Accion = accion;
        Usuario = usuario;
        Fecha = fecha;
        Motivo = motivo;
    }
}
