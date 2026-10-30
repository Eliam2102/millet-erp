using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>Lado de una línea de plantilla. Fijo por ABI: agregar al final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LadoPartida : short { Cargo = 0, Abono = 1 }

/// <summary>
/// Auxiliar de una línea de plantilla (C1.4). Define también el origen con que se valida la cuenta: Proveedor → AuxiliarCxP,
/// Cliente → AuxiliarCxC, Ninguno → Automatica (las cuentas colectivas lo rechazan, R4).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuxiliarPlantilla : short { Ninguno = 0, Proveedor = 1, Cliente = 2 }

/// <summary>Causa de un rechazo del motor (plano C1 §2, ficha C1.4 §2). Fijo por ABI: agregar al final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CausaRechazoContable : short { SinPlantilla = 0, RolSinCuenta = 1, PeriodoCerrado = 2, CuentaNoValida = 3, DatoFaltante = 4, NoCuadra = 5 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoRechazoContable : short { Pendiente = 0, Reprocesado = 1, Descartado = 2 }

/// <summary>
/// Rol de cuenta (plano C1 §2): lo único que mantiene Laura. Las plantillas apuntan a roles, no a cuentas; cambiar la cuenta de un
/// rol solo afecta a las pólizas nuevas (las pasadas guardan su cuenta). <see cref="CuentaId"/> nulo = rol aún sin mapear: el motor
/// rechaza con <see cref="CausaRechazoContable.RolSinCuenta"/>. El filtro opcional por sucursal o moneda permite una cuenta distinta
/// para un caso; el motor prefiere el rol más específico.
/// </summary>
public sealed class RolCuenta : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int CodigoMaximo = 60;

    public Guid EmpresaId { get; set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public Guid? CuentaId { get; private set; }
    public Guid? SucursalId { get; private set; }
    public string? Moneda { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }

    private RolCuenta() { }

    public static RolCuenta Crear(Guid id, string codigo, string descripcion, Guid? cuentaId, Guid? sucursalId, string? moneda, DateOnly vigenteDesde)
    {
        var c = codigo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (c.Length is 0 or > CodigoMaximo || c.Any(ch => !(char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '_')))
            throw new BusinessRuleException("CONTAB_ROL_CODIGO_INVALIDO", "El código del rol va en mayúsculas, dígitos y guion bajo (hasta 60).");
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new BusinessRuleException("CONTAB_ROL_DESCRIPCION", "Describa el rol.");
        return new RolCuenta
        {
            Id = id, Codigo = c, Descripcion = descripcion.Trim(), CuentaId = cuentaId, SucursalId = sucursalId,
            Moneda = string.IsNullOrWhiteSpace(moneda) ? null : moneda.Trim().ToUpperInvariant(), VigenteDesde = vigenteDesde,
        };
    }

    public bool VigenteEn(DateOnly fecha) => VigenteDesde <= fecha && (VigenteHasta is null || fecha <= VigenteHasta);

    /// <summary>Cambia la cuenta del rol y devuelve el renglón de bitácora (C1.4-c: quién, cuándo, cuenta anterior y nueva).</summary>
    public RolCuentaCambio CambiarCuenta(Guid cuentaId, string usuario, DateTimeOffset ahora, string? motivo)
    {
        if (CuentaId == cuentaId)
            throw new BusinessRuleException("CONTAB_ROL_MISMA_CUENTA", $"El rol {Codigo} ya usa esa cuenta.");
        var cambio = new RolCuentaCambio(Guid.CreateVersion7(), Id, CuentaId, cuentaId, usuario, ahora, motivo) { EmpresaId = EmpresaId };
        CuentaId = cuentaId;
        return cambio;
    }
}

/// <summary>Bitácora de cambios de cuenta de un rol (C1.4-c).</summary>
public sealed class RolCuentaCambio : IPerteneceAEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid RolCuentaId { get; private set; }
    public Guid? CuentaAnteriorId { get; private set; }
    public Guid CuentaNuevaId { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset CambiadoEn { get; private set; }
    public string? Motivo { get; private set; }

    private RolCuentaCambio() { }

    public RolCuentaCambio(Guid id, Guid rolCuentaId, Guid? anterior, Guid nueva, string usuario, DateTimeOffset en, string? motivo)
    {
        Id = id; RolCuentaId = rolCuentaId; CuentaAnteriorId = anterior; CuentaNuevaId = nueva; Usuario = usuario; CambiadoEn = en;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
    }
}

/// <summary>
/// Línea de plantilla (ficha C1.4 §2; E12 pendiente: campos con nombre del evento, sin lenguaje de expresiones).
/// <see cref="Campo"/> es el importe del evento normalizado (p. ej. <c>Subtotal</c>, <c>Iva</c>, <c>RetencionesTotal</c>,
/// <c>TotalMenosRetenciones</c>). <see cref="PorLinea"/>: una partida por cada línea del evento que cumpla
/// <see cref="SoloServicios"/> (E6: servicios a gasto; el resto, al puente), con el importe de la línea.
/// </summary>
public sealed class LineaPlantillaContable
{
    public int Orden { get; set; }
    public string Rol { get; set; } = string.Empty;
    public LadoPartida Lado { get; set; }
    public string Campo { get; set; } = string.Empty;
    public AuxiliarPlantilla Auxiliar { get; set; }
    public bool PorLinea { get; set; }
    /// <summary>Con <see cref="PorLinea"/>: null = todas, true = solo líneas de servicio, false = solo líneas que no son servicio.</summary>
    public bool? SoloServicios { get; set; }
    /// <summary>Lleva el centro de costo del evento o de la línea (dimensión Dim3).</summary>
    public bool ConCentroCosto { get; set; }
    public string? Concepto { get; set; }
    /// <summary>
    /// C1.5: con <see cref="PorLinea"/>, usa el rol <c>{Rol}_{clasificador de la línea}</c> si existe con cuenta (p. ej. VENTAS_PT por
    /// tipo de producto); si no, el rol base. Contabilidad agrega el rol específico cuando lo necesita, sin cambiar la plantilla.
    /// </summary>
    public bool RolPorClasificador { get; set; }
}

/// <summary>
/// Plantilla contable por tipo de evento (plano C1 §2). Se siembra por migración y su pantalla es de consulta; una versión nueva no
/// toca pólizas pasadas. El motor toma la vigente a la fecha contable del evento (mayor <see cref="NumeroVersion"/> con
/// <see cref="VigenteDesde"/> ≤ fecha).
/// </summary>
public sealed class PlantillaContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    private List<LineaPlantillaContable> _lineas = [];

    public Guid EmpresaId { get; set; }
    public string TipoEvento { get; private set; } = string.Empty;
    public int NumeroVersion { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    /// <summary>Tipo de operación de dos letras (K10.9), p. ej. TT.</summary>
    public string TipoOperacionClave { get; private set; } = string.Empty;
    public string Concepto { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public IReadOnlyList<LineaPlantillaContable> Lineas => _lineas;

    private PlantillaContable() { }

    public static PlantillaContable Crear(Guid id, string tipoEvento, int version, DateOnly vigenteDesde, string tipoClave, string concepto,
        string descripcion, IEnumerable<LineaPlantillaContable> lineas) => new()
    {
        Id = id, TipoEvento = tipoEvento, NumeroVersion = version, VigenteDesde = vigenteDesde, TipoOperacionClave = tipoClave,
        Concepto = concepto, Descripcion = descripcion, _lineas = [.. lineas.OrderBy(l => l.Orden)],
    };
}

/// <summary>
/// Evento que el motor no pudo convertir en póliza (plano C1 §2; ficha C1.4 §2). Nada se pierde: guarda el payload y la causa;
/// «Reprocesar» corre la plantilla vigente con el mismo payload y la misma llave; «Descartar» exige motivo y permiso. Un rechazo por
/// evento (tipo + id): reintentos fallidos suman <see cref="Intentos"/>.
/// </summary>
public sealed class RechazoContable : IPerteneceAEmpresa
{
    public const int MotivoMinimo = 10;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public string TipoEvento { get; private set; } = string.Empty;
    public Guid EventoId { get; private set; }
    public string Payload { get; private set; } = "{}";
    public CausaRechazoContable Causa { get; private set; }
    /// <summary>Lo que falta o falló: el rol sin cuenta, el campo vacío, la partida inválida.</summary>
    public string? Detalle { get; private set; }
    public string Mensaje { get; private set; } = string.Empty;
    public int Intentos { get; private set; }
    public EstadoRechazoContable Estado { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public DateTimeOffset UltimoIntentoEn { get; private set; }
    public Guid? PolizaId { get; private set; }
    public string? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoDescarte { get; private set; }
    public int Version { get; private set; } = 1;

    private RechazoContable() { }

    public static RechazoContable Crear(Guid empresaId, string tipoEvento, Guid eventoId, string payload, CausaRechazoContable causa,
        string? detalle, string mensaje, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), EmpresaId = empresaId, TipoEvento = tipoEvento, EventoId = eventoId, Payload = payload, Causa = causa,
        Detalle = detalle, Mensaje = Recortar(mensaje), Intentos = 1, Estado = EstadoRechazoContable.Pendiente, CreadoEn = ahora,
        UltimoIntentoEn = ahora,
    };

    /// <summary>Un reintento que vuelve a fallar: actualiza la causa (puede ser otra) y suma el intento.</summary>
    public void FallaDeNuevo(CausaRechazoContable causa, string? detalle, string mensaje, DateTimeOffset ahora)
    {
        ExigirPendiente();
        Causa = causa; Detalle = detalle; Mensaje = Recortar(mensaje); Intentos++; UltimoIntentoEn = ahora; Version++;
    }

    public void MarcarReprocesado(Guid polizaId, string usuario, DateTimeOffset ahora)
    {
        ExigirPendiente();
        Estado = EstadoRechazoContable.Reprocesado; PolizaId = polizaId; ResueltoPor = usuario; ResueltoEn = ahora; Intentos++;
        UltimoIntentoEn = ahora; Version++;
    }

    public void Descartar(string motivo, string usuario, DateTimeOffset ahora)
    {
        ExigirPendiente();
        if (motivo is null || motivo.Trim().Length < MotivoMinimo)
            throw new BusinessRuleException("CONTAB_RECHAZO_MOTIVO", $"Indique por qué se descarta (al menos {MotivoMinimo} caracteres).");
        Estado = EstadoRechazoContable.Descartado; MotivoDescarte = motivo.Trim(); ResueltoPor = usuario; ResueltoEn = ahora; Version++;
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoRechazoContable.Pendiente)
            throw new ConflictException("CONTAB_RECHAZO_RESUELTO", "El rechazo ya se resolvió (reprocesado o descartado).");
    }

    private static string Recortar(string m) => m.Length <= 1000 ? m : m[..1000];
}
