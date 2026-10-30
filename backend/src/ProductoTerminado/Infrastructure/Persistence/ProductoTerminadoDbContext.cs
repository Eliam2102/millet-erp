using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Infrastructure.Persistence;

/// <summary>Contexto del módulo Producto Terminado (esquema propio, ADR-0030). Sin FK hacia otros esquemas.</summary>
public sealed class ProductoTerminadoDbContext(DbContextOptions<ProductoTerminadoDbContext> options, ICurrentEmpresaContext empresa) : BaseDbContext(options, empresa)
{
    public const string SchemaName = "producto_terminado";
    public DbSet<PedidoPt> PedidosPt => Set<PedidoPt>();
    public DbSet<PedidoPtPosicion> Posiciones => Set<PedidoPtPosicion>();
    public DbSet<PiezaPt> Piezas => Set<PiezaPt>();
    public DbSet<MovimientoPieza> Movimientos => Set<MovimientoPieza>();
    public DbSet<EscaneoPt> Escaneos => Set<EscaneoPt>();
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        var p = modelBuilder.Entity<PedidoPt>();
        p.ToTable("pedidos_pt");
        p.HasKey(x => x.Id);
        p.Property(x => x.Id).ValueGeneratedNever();
        p.Property(x => x.NumeroPedido).HasMaxLength(50);
        p.Property(x => x.CondicionPagoOrigen).HasMaxLength(50);
        p.HasIndex(x => new { x.EmpresaId, x.NumeroPedido }).IsUnique();
        p.HasIndex(x => new { x.EmpresaId, x.SucursalVentaId });
        p.HasMany(x => x.Posiciones).WithOne().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        p.Navigation(x => x.Posiciones).UsePropertyAccessMode(PropertyAccessMode.Field);

        var pos = modelBuilder.Entity<PedidoPtPosicion>();
        pos.ToTable("pedido_pt_posiciones");
        pos.HasKey(x => x.Id);
        pos.Property(x => x.Id).ValueGeneratedNever();
        pos.Ignore(x => x.AggregateRootId);
        pos.Property(x => x.Descripcion).HasMaxLength(500);
        pos.HasIndex(x => new { x.PedidoPtId, x.Posicion }).IsUnique();

        var pz = modelBuilder.Entity<PiezaPt>();
        pz.ToTable("piezas");
        pz.HasKey(x => x.Id);
        pz.Property(x => x.Id).ValueGeneratedNever();
        pz.Ignore(x => x.AggregateRootId);
        pz.Property(x => x.Etiqueta).HasMaxLength(100);
        pz.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        pz.HasIndex(x => new { x.PedidoPtId, x.Posicion, x.NumeroPieza }).IsUnique();
        pz.HasIndex(x => new { x.EmpresaId, x.Etiqueta }).IsUnique();
        pz.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Estado });
        pz.HasIndex(x => new { x.PedidoPtId, x.Estado });
        pz.HasOne<PedidoPt>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);

        var m = modelBuilder.Entity<MovimientoPieza>();
        m.ToTable("movimientos_pieza");
        m.HasKey(x => x.Id);
        m.Property(x => x.Id).ValueGeneratedNever();
        m.Ignore(x => x.AggregateRootId);
        m.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
        m.Property(x => x.EstadoAnterior).HasMaxLength(20);
        m.Property(x => x.EstadoNuevo).HasMaxLength(20);
        m.Property(x => x.Referencia).HasMaxLength(200);
        m.Property(x => x.Usuario).HasMaxLength(100);
        m.HasIndex(x => new { x.PiezaId, x.Tipo, x.Referencia }).IsUnique();
        m.HasOne<PiezaPt>().WithMany().HasForeignKey(x => x.PiezaId).OnDelete(DeleteBehavior.Restrict);

        var e = modelBuilder.Entity<EscaneoPt>();
        e.ToTable("escaneos");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Origen).HasMaxLength(20);
        e.Property(x => x.Etiqueta).HasMaxLength(100);
        e.Property(x => x.NumeroPedido).HasMaxLength(50);
        e.Property(x => x.NumeroPiezaTexto).HasMaxLength(50);
        e.Property(x => x.Area).HasMaxLength(200);
        e.Property(x => x.RegistradoAt).HasColumnType("timestamp without time zone");
        e.Property(x => x.ClaveSucursalAw).HasMaxLength(50);
        e.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Motivo).HasMaxLength(100);
        e.Property(x => x.MotivoResolucion).HasMaxLength(500);
        e.Ignore(x => x.EstaAbierto);
        e.HasIndex(x => new { x.EmpresaId, x.Origen, x.Etiqueta, x.RegistradoAt }).IsUnique().HasDatabaseName("ux_pt_escaneo_origen_etiqueta_hora");
        e.HasIndex(x => x.Resultado).HasDatabaseName("ix_pt_escaneos_abiertos").HasFilter("resultado IN ('Pospuesto','Excepcion','Reposicion')");
        e.HasIndex(x => new { x.EmpresaId, x.Origen, x.RegistradoAt }).HasDatabaseName("ix_pt_escaneos_marca_agua");

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
