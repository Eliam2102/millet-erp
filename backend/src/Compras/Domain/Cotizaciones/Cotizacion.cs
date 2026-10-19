using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Cotizaciones;

/// <summary>Datos de una partida cotizada (entrada del dominio).</summary>
public sealed record LineaCotizacionDatos(
    Guid LineaRequisicionId,
    string UnidadRequisicion,
    string UnidadCotizada,
    decimal FactorAUnidadRq,
    decimal PrecioUnitario,
    int? DiasEntrega);

/// <summary>
/// Elección previa de una cotización que se liberó (OC cancelada o sin líneas). Historial inmutable:
/// conserva quién, cuándo, por qué se eligió y por qué se liberó.
/// </summary>
public sealed record EleccionCotizacionAnterior(
    Guid Id, Guid OrdenCompraId, string Motivo, Guid ElegidaPor, DateTimeOffset ElegidaEn,
    string MotivoLiberacion, Guid LiberadaPor, DateTimeOffset LiberadaEn);

/// <summary>
/// Cotización de un proveedor para las partidas de una requisición (F1-COM-02).
/// Elegirla genera la OC; a partir de entonces ya no se edita ni se elimina.
/// </summary>
public sealed class Cotizacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    private static readonly Regex MonedaIso = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public Guid EmpresaId { get; set; }

    public Guid RequisicionId { get; private set; }
    public Guid SucursalId { get; private set; }
    public Guid ProveedorId { get; private set; }
    public string? FolioProveedor { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public DateOnly FechaCotizacion { get; private set; }
    public DateOnly VigenteHasta { get; private set; }
    public string? CondicionesCredito { get; private set; }
    public Guid RegistradaPor { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }
    public Guid? ElegidaPor { get; private set; }
    public DateTimeOffset? ElegidaEn { get; private set; }
    public string? MotivoEleccion { get; private set; }
    public Guid? OrdenCompraId { get; private set; }

    private readonly List<LineaCotizacion> _lineas = [];
    public IReadOnlyCollection<LineaCotizacion> Lineas => _lineas.AsReadOnly();

    private readonly List<EleccionCotizacionAnterior> _eleccionesAnteriores = [];
    public IReadOnlyCollection<EleccionCotizacionAnterior> EleccionesAnteriores => _eleccionesAnteriores.AsReadOnly();

    public bool Elegida => OrdenCompraId is not null;

    public bool EsVigente(DateOnly hoy) => VigenteHasta >= hoy;

    /// <summary>Constructor para EF Core.</summary>
    private Cotizacion() { }

    private Cotizacion(Guid id) : base(id) { }

    public static Cotizacion Registrar(
        Guid id, Guid empresaId, Guid requisicionId, Guid sucursalId, Guid proveedorId,
        string? folioProveedor, string moneda, DateOnly fechaCotizacion, DateOnly vigenteHasta,
        string? condicionesCredito, IReadOnlyList<LineaCotizacionDatos> lineas,
        Guid registradaPor, DateTimeOffset ahora, DateOnly hoy)
    {
        var c = new Cotizacion(id)
        {
            EmpresaId = empresaId,
            RequisicionId = requisicionId,
            SucursalId = sucursalId,
            RegistradaPor = registradaPor,
            RegistradaEn = ahora,
        };
        c.AsignarDatos(proveedorId, folioProveedor, moneda, fechaCotizacion, vigenteHasta, condicionesCredito, lineas, hoy);
        return c;
    }

    public void Editar(
        Guid proveedorId, string? folioProveedor, string moneda, DateOnly fechaCotizacion,
        DateOnly vigenteHasta, string? condicionesCredito, IReadOnlyList<LineaCotizacionDatos> lineas, DateOnly hoy)
    {
        if (Elegida)
        {
            throw new BusinessRuleException("COT_COMPARATIVO_CERRADO",
                "La cotización ya se eligió para una orden de compra; no se puede modificar.");
        }

        AsignarDatos(proveedorId, folioProveedor, moneda, fechaCotizacion, vigenteHasta, condicionesCredito, lineas, hoy);
    }

    private void AsignarDatos(
        Guid proveedorId, string? folioProveedor, string moneda, DateOnly fechaCotizacion,
        DateOnly vigenteHasta, string? condicionesCredito, IReadOnlyList<LineaCotizacionDatos> lineas, DateOnly hoy)
    {
        if (proveedorId == Guid.Empty)
        {
            throw new BusinessRuleException("COT_PROVEEDOR_REQUERIDO", "Selecciona el proveedor de la cotización.");
        }

        var monedaNorm = moneda?.Trim().ToUpperInvariant();
        if (monedaNorm is null || !MonedaIso.IsMatch(monedaNorm))
        {
            throw new BusinessRuleException("COT_MONEDA_INVALIDA", "La moneda debe ser un código ISO de 3 letras (MXN, USD…).");
        }

        if (fechaCotizacion > hoy)
        {
            throw new BusinessRuleException("COT_FECHA_FUTURA", "La fecha de la cotización no puede ser posterior a hoy.");
        }

        if (vigenteHasta < fechaCotizacion)
        {
            throw new BusinessRuleException("COT_VIGENCIA_INVALIDA", "La vigencia no puede ser anterior a la fecha de la cotización.");
        }

        var folio = folioProveedor?.Trim();
        if (folio is { Length: > 40 })
        {
            throw new BusinessRuleException("COT_FOLIO_INVALIDO", "El folio del proveedor admite máximo 40 caracteres.");
        }

        var credito = condicionesCredito?.Trim();
        if (credito is { Length: > 100 })
        {
            throw new BusinessRuleException("COT_CREDITO_INVALIDO", "Las condiciones de crédito admiten máximo 100 caracteres.");
        }

        if (lineas.Count == 0)
        {
            throw new BusinessRuleException("COT_SIN_PARTIDAS", "La cotización debe incluir al menos una partida.");
        }

        if (lineas.Select(l => l.LineaRequisicionId).Distinct().Count() != lineas.Count)
        {
            throw new BusinessRuleException("COT_PARTIDA_DUPLICADA", "Una partida aparece dos veces en la cotización.");
        }

        var nuevas = lineas.Select(d => new LineaCotizacion(Guid.CreateVersion7(), Id, d)).ToList();

        ProveedorId = proveedorId;
        FolioProveedor = string.IsNullOrEmpty(folio) ? null : folio;
        Moneda = monedaNorm;
        FechaCotizacion = fechaCotizacion;
        VigenteHasta = vigenteHasta;
        CondicionesCredito = string.IsNullOrEmpty(credito) ? null : credito;
        _lineas.Clear();
        _lineas.AddRange(nuevas);
    }

    /// <param name="hayOtraElegidaVigente">Otra cotización de la misma RQ ya está elegida con OC vigente.</param>
    public void Elegir(Guid ordenCompraId, string motivo, Guid usuarioId, DateTimeOffset ahora, DateOnly hoy,
        bool hayOtraElegidaVigente = false)
    {
        if (hayOtraElegidaVigente)
        {
            throw new BusinessRuleException("COT_YA_HAY_ELEGIDA",
                "Ya hay una cotización elegida con orden de compra vigente en esta requisición.");
        }

        if (Elegida)
        {
            throw new BusinessRuleException("COT_YA_ELEGIDA",
                $"Esta cotización ya se eligió para la orden de compra {OrdenCompraId}.");
        }

        if (!EsVigente(hoy))
        {
            throw new BusinessRuleException("COT_VENCIDA",
                $"La cotización venció el {VigenteHasta:dd/MM/yyyy}; no se puede elegir. Pide al proveedor una cotización vigente.");
        }

        var m = motivo?.Trim();
        if (string.IsNullOrEmpty(m) || m.Length is < 5 or > 500)
        {
            throw new BusinessRuleException("COT_MOTIVO_REQUERIDO", "Escribe el motivo de la elección (5 a 500 caracteres).");
        }

        if (ordenCompraId == Guid.Empty || usuarioId == Guid.Empty)
        {
            throw new BusinessRuleException("COT_ELECCION_INVALIDA", "Faltan la orden de compra o el usuario de la elección.");
        }

        OrdenCompraId = ordenCompraId;
        MotivoEleccion = m;
        ElegidaPor = usuarioId;
        ElegidaEn = ahora;
    }

    /// <summary>
    /// Libera la elección (la OC se canceló o se quedó sin líneas): la cotización vuelve a ser elegible y
    /// la elección queda en <see cref="EleccionesAnteriores"/> con su motivo.
    /// </summary>
    public void LiberarEleccion(Guid usuarioId, DateTimeOffset ahora, string motivo)
    {
        if (!Elegida)
        {
            return;
        }

        var m = string.IsNullOrWhiteSpace(motivo) ? "Orden de compra liberada." : motivo.Trim();
        _eleccionesAnteriores.Add(new EleccionCotizacionAnterior(
            Guid.CreateVersion7(), OrdenCompraId!.Value, MotivoEleccion ?? string.Empty,
            ElegidaPor ?? Guid.Empty, ElegidaEn ?? ahora,
            m.Length > 500 ? m[..500] : m, usuarioId, ahora));
        OrdenCompraId = null;
        MotivoEleccion = null;
        ElegidaPor = null;
        ElegidaEn = null;
    }

    public decimal PrecioPorUnidadRequisicion(Guid lineaRequisicionId)
    {
        var linea = _lineas.FirstOrDefault(l => l.LineaRequisicionId == lineaRequisicionId);
        return linea?.PrecioPorUnidadRequisicion
            ?? throw new BusinessRuleException("COT_PARTIDAS_INCOMPLETAS",
                "La cotización no cubre todas las partidas por comprar de la requisición.");
    }
}
