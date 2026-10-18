using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>Estado de la plantilla recurrente (K10.4). Fijo por ABI: agregar al final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoPlantillaRecurrente : short { Borrador = 0, Activa = 1, Suspendida = 2, Terminada = 3 }

/// <summary>Partida fija de una plantilla recurrente (mismas dimensiones que una partida de póliza, en MXN).</summary>
public sealed class PartidaPlantilla
{
    public Guid CuentaId { get; set; }
    public string? Concepto { get; set; }
    public decimal Cargo { get; set; }
    public decimal Abono { get; set; }
    public Guid SucursalId { get; set; }
    public Guid? Dim1Id { get; set; }
    public Guid? Dim2Id { get; set; }
    public Guid? Dim3Id { get; set; }
    public string? Proyecto { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? ProveedorId { get; set; }
    public Guid? CuentaBancariaId { get; set; }
}

/// <summary>
/// Póliza recurrente (K10.4, R11 de Millet: «tener la información del importe y periodos a aplicar, para que la repetición se libere
/// en el sistema»; casilla de reversa automática). La prepara el Analista y la activa otra persona con permiso (R23); desde entonces
/// el generador crea la póliza de cada periodo en su día, sin capturarla de nuevo (CA10.6), y si la plantilla tiene reversa
/// automática, la reversa en negativo el día 1 del periodo siguiente (K10.4-b). Una plantilla activa no se edita: se suspende y se
/// crea otra.
/// </summary>
public sealed class PlantillaRecurrente : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int PeriodosMaximo = 120;
    public const int UltimoDiaDelMes = 0;

    private List<PartidaPlantilla> _partidas = [];

    public Guid EmpresaId { get; set; }
    public string Nombre { get; private set; } = string.Empty;
    public Guid TipoOperacionId { get; private set; }
    public string Concepto { get; private set; } = string.Empty;
    public int AnioInicio { get; private set; }
    public int MesInicio { get; private set; }
    public int NumeroPeriodos { get; private set; }
    /// <summary>Día del mes de la póliza (1–28); 0 = último día del mes.</summary>
    public int DiaDelMes { get; private set; }
    public bool ReversaAutomatica { get; private set; }
    /// <summary>true = cada póliza nace autorizada por quien activó la plantilla; false = nace en borrador para autorizarse.</summary>
    public bool NaceAutorizada { get; private set; }
    public EstadoPlantillaRecurrente Estado { get; private set; }
    public Guid? CreadaPorId { get; private set; }
    public string CreadaPor { get; private set; } = string.Empty;
    public Guid? ActivadaPorId { get; private set; }
    public string? ActivadaPor { get; private set; }
    public DateTimeOffset? ActivadaEn { get; private set; }

    public IReadOnlyList<PartidaPlantilla> Partidas => _partidas;

    public decimal TotalCargos => _partidas.Sum(x => x.Cargo);
    public decimal TotalAbonos => _partidas.Sum(x => x.Abono);

    private PlantillaRecurrente() { }

    public PlantillaRecurrente(Guid id, string nombre, TipoDocumentoContable tipo, string concepto, int anioInicio, int mesInicio,
        int numeroPeriodos, int diaDelMes, bool reversaAutomatica, bool naceAutorizada, IEnumerable<PartidaPlantilla> partidas,
        Guid? usuarioId, string usuarioNombre) : base(id)
    {
        CreadaPorId = usuarioId;
        CreadaPor = usuarioNombre;
        Estado = EstadoPlantillaRecurrente.Borrador;
        Aplicar(nombre, tipo, concepto, anioInicio, mesInicio, numeroPeriodos, diaDelMes, reversaAutomatica, naceAutorizada, partidas);
    }

    public void Editar(string nombre, TipoDocumentoContable tipo, string concepto, int anioInicio, int mesInicio, int numeroPeriodos,
        int diaDelMes, bool reversaAutomatica, bool naceAutorizada, IEnumerable<PartidaPlantilla> partidas, Guid? editorId)
    {
        if (Estado != EstadoPlantillaRecurrente.Borrador)
            throw new BusinessRuleException("CONTAB_RECURRENTE_NO_EDITABLE", "Solo se edita una plantilla en borrador; suspéndala y cree otra.");
        // Solo quien la preparó la edita: si otro la reescribiera, después podría activarla él mismo (R23).
        if (editorId is null || editorId != CreadaPorId)
            throw new BusinessRuleException("CONTAB_RECURRENTE_EDITA_SOLO_QUIEN_CREA",
                "Solo quien preparó la plantilla puede editarla. Si tiene un error, regrésela a quien la preparó.");
        Aplicar(nombre, tipo, concepto, anioInicio, mesInicio, numeroPeriodos, diaDelMes, reversaAutomatica, naceAutorizada, partidas);
    }

    private void Aplicar(string nombre, TipoDocumentoContable tipo, string concepto, int anioInicio, int mesInicio, int numeroPeriodos,
        int diaDelMes, bool reversaAutomatica, bool naceAutorizada, IEnumerable<PartidaPlantilla> partidas)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 120)
            throw new BusinessRuleException("CONTAB_RECURRENTE_NOMBRE_INVALIDO", "El nombre es requerido y admite hasta 120 caracteres.");
        if (string.IsNullOrWhiteSpace(concepto) || concepto.Trim().Length > Poliza.ConceptoMaximo)
            throw new BusinessRuleException("CONTAB_RECURRENTE_CONCEPTO_INVALIDO", $"El concepto es requerido y admite hasta {Poliza.ConceptoMaximo} caracteres.");
        if (!tipo.Activo)
            throw new BusinessRuleException("CONTAB_POLIZA_TIPO_INACTIVO", $"El tipo de operación {tipo.Clave} está inactivo.");
        if (mesInicio is < 1 or > 12 || anioInicio is < EjercicioContable.AnioMinimo or > EjercicioContable.AnioMaximo)
            throw new BusinessRuleException("CONTAB_RECURRENTE_INICIO_INVALIDO", "El periodo inicial es un mes de 1 a 12 de un año válido.");
        if (numeroPeriodos is < 1 or > PeriodosMaximo)
            throw new BusinessRuleException("CONTAB_RECURRENTE_PERIODOS_INVALIDOS", $"Los periodos a aplicar van de 1 a {PeriodosMaximo}.");
        if (diaDelMes is < UltimoDiaDelMes or > 28)
            throw new BusinessRuleException("CONTAB_RECURRENTE_DIA_INVALIDO", "El día del mes va de 1 a 28, o 0 para el último día.");
        var lista = partidas.ToList();
        if (lista.Count < 2)
            throw new BusinessRuleException("CONTAB_POLIZA_SIN_PARTIDAS", "La plantilla necesita al menos dos partidas.");
        foreach (var (p, i) in lista.Select((p, i) => (p, i + 1)))
        {
            if (p.CuentaId == Guid.Empty || p.SucursalId == Guid.Empty)
                throw new BusinessRuleException("CONTAB_PARTIDA_CUENTA_REQUERIDA", $"Partida {i}: elija la cuenta y la sucursal.");
            if ((p.Cargo != 0m) == (p.Abono != 0m) || p.Cargo < 0m || p.Abono < 0m)
                throw new BusinessRuleException("CONTAB_PARTIDA_UN_SOLO_LADO", $"Partida {i}: capture un cargo o un abono positivo (solo uno).");
            if (decimal.Round(p.Cargo, 2) != p.Cargo || decimal.Round(p.Abono, 2) != p.Abono)
                throw new BusinessRuleException("CONTAB_PARTIDA_IMPORTE_INVALIDO", $"Partida {i}: los importes admiten hasta dos decimales.");
        }
        var cargos = lista.Sum(x => x.Cargo);
        if (cargos != lista.Sum(x => x.Abono) || cargos == 0m)
            throw new BusinessRuleException("CONTAB_POLIZA_NO_CUADRA", "La plantilla no cuadra: los cargos deben ser iguales a los abonos.");
        Nombre = nombre.Trim();
        TipoOperacionId = tipo.Id;
        Concepto = concepto.Trim();
        AnioInicio = anioInicio;
        MesInicio = mesInicio;
        NumeroPeriodos = numeroPeriodos;
        DiaDelMes = diaDelMes;
        ReversaAutomatica = reversaAutomatica;
        NaceAutorizada = naceAutorizada;
        _partidas = lista;
    }

    /// <summary>La activa otra persona con permiso, no quien la preparó (R23).</summary>
    public void Activar(Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        if (Estado != EstadoPlantillaRecurrente.Borrador)
            throw new BusinessRuleException("CONTAB_RECURRENTE_NO_ACTIVABLE", "Solo se activa una plantilla en borrador.");
        if (usuarioId is null || usuarioId == CreadaPorId)
            throw new BusinessRuleException("CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA",
                "No puede activar una plantilla que usted preparó: la activa otro usuario con permiso (prepara el Analista, aprueba el Supervisor).");
        Estado = EstadoPlantillaRecurrente.Activa;
        ActivadaPorId = usuarioId;
        ActivadaPor = usuarioNombre;
        ActivadaEn = ahora;
    }

    public void Suspender()
    {
        if (Estado != EstadoPlantillaRecurrente.Activa)
            throw new BusinessRuleException("CONTAB_RECURRENTE_NO_SUSPENDIBLE", "Solo se suspende una plantilla activa.");
        Estado = EstadoPlantillaRecurrente.Suspendida;
    }

    public void Reanudar()
    {
        if (Estado != EstadoPlantillaRecurrente.Suspendida)
            throw new BusinessRuleException("CONTAB_RECURRENTE_NO_REANUDABLE", "Solo se reanuda una plantilla suspendida.");
        Estado = EstadoPlantillaRecurrente.Activa;
    }

    public void Terminar() => Estado = EstadoPlantillaRecurrente.Terminada;

    /// <summary>Periodo (año, mes) de la aplicación k (0-based).</summary>
    public (int Anio, int Mes) Periodo(int k)
    {
        var m = MesInicio - 1 + k;
        return (AnioInicio + m / 12, m % 12 + 1);
    }

    /// <summary>Fecha de la póliza del periodo: el día configurado o el último del mes.</summary>
    public DateOnly Fecha(int anio, int mes) =>
        DiaDelMes == UltimoDiaDelMes ? new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes)) : new DateOnly(anio, mes, DiaDelMes);

    /// <summary>Fecha de la reversa automática: día 1 del periodo siguiente (C-10, K10.4-b).</summary>
    public static DateOnly FechaReversa(int anio, int mes) => new DateOnly(anio, mes, 1).AddMonths(1);

    public static string Llave(Guid plantillaId, int anio, int mes) => $"recurrente:{plantillaId}:{anio}-{mes:00}";
}
