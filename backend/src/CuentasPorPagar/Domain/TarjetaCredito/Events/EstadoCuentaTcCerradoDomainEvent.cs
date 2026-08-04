using MediatR;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito.Events;

/// <summary>
/// Domain event publicado cuando un <see cref="EstadoCuentaTc"/>
/// transiciona a <see cref="EstadoCuentaTcStatus.Cerrado"/> (§5.4 anexo,
/// F7-PR6). Lleva la FK a la <c>FacturaProveedor</c> agregada contra
/// el banco para que Tesorería + Contabilidad la procesen.
/// </summary>
public sealed record EstadoCuentaTcCerradoDomainEvent(
    Guid EmpresaId,
    Guid EstadoCuentaTcId,
    Guid TarjetaId,
    decimal TotalBancoMxn,
    Guid FacturaProveedorId,
    decimal? DiferenciaCambiariaMxn,
    DateTimeOffset OcurridoEn) : INotification;
