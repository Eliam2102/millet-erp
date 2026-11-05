using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Mermas;

/// <summary>
/// Baja de producción en proceso informada por A+W (A4.4). Tabla <c>almacen.mermas_proceso</c>, dos tipos:
/// <see cref="DesperdicioMesa"/> (ajuste diario por mesa de corte, entra <see cref="Aplicada"/>) y <see cref="Merma"/>
/// (por etapa y motivo, entra <see cref="Pendiente"/> hasta que alguien con facultad la autoriza o la rechaza).
/// No mueve existencia: la hoja completa ya salió al consumirla (A4.3). La cantidad sale de producción en proceso y su
/// valor (m² × costo promedio de la hoja) se absorbe en el costo del PT del periodo (P-A4.1); la póliza la arma C1.5
/// con el evento <c>almacen.merma_proceso.aplicada.v1</c>.
/// </summary>
public sealed class MermaProceso : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string DesperdicioMesa = "DESPERDICIO_MESA";
    public const string Merma = "MERMA";
    public const string Pendiente = "PENDIENTE";
    public const string Aplicada = "APLICADA";
    public const string Rechazada = "RECHAZADA";
    public static readonly string[] Etapas = ["corte", "templado", "laminado", "insulado", "manejo"];

    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public DateOnly Fecha { get; private set; }
    /// <summary>Mesa de corte (solo desperdicio de mesa), p. ej. HEGLA1.</summary>
    public string? Mesa { get; private set; }
    public string Etapa { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;
    public Guid ArticuloId { get; private set; }
    public decimal? Kg { get; private set; }
    public decimal M2 { get; private set; }
    public decimal CostoUnitarioM2Mxn { get; private set; }
    public decimal ValorMxn { get; private set; }
    public string Estado { get; private set; } = string.Empty;
    public string? MensajeId { get; private set; }
    public Guid? AutorizadoPor { get; private set; }
    public DateTimeOffset? AutorizadoAt { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private MermaProceso() { }

    private MermaProceso(Guid empresaId, Guid sucursalId, string tipo, DateOnly fecha, string? mesa, string etapa, string motivo,
        Guid articuloId, decimal? kg, decimal m2, decimal costoM2, string? mensajeId) : base(Guid.CreateVersion7())
    {
        if (sucursalId == Guid.Empty || articuloId == Guid.Empty)
            throw new BusinessRuleException("MERMA_DATOS_INVALIDOS", "La sucursal y el artículo son obligatorios.");
        if (m2 <= 0 || kg is <= 0)
            throw new BusinessRuleException("MERMA_CANTIDAD_INVALIDA", "Los m² (y los kg, si vienen) deben ser mayores que cero.");
        if (costoM2 < 0)
            throw new BusinessRuleException("MERMA_COSTO_INVALIDO", "El costo por m² no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 200)
            throw new BusinessRuleException("MERMA_MOTIVO_INVALIDO", "El motivo es obligatorio (hasta 200 caracteres).");
        EmpresaId = empresaId; SucursalId = sucursalId; Tipo = tipo; Fecha = fecha; Mesa = mesa; Etapa = etapa; Motivo = motivo.Trim();
        ArticuloId = articuloId; Kg = kg; M2 = m2; CostoUnitarioM2Mxn = costoM2; ValorMxn = Math.Round(m2 * costoM2, 2); MensajeId = mensajeId;
    }

    /// <summary>Desperdicio diario de una mesa: informado por A+W, se aplica de una vez (etapa corte).</summary>
    public static MermaProceso DelaMesa(Guid empresaId, Guid sucursalId, DateOnly fecha, string mesa, Guid articuloId, decimal m2,
        decimal? kg, decimal costoM2, string? mensajeId)
    {
        if (string.IsNullOrWhiteSpace(mesa) || mesa.Length > 30)
            throw new BusinessRuleException("MERMA_MESA_INVALIDA", "La mesa de corte es obligatoria (hasta 30 caracteres).");
        return new MermaProceso(empresaId, sucursalId, DesperdicioMesa, fecha, mesa.Trim().ToUpperInvariant(), "corte",
            "Desperdicio de la mesa de corte", articuloId, kg, m2, costoM2, mensajeId) { Estado = Aplicada };
    }

    /// <summary>Merma por etapa y motivo: nace pendiente, sin efecto hasta que se autorice.</summary>
    public static MermaProceso PorEtapa(Guid empresaId, Guid sucursalId, DateOnly fecha, string etapa, string motivo, Guid articuloId,
        decimal kg, decimal m2, decimal costoM2, string? mensajeId)
    {
        etapa = (etapa ?? string.Empty).Trim().ToLowerInvariant();
        if (!Etapas.Contains(etapa))
            throw new BusinessRuleException("MERMA_ETAPA_INVALIDA", $"La etapa debe ser una de: {string.Join(", ", Etapas)}.");
        return new MermaProceso(empresaId, sucursalId, Merma, fecha, null, etapa, motivo, articuloId, kg, m2, costoM2, mensajeId)
        { Estado = Pendiente };
    }

    public void Autorizar(Guid usuarioId)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("MERMA_NO_PENDIENTE", $"Solo se autoriza una merma pendiente (estado actual: {Estado}).");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("MERMA_SIN_AUTORIZADOR", "La autorización requiere usuario.");
        Estado = Aplicada; AutorizadoPor = usuarioId; AutorizadoAt = DateTimeOffset.UtcNow;
    }

    public void Rechazar(Guid usuarioId, string motivo)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("MERMA_NO_PENDIENTE", $"Solo se rechaza una merma pendiente (estado actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("MERMA_RECHAZO_SIN_MOTIVO", "El motivo del rechazo es obligatorio (hasta 500 caracteres).");
        Estado = Rechazada; AutorizadoPor = usuarioId; AutorizadoAt = DateTimeOffset.UtcNow; MotivoRechazo = motivo.Trim();
    }
}
