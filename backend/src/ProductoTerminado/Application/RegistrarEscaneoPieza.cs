using Millet.ProductoTerminado.Domain;
using MediatR;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application;

public sealed record RegistrarEscaneoPieza(Guid EmpresaId, string Origin, string SourceEventId, string OrderNumber,
    int Position, int? PieceNumber, string? BranchAwKey, Guid? BranchId, DateTimeOffset ScannedAt,
    string? Barcode = null, string? AwUser = null)
    : PtScanInput(EmpresaId, Origin, SourceEventId, OrderNumber, Position, PieceNumber, BranchAwKey, BranchId, ScannedAt, Barcode, AwUser), IRequest<ScanResult>;
public sealed record PiezaIngresadaEvent(Guid EmpresaId, Guid PiezaId, string Pedido, int Posicion,
    Guid? ProductoId, Guid SucursalId, DateTimeOffset Fecha)
    : IntegrationEvent("producto_terminado.pieza.ingresada.v1", EmpresaId, Fecha);
public interface IProductoTerminadoSucursalReadPort : IDocumentoSucursalReadPort;
public sealed class PtScanOptions
{
    public int PospuestoHoras { get; set; } = 24;
    public int MaxIntentos { get; set; } = 5;
}
