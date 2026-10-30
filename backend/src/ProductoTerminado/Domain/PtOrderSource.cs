namespace Millet.ProductoTerminado.Domain;

public sealed record PtOrderSource(Guid Id, string Number, Guid BranchId, string State, string? PaymentTerms,
    Guid CustomerId, string Currency, decimal Total, long? SourceVersion, IReadOnlyList<PtOrderLineSource> Lines);
public sealed record PtOrderLineSource(int Position, Guid? ProductId, string Description, decimal Quantity, string? SatUnit)
{
    public int ExpectedPieces => SatUnit == "E48" || Quantity < 0 || Quantity > int.MaxValue || decimal.Truncate(Quantity) != Quantity ? 0 : (int)Quantity;
}
