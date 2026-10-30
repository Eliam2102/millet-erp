using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Domain;

public sealed class PtOrder : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoFacturableId { get; private set; }
    public string NumeroPedido { get; private set; } = "";
    public Guid SucursalVentaId { get; private set; }
    public Guid SucursalEntregaId { get; private set; }
    public string? CondicionPagoOrigen { get; private set; }
    public long? VersionOrigen { get; private set; }
    private PtOrder() { }
    public PtOrder(Guid empresaId, PtOrderSource source) : base(Guid.CreateVersion7())
    { EmpresaId = empresaId; NumeroPedido = source.Number; PedidoFacturableId = source.Id; Refresh(source); }
    public void Refresh(PtOrderSource source)
    { SucursalVentaId = source.BranchId; SucursalEntregaId = source.BranchId; CondicionPagoOrigen = source.PaymentTerms; VersionOrigen = source.SourceVersion; }
}
public sealed class PtOrderPosition : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public int Posicion { get; private set; }
    public Guid? ProductoId { get; private set; }
    public string Descripcion { get; private set; } = "";
    public int PiezasEsperadas { get; private set; }
    private PtOrderPosition() { }
    public PtOrderPosition(Guid empresaId, Guid orderId, PtOrderLineSource line) : base(Guid.CreateVersion7())
    { EmpresaId = empresaId; PedidoPtId = orderId; Posicion = line.Position; Refresh(line); }
    public void Refresh(PtOrderLineSource line)
    { ProductoId = line.ProductId; Descripcion = line.Description; PiezasEsperadas = line.ExpectedPieces; }
}
public sealed class PtPiece : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PedidoPtId { get; private set; }
    public int Posicion { get; private set; }
    public int NumeroPieza { get; private set; }
    public Guid SucursalId { get; private set; }
    public string Estado { get; private set; } = "EnPT";
    public DateTimeOffset EscaneadaAt { get; private set; }
    public int EstatusAw { get; private set; } = 69;
    public Guid EscaneoId { get; private set; }
    private PtPiece() { }
    public PtPiece(PtScan scan, Guid orderId) : base(Guid.CreateVersion7())
    { EmpresaId = scan.EmpresaId; PedidoPtId = orderId; Posicion = scan.Posicion; NumeroPieza = scan.NumeroPieza!.Value;
      SucursalId = scan.SucursalId!.Value; EscaneadaAt = scan.EscaneadoAt; EscaneoId = scan.Id; }
}
public sealed class PtMovement : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid PiezaId { get; private set; }
    public string Tipo { get; private set; } = "Alta";
    public Guid? SucursalOrigenId { get; private set; }
    public Guid SucursalDestinoId { get; private set; }
    public string? EstadoAnterior { get; private set; }
    public string EstadoNuevo { get; private set; } = "EnPT";
    public Guid Referencia { get; private set; }
    public DateTimeOffset OcurridoAt { get; private set; }
    public string? Usuario { get; private set; }
    private PtMovement() { }
    public PtMovement(PtPiece piece, string? user) : base(Guid.CreateVersion7())
    { EmpresaId = piece.EmpresaId; PiezaId = piece.Id; SucursalDestinoId = piece.SucursalId; Referencia = piece.EscaneoId; OcurridoAt = piece.EscaneadaAt; Usuario = user; }
}
public sealed class PtScan : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Origen { get; private set; } = "";
    public string OrigenEventoId { get; private set; } = "";
    public string NumeroPedido { get; private set; } = "";
    public int Posicion { get; private set; }
    public int? NumeroPieza { get; private set; }
    public string? SucursalClaveAw { get; private set; }
    public Guid? SucursalId { get; private set; }
    public DateTimeOffset EscaneadoAt { get; private set; }
    public string? CodigoBarras { get; private set; }
    public string? UsuarioAw { get; private set; }
    public int EstatusAw { get; private set; } = 69;
    public string Resultado { get; private set; } = "Pospuesto";
    public string? Motivo { get; private set; }
    public int Intentos { get; private set; }
    public Guid? PiezaId { get; private set; }
    public DateTimeOffset FirstAttemptAt { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoAt { get; private set; }
    public string? ResolutionReason { get; private set; }
    private PtScan() { }
    public PtScan(PtScanInput input, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        if (input.EmpresaId == Guid.Empty || string.IsNullOrWhiteSpace(input.SourceEventId) || input.SourceEventId.Length > 300 || input.Origin is not ("Demo" or "Real"))
            throw new BusinessRuleException("PT_EVENTO_INVALIDO", "Revise la empresa, origen e identificador del escaneo.");
        EmpresaId = input.EmpresaId; Origen = input.Origin; OrigenEventoId = input.SourceEventId; NumeroPedido = input.OrderNumber;
        Posicion = input.Position; NumeroPieza = input.PieceNumber; SucursalClaveAw = input.BranchAwKey;
        SucursalId = input.BranchId == Guid.Empty ? null : input.BranchId; EscaneadoAt = input.ScannedAt.ToUniversalTime();
        CodigoBarras = input.Barcode; UsuarioAw = input.AwUser; FirstAttemptAt = now;
    }
    public string? Validate(PtOrderSource? order)
    {
        if (string.IsNullOrWhiteSpace(NumeroPedido) || Posicion < 1 || NumeroPieza is null or < 1) return "PT_IDENTIDAD_INCOMPLETA";
        if (SucursalId is null) return "PT_SUCURSAL_SIN_CLAVE_AW";
        if (order is null) return "PT_PEDIDO_INEXISTENTE";
        if (string.Equals(order.State, "Cancelado", StringComparison.OrdinalIgnoreCase)) return "PT_PEDIDO_CANCELADO";
        var line = order.Lines.SingleOrDefault(x => x.Position == Posicion);
        return line is null ? "PT_POSICION_INEXISTENTE" : NumeroPieza > line.ExpectedPieces ? "PT_PIEZA_EXCEDE_POSICION" : null;
    }
    public void Attempt(string? reason, DateTimeOffset now, int postponedHours)
    {
        Intentos++; LastAttemptAt = now; Motivo = reason;
        Resultado = reason == "PT_PEDIDO_INEXISTENTE" && now - FirstAttemptAt < TimeSpan.FromHours(postponedHours) ? "Pospuesto" : reason is null ? "Ingresada" : "Excepcion";
    }
    public void Complete(Guid pieceId, bool duplicate) { PiezaId = pieceId; Resultado = duplicate ? "Duplicado" : "Ingresada"; Motivo = null; }
    public void Resolve(Guid userId, string reason, DateTimeOffset now, bool discard)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new BusinessRuleException("PT_MOTIVO_REQUERIDO", "Capture un motivo de 1 a 500 caracteres.");
        if (Resultado is not ("Pospuesto" or "Excepcion")) throw new BusinessRuleException("PT_ESCANEO_RESUELTO", "El escaneo ya está resuelto.");
        ResueltoPor = userId; ResueltoAt = now; ResolutionReason = reason.Trim();
        if (discard) Resultado = "Descartado";
    }
    public void SetBranch(string? key, Guid? branch) { SucursalClaveAw = key; SucursalId = branch; }
    public ScanResult ToResult() => new(Id, Resultado, Motivo, PiezaId, Intentos, Version);
}
