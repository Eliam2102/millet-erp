using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Infrastructure.Persistence;
public sealed class ProductoTerminadoDbContext(DbContextOptions<ProductoTerminadoDbContext> options, ICurrentEmpresaContext empresa) : BaseDbContext(options, empresa)
{
    public const string SchemaName = "producto_terminado";
    public DbSet<PtOrder> Orders => Set<PtOrder>();
    public DbSet<PtOrderPosition> Positions => Set<PtOrderPosition>();
    public DbSet<PtPiece> Pieces => Set<PtPiece>();
    public DbSet<PtMovement> Movements => Set<PtMovement>();
    public DbSet<PtScan> Scans => Set<PtScan>();
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();
    public DbSet<PtWatermark> Watermarks => Set<PtWatermark>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema(SchemaName);
        var o = b.Entity<PtOrder>(); o.ToTable("pedidos_pt"); o.HasKey(x => x.Id);
        o.HasIndex(x => new { x.EmpresaId, x.NumeroPedido }).IsUnique(); o.HasIndex(x => new { x.EmpresaId, x.SucursalVentaId });
        o.Property(x => x.NumeroPedido).HasMaxLength(100); o.Property(x => x.CondicionPagoOrigen).HasMaxLength(50);
        var l = b.Entity<PtOrderPosition>(); l.ToTable("pedido_pt_posiciones"); l.HasKey(x => x.Id);
        l.HasIndex(x => new { x.PedidoPtId, x.Posicion }).IsUnique();
        l.HasOne<PtOrder>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        var p = b.Entity<PtPiece>(); p.ToTable("piezas"); p.HasKey(x => x.Id);
        p.HasIndex(x => new { x.PedidoPtId, x.Posicion, x.NumeroPieza }).IsUnique();
        p.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Estado }); p.HasIndex(x => new { x.PedidoPtId, x.Estado });
        p.HasOne<PtOrder>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        var m = b.Entity<PtMovement>(); m.ToTable("movimientos_pieza"); m.HasKey(x => x.Id);
        m.HasIndex(x => new { x.PiezaId, x.Tipo, x.Referencia }).IsUnique();
        m.HasOne<PtPiece>().WithMany().HasForeignKey(x => x.PiezaId).OnDelete(DeleteBehavior.Restrict);
        var s = b.Entity<PtScan>(); s.ToTable("escaneos"); s.HasKey(x => x.Id);
        s.Property(x => x.OrigenEventoId).HasMaxLength(300); s.Property(x => x.Motivo).HasMaxLength(100); s.Property(x => x.ResolutionReason).HasMaxLength(500);
        s.HasIndex(x => new { x.EmpresaId, x.Origen, x.OrigenEventoId }).IsUnique();
        s.HasIndex(x => new { x.EmpresaId, x.LastAttemptAt }).HasFilter("resultado IN ('Pospuesto','Excepcion')");
        var w = b.Entity<PtWatermark>(); w.ToTable("reader_watermarks"); w.HasKey(x => new { x.EmpresaId, x.Origin });
        w.Property(x => x.Timestamp).HasColumnType("timestamp without time zone");
        b.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        base.OnModelCreating(b);
    }
}
// Cursor compuesto: no omite filas cuando más de un lote comparte bearb_zeit.
public sealed class PtWatermark
{
    public Guid EmpresaId { get; set; }
    public string Origin { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Unspecified);
    public string Barcode { get; set; } = "";
    public int Station { get; set; }
}
