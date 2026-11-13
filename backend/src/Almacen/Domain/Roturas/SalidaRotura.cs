using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Roturas;

/// <summary>
/// Salida por rotura de material en el almacén (ALM-03). Tabla <c>almacen.salidas_rotura</c>. Dos pasos: el almacenista la
/// captura (<see cref="Pendiente"/>, sin efecto en el saldo) y un Jefe o Gerente de almacén distinto la autoriza
/// (<see cref="Autorizada"/>: se crea el movimiento de rotura a costo promedio y baja el saldo) o la rechaza.
/// </summary>
public sealed class SalidaRotura : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string Pendiente = "PENDIENTE";
    public const string Autorizada = "AUTORIZADA";
    public const string Rechazada = "RECHAZADA";
    public const string MotivoOtro = "otro";

    /// <summary>Catálogo DEMO de motivos (código → texto); el catálogo real lo confirma Millet (Fernando Alejos).</summary>
    public static readonly IReadOnlyDictionary<string, string> Motivos = new Dictionary<string, string>
    {
        ["caida_maniobra"] = "Caída en maniobra",
        ["rotura_rack"] = "Rotura en rack",
        ["rotura_carga"] = "Rotura al cargar",
        [MotivoOtro] = "Otro",
    };

    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public Guid SubAlmacenId { get; private set; }
    public Guid UbicacionId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Cantidad { get; private set; }
    public string UnidadMedida { get; private set; } = string.Empty;
    public DateOnly Fecha { get; private set; }
    public string MotivoCodigo { get; private set; } = string.Empty;
    public string? Comentario { get; private set; }
    public Guid CapturadoPor { get; private set; }
    public string Estado { get; private set; } = string.Empty;
    public Guid? DecididoPor { get; private set; }
    public DateTimeOffset? DecididoAt { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public Guid? MovimientoId { get; private set; }
    public string? FolioMovimiento { get; private set; }
    public decimal? CostoUnitarioMxn { get; private set; }
    public decimal? ValorMxn { get; private set; }

    /// <summary>86e3zf04y: clave del área (CeCo, Dim2) capturada; null si no se capturó.</summary>
    public string? AreaClave { get; private set; }
    /// <summary>86e3zf04y: clave del equipo (Dim3) capturada; null si no se capturó.</summary>
    public string? EquipoClave { get; private set; }
    /// <summary>86e3zf04y: centro que viaja a la póliza de merma (el equipo si se capturó; si no, el área).</summary>
    public Guid? CentroCostoId { get; private set; }
    /// <summary>M-223: foto o documento que respalda la rotura (referencia del blob de Almacén, mismo que la recepción).</summary>
    public string EvidenciaBlobRef { get; private set; } = string.Empty;

    private SalidaRotura() { }

    public SalidaRotura(Guid empresaId, Guid sucursalId, Guid subAlmacenId, Guid ubicacionId, Guid articuloId, decimal cantidad,
        string unidadMedida, DateOnly fecha, string motivoCodigo, string? comentario, Guid capturadoPor, string? evidenciaBlobRef) : base(Guid.CreateVersion7())
    {
        if (cantidad <= 0)
            throw new BusinessRuleException("ROTURA_CANTIDAD_INVALIDA", "La cantidad rota debe ser mayor que cero.");
        motivoCodigo = (motivoCodigo ?? string.Empty).Trim();
        if (!Motivos.ContainsKey(motivoCodigo))
            throw new BusinessRuleException("ROTURA_MOTIVO_INVALIDO", "Elige un motivo del catálogo.");
        comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (motivoCodigo == MotivoOtro && comentario is null)
            throw new BusinessRuleException("ROTURA_COMENTARIO_REQUERIDO", "Con el motivo «Otro» describe qué pasó.");
        if (comentario?.Length > 500)
            throw new BusinessRuleException("ROTURA_COMENTARIO_LARGO", "El comentario no puede exceder 500 caracteres.");
        if (capturadoPor == Guid.Empty)
            throw new BusinessRuleException("ROTURA_SIN_USUARIO", "La captura requiere usuario.");
        if (string.IsNullOrWhiteSpace(evidenciaBlobRef))
            throw new BusinessRuleException("ROTURA_SIN_EVIDENCIA", "Adjunta una foto o documento de la rotura.");
        if (evidenciaBlobRef.Length > 500)
            throw new BusinessRuleException("ROTURA_EVIDENCIA_INVALIDA", "La referencia de la evidencia no es válida.");
        EvidenciaBlobRef = evidenciaBlobRef.Trim();
        EmpresaId = empresaId; SucursalId = sucursalId; SubAlmacenId = subAlmacenId; UbicacionId = ubicacionId; ArticuloId = articuloId;
        Cantidad = cantidad; UnidadMedida = unidadMedida; Fecha = fecha; MotivoCodigo = motivoCodigo; Comentario = comentario;
        CapturadoPor = capturadoPor; Estado = Pendiente;
    }

    /// <summary>
    /// 86e3zf04y: área y equipo de la rotura, ya resueltos en la sucursal por Contabilidad (regla C-03, mismo puerto que H1).
    /// Solo mientras está pendiente: al autorizar, el centro ya viajó en el evento de la póliza.
    /// </summary>
    public void AsignarAreaEquipo(string? areaClave, string? equipoClave, Guid? centroCostoId)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("ROTURA_NO_PENDIENTE", $"Solo se cambia el área o el equipo de una rotura pendiente (estado actual: {Estado}).");
        AreaClave = string.IsNullOrWhiteSpace(areaClave) ? null : areaClave.Trim();
        EquipoClave = string.IsNullOrWhiteSpace(equipoClave) ? null : equipoClave.Trim();
        CentroCostoId = centroCostoId;
    }

    /// <summary>Separación de funciones: quien capturó no autoriza. Solo se autoriza una rotura pendiente.</summary>
    public void VerificarAutorizacion(Guid usuarioId)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("ROTURA_NO_PENDIENTE", $"Solo se decide una rotura pendiente (estado actual: {Estado}).");
        if (usuarioId == Guid.Empty || usuarioId == CapturadoPor)
            throw new BusinessRuleException("ROTURA_MISMO_USUARIO", "La rotura debe autorizarla un usuario distinto de quien la capturó.");
    }

    public void Autorizar(Guid usuarioId, Guid movimientoId, string folio, decimal costoUnitarioMxn)
    {
        VerificarAutorizacion(usuarioId);
        Estado = Autorizada; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow;
        MovimientoId = movimientoId; FolioMovimiento = folio; CostoUnitarioMxn = costoUnitarioMxn;
        ValorMxn = Math.Round(Cantidad * costoUnitarioMxn, 2);
    }

    public void Rechazar(Guid usuarioId, string motivo)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("ROTURA_NO_PENDIENTE", $"Solo se decide una rotura pendiente (estado actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("ROTURA_RECHAZO_SIN_MOTIVO", "El motivo del rechazo es obligatorio (hasta 500 caracteres).");
        Estado = Rechazada; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow; MotivoRechazo = motivo.Trim();
    }
}
