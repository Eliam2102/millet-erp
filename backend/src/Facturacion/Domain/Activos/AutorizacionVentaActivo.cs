using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Activos;

/// <summary>Estado de una <see cref="AutorizacionVentaActivo"/> (§7.8).</summary>
public enum EstadoAutorizacionActivo : short
{
    /// <summary>Autorizada por el Contador General; lista para emitir la factura.</summary>
    Autorizada = 1,

    /// <summary>Consumida por una factura de venta de activo (no reutilizable).</summary>
    Usada = 2,

    /// <summary>Cancelada antes de usarse.</summary>
    Cancelada = 3,
}

/// <summary>
/// Autorización del Contador General para vender un activo fijo (§7.8). Es
/// requisito previo a timbrar una factura con comportamiento
/// <c>VentaActivoFijo</c>. Captura el valor en libros y la depreciación acumulada
/// (snapshot de Activos Fijos) para calcular la utilidad/pérdida del asiento de
/// baja, que Contabilidad materializa (evento, F10).
/// </summary>
public sealed class AutorizacionVentaActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string ActivoRef { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;

    public decimal ValorEnLibros { get; private set; }
    public decimal DepreciacionAcumulada { get; private set; }
    public bool EsImportacion { get; private set; }

    public decimal PrecioVenta { get; private set; }

    public Guid AutorizadoPor { get; private set; }
    public DateTimeOffset FechaAutorizacion { get; private set; }

    public EstadoAutorizacionActivo Estado { get; private set; }
    public Guid? FacturaVentaId { get; private set; }

    /// <summary>Valor neto en libros = valor − depreciación acumulada.</summary>
    public decimal ValorNetoEnLibros => ValorEnLibros - DepreciacionAcumulada;

    /// <summary>Utilidad (&gt;0) o pérdida (&lt;0) en la venta = precio − valor neto en libros.</summary>
    public decimal UtilidadOPerdida => PrecioVenta - ValorNetoEnLibros;

    private AutorizacionVentaActivo() { }

    private AutorizacionVentaActivo(
        Guid id, Guid empresaId, string activoRef, string descripcion, decimal valorEnLibros,
        decimal depreciacionAcumulada, bool esImportacion, decimal precioVenta, Guid autorizadoPor, DateTimeOffset ahora) : base(id)
    {
        EmpresaId = empresaId;
        ActivoRef = activoRef;
        Descripcion = descripcion;
        ValorEnLibros = valorEnLibros;
        DepreciacionAcumulada = depreciacionAcumulada;
        EsImportacion = esImportacion;
        PrecioVenta = precioVenta;
        AutorizadoPor = autorizadoPor;
        FechaAutorizacion = ahora;
        Estado = EstadoAutorizacionActivo.Autorizada;
    }

    public static AutorizacionVentaActivo Crear(
        Guid empresaId, string activoRef, string descripcion, decimal valorEnLibros, decimal depreciacionAcumulada,
        bool esImportacion, decimal precioVenta, Guid autorizadoPor, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(activoRef))
            throw new BusinessRuleException("ACTIVO_REF_INVALIDA", "La referencia del activo es obligatoria.");
        if (precioVenta <= 0)
            throw new BusinessRuleException("ACTIVO_PRECIO_INVALIDO", "El precio de venta debe ser mayor que cero.");
        if (autorizadoPor == Guid.Empty)
            throw new BusinessRuleException("ACTIVO_AUTORIZADOR_INVALIDO", "Se requiere el usuario autorizador (Contador General).");

        return new AutorizacionVentaActivo(Guid.CreateVersion7(), empresaId, activoRef, descripcion,
            valorEnLibros, depreciacionAcumulada, esImportacion, precioVenta, autorizadoPor, ahora);
    }

    /// <summary>Consume la autorización al emitirse la factura (no reutilizable).</summary>
    public void MarcarUsada(Guid facturaVentaId)
    {
        if (Estado != EstadoAutorizacionActivo.Autorizada)
            throw new BusinessRuleException(
                "AUTORIZACION_NO_DISPONIBLE",
                $"La autorización no está disponible para usarse (estado actual: {Estado}).");

        Estado = EstadoAutorizacionActivo.Usada;
        FacturaVentaId = facturaVentaId;
    }
}
