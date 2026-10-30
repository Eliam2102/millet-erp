namespace Millet.ProductoTerminado.Domain;

public record PtScanInput(Guid EmpresaId, string Origin, string SourceEventId, string OrderNumber,
    int Position, int? PieceNumber, string? BranchAwKey, Guid? BranchId, DateTimeOffset ScannedAt,
    string? Barcode = null, string? AwUser = null);
public sealed record ScanResult(Guid Id, string Result, string? Reason, Guid? PieceId, int Attempts, int Version);
