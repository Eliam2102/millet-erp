using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Domain.NotaCargo.Events;

/// <summary>
/// Evento in-process emitido cuando una NotaCargo pasa a
/// <see cref="EstadoNotaCargo.Autorizada"/> por Dirección (§8.1).
/// Compras suscribe informativamente.
/// </summary>
public sealed record NotaCargoAutorizadaDomainEvent(
    Guid EmpresaId,
    Guid NotaCargoId,
    Guid ProveedorId,
    decimal Monto,
    Guid? FacturaOrigenId,
    DateTimeOffset OcurridoEn,
    // G1.6: bloque contable opcional (al final). ProveedorId ya existe.
    string? Uuid = null,
    decimal? Subtotal = null,
    decimal? Iva = null,
    decimal? RetencionesTotal = null,
    IReadOnlyList<RetencionCfdi>? Retenciones = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    Guid? SucursalId = null) : INotification;
