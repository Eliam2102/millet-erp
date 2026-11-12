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
    public DbSet<TraspasoPt> Traspasos => Set<TraspasoPt>();
    public DbSet<SalidaPt> Salidas => Set<SalidaPt>();
    public DbSet<AlbaranPt> Albaranes => Set<AlbaranPt>();
    public DbSet<AlbaranPtPieza> AlbaranPiezas => Set<AlbaranPtPieza>();
    public DbSet<LiquidacionRutaPt> Liquidaciones => Set<LiquidacionRutaPt>();
    public DbSet<AvisoContraEntregaPt> AvisosContraEntrega => Set<AvisoContraEntregaPt>();
    public DbSet<EntregaPt> Entregas => Set<EntregaPt>();
    public DbSet<SaldoDemo> SaldosDemo => Set<SaldoDemo>();
    public DbSet<EscaneoPt> Escaneos => Set<EscaneoPt>();
    public DbSet<EntregaPtVersion> EntregaVersiones => Set<EntregaPtVersion>();
    public DbSet<SolicitudSalidaParcialPt> SolicitudesParciales => Set<SolicitudSalidaParcialPt>();
    public DbSet<CasoDanoPt> CasosDano => Set<CasoDanoPt>();
    public DbSet<CasoDanoPtPieza> CasoDanoPiezas => Set<CasoDanoPtPieza>();
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
        p.Property(x => x.Liberacion).HasConversion<string>().HasMaxLength(20);
        p.Property(x => x.MotivoLiberacion).HasMaxLength(30);
        p.HasIndex(x => x.Liberacion).HasDatabaseName("ix_pt_pedidos_retenidos").HasFilter("liberacion = 'Retenido'");
        p.HasIndex(x => new { x.EmpresaId, x.NumeroPedido }).IsUnique();
        p.HasIndex(x => new { x.EmpresaId, x.SucursalVentaId });
        p.HasMany(x => x.Posiciones).WithOne().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        p.Navigation(x => x.Posiciones).UsePropertyAccessMode(PropertyAccessMode.Field);

        var sd = modelBuilder.Entity<SaldoDemo>();
        sd.ToTable("saldos_demo");
        sd.HasKey(x => x.Id);
        sd.Property(x => x.Id).ValueGeneratedNever();
        sd.Property(x => x.NumeroPedido).HasMaxLength(50);
        sd.Property(x => x.Saldo).HasPrecision(18, 2);
        sd.HasIndex(x => new { x.EmpresaId, x.NumeroPedido }).IsUnique();

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
        pz.Property(x => x.Almacen).HasConversion<string>().HasMaxLength(20);
        pz.Property(x => x.Lugar).HasMaxLength(100);
        pz.Property(x => x.Costo).HasPrecision(18, 4);

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

        var t = modelBuilder.Entity<TraspasoPt>();
        t.ToTable("traspasos_pt");
        t.HasKey(x => x.Id);
        t.Property(x => x.Id).ValueGeneratedNever();
        t.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        t.HasIndex(x => new { x.EmpresaId, x.Estado, x.SalidaAt });
        t.HasMany(x => x.Piezas).WithOne().HasForeignKey(x => x.TraspasoPtId).OnDelete(DeleteBehavior.Restrict);
        t.Navigation(x => x.Piezas).UsePropertyAccessMode(PropertyAccessMode.Field);

        var tp = modelBuilder.Entity<TraspasoPtPieza>();
        tp.ToTable("traspaso_pt_piezas");
        tp.HasKey(x => x.Id);
        tp.Property(x => x.Id).ValueGeneratedNever();
        tp.Ignore(x => x.AggregateRootId);
        tp.HasIndex(x => new { x.TraspasoPtId, x.PiezaId }).IsUnique();
        tp.HasOne<PiezaPt>().WithMany().HasForeignKey(x => x.PiezaId).OnDelete(DeleteBehavior.Restrict);

        var sp = modelBuilder.Entity<SalidaPt>();
        sp.ToTable("salidas_pt");
        sp.HasKey(x => x.Id);
        sp.Property(x => x.Id).ValueGeneratedNever();
        sp.Property(x => x.Folio).HasMaxLength(40);
        sp.Property(x => x.Destino).HasMaxLength(200);
        sp.Property(x => x.OrigenCodigoPostal).HasMaxLength(5);
        sp.Property(x => x.OrigenEstado).HasMaxLength(3);
        sp.Property(x => x.DestinoCodigoPostal).HasMaxLength(5);
        sp.Property(x => x.DestinoEstado).HasMaxLength(3);
        sp.Property(x => x.DistanciaKm).HasPrecision(12, 2);
        sp.Property(x => x.PesoKg).HasPrecision(12, 2);
        sp.Property(x => x.CartaPorteFolio).HasMaxLength(40);
        sp.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        sp.HasIndex(x => new { x.EmpresaId, x.Folio }).IsUnique();
        sp.HasIndex(x => new { x.EmpresaId, x.Estado, x.CreadaAt });
        sp.HasMany(x => x.Albaranes).WithOne().HasForeignKey(x => x.SalidaPtId).OnDelete(DeleteBehavior.Restrict);
        sp.Navigation(x => x.Albaranes).UsePropertyAccessMode(PropertyAccessMode.Field);

        var al = modelBuilder.Entity<AlbaranPt>();
        al.ToTable("albaranes_pt");
        al.HasKey(x => x.Id);
        al.Property(x => x.Id).ValueGeneratedNever();
        al.Property(x => x.Folio).HasMaxLength(60);
        al.Property(x => x.MotivoCancelacion).HasMaxLength(300);
        al.Ignore(x => x.Cancelado);
        al.Ignore(x => x.Entregado);
        al.Ignore(x => x.Liquidado);
        al.Ignore(x => x.CancelacionPendiente);
        al.Property(x => x.MotivoSolicitudCancelacion).HasMaxLength(500);
        al.Property(x => x.MotivoRechazoCancelacion).HasMaxLength(500);
        al.HasIndex(x => new { x.EmpresaId, x.Folio }).IsUnique();
        al.HasIndex(x => new { x.SalidaPtId, x.PedidoPtId }).IsUnique();
        al.HasOne<PedidoPt>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        al.HasMany(x => x.Piezas).WithOne().HasForeignKey(x => x.AlbaranPtId).OnDelete(DeleteBehavior.Restrict);
        al.Navigation(x => x.Piezas).UsePropertyAccessMode(PropertyAccessMode.Field);

        var ap = modelBuilder.Entity<AlbaranPtPieza>();
        ap.ToTable("albaran_pt_piezas");
        ap.HasKey(x => x.Id);
        ap.Property(x => x.Id).ValueGeneratedNever();
        ap.Ignore(x => x.AggregateRootId);
        ap.HasIndex(x => new { x.AlbaranPtId, x.PiezaId }).IsUnique();
        ap.HasOne<PiezaPt>().WithMany().HasForeignKey(x => x.PiezaId).OnDelete(DeleteBehavior.Restrict);

        var en = modelBuilder.Entity<EntregaPt>();
        en.ToTable("entregas_pt");
        en.HasKey(x => x.Id);
        en.Property(x => x.Id).ValueGeneratedNever();
        en.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(20);
        en.Property(x => x.Receptor).HasMaxLength(200);
        en.Property(x => x.MotivoRechazo).HasMaxLength(500);
        en.Property(x => x.CobroMonto).HasPrecision(18, 2);
        en.Property(x => x.CobroForma).HasMaxLength(20);
        en.Property(x => x.CobroReferencia).HasMaxLength(100);
        en.Property(x => x.Usuario).HasMaxLength(100);
        en.HasIndex(x => x.AlbaranPtId).IsUnique();
        en.HasIndex(x => x.PedidoPtId);
        en.HasIndex(x => x.CobroConciliadoAt).HasDatabaseName("ix_pt_entregas_cobro_pendiente").HasFilter("cobro_monto IS NOT NULL AND cobro_conciliado_at IS NULL");
        en.HasOne<AlbaranPt>().WithMany().HasForeignKey(x => x.AlbaranPtId).OnDelete(DeleteBehavior.Restrict);

        var ev = modelBuilder.Entity<EntregaPtVersion>();
        ev.ToTable("entrega_pt_versiones");
        ev.HasKey(x => x.Id);
        ev.Property(x => x.Id).ValueGeneratedNever();
        ev.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(20);
        ev.Property(x => x.Receptor).HasMaxLength(200);
        ev.Property(x => x.MotivoRechazo).HasMaxLength(500);
        ev.Property(x => x.Usuario).HasMaxLength(100);
        ev.Property(x => x.CorregidaPor).HasMaxLength(100);
        ev.Property(x => x.MotivoCorreccion).HasMaxLength(500);
        ev.HasIndex(x => new { x.EntregaPtId, x.Numero }).IsUnique();
        ev.HasOne<EntregaPt>().WithMany().HasForeignKey(x => x.EntregaPtId).OnDelete(DeleteBehavior.Restrict);

        var sol = modelBuilder.Entity<SolicitudSalidaParcialPt>();
        sol.ToTable("solicitudes_salida_parcial_pt");
        sol.HasKey(x => x.Id);
        sol.Property(x => x.Id).ValueGeneratedNever();
        sol.Property(x => x.Motivo).HasMaxLength(500);
        sol.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        sol.Property(x => x.Nivel).HasMaxLength(20);
        sol.Property(x => x.Suplencia).HasMaxLength(500);
        sol.Property(x => x.MotivoResolucion).HasMaxLength(500);
        sol.HasIndex(x => new { x.PedidoPtId, x.Estado });
        sol.HasIndex(x => x.PedidoPtId).IsUnique().HasFilter("estado = 'Pendiente'").HasDatabaseName("ux_pt_parcial_pendiente_por_pedido");
        sol.HasOne<PedidoPt>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);

        var cd = modelBuilder.Entity<CasoDanoPt>();
        cd.ToTable("casos_dano_pt");
        cd.HasKey(x => x.Id);
        cd.Property(x => x.Id).ValueGeneratedNever();
        cd.Property(x => x.Folio).HasMaxLength(40);
        cd.Property(x => x.Etapa).HasConversion<string>().HasMaxLength(20);
        cd.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        cd.Property(x => x.Dictamen).HasConversion<string>().HasMaxLength(20);
        cd.Property(x => x.Correctivo).HasConversion<string>().HasMaxLength(20);
        cd.Property(x => x.Motivo).HasMaxLength(500);
        cd.Property(x => x.ObservacionDictamen).HasMaxLength(500);
        cd.Property(x => x.PedidoRefabricacionAw).HasMaxLength(50);
        cd.Property(x => x.DocumentoFiscalFolio).HasMaxLength(60);
        cd.HasIndex(x => new { x.EmpresaId, x.Folio }).IsUnique();
        cd.HasIndex(x => new { x.EmpresaId, x.Estado, x.RegistradoAt });
        cd.HasOne<PedidoPt>().WithMany().HasForeignKey(x => x.PedidoPtId).OnDelete(DeleteBehavior.Restrict);
        cd.HasMany(x => x.Piezas).WithOne().HasForeignKey(x => x.CasoDanoPtId).OnDelete(DeleteBehavior.Restrict);
        cd.Navigation(x => x.Piezas).UsePropertyAccessMode(PropertyAccessMode.Field);

        var cdp = modelBuilder.Entity<CasoDanoPtPieza>();
        cdp.ToTable("caso_dano_pt_piezas");
        cdp.HasKey(x => x.Id);
        cdp.Property(x => x.Id).ValueGeneratedNever();
        cdp.Ignore(x => x.AggregateRootId);
        cdp.Property(x => x.Etiqueta).HasMaxLength(100);
        cdp.HasIndex(x => new { x.CasoDanoPtId, x.PiezaId }).IsUnique();
        cdp.HasIndex(x => x.PiezaId);
        cdp.HasOne<PiezaPt>().WithMany().HasForeignKey(x => x.PiezaId).OnDelete(DeleteBehavior.Restrict);

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

        var lq = modelBuilder.Entity<LiquidacionRutaPt>();
        lq.ToTable("liquidaciones_ruta_pt");
        lq.HasKey(x => x.Id);
        lq.Property(x => x.Id).ValueGeneratedNever();
        lq.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        lq.Property(x => x.Usuario).HasMaxLength(100);
        lq.HasIndex(x => x.SalidaPtId).IsUnique();
        lq.HasOne<SalidaPt>().WithMany().HasForeignKey(x => x.SalidaPtId).OnDelete(DeleteBehavior.Restrict);
        lq.HasMany(x => x.Albaranes).WithOne().HasForeignKey(x => x.LiquidacionRutaPtId).OnDelete(DeleteBehavior.Restrict);
        lq.Navigation(x => x.Albaranes).UsePropertyAccessMode(PropertyAccessMode.Field);
        lq.HasMany(x => x.Imprevistos).WithOne().HasForeignKey(x => x.LiquidacionRutaPtId).OnDelete(DeleteBehavior.Restrict);
        lq.Navigation(x => x.Imprevistos).UsePropertyAccessMode(PropertyAccessMode.Field);

        var la = modelBuilder.Entity<LiquidacionAlbaranPt>();
        la.ToTable("liquidacion_albaranes_pt");
        la.HasKey(x => x.Id);
        la.Property(x => x.Id).ValueGeneratedNever();
        la.Ignore(x => x.AggregateRootId);
        la.Ignore(x => x.Diferencia);
        la.Ignore(x => x.Conciliado);
        la.Property(x => x.Esperado).HasPrecision(18, 2);
        la.Property(x => x.Efectivo).HasPrecision(18, 2);
        la.Property(x => x.Voucher).HasPrecision(18, 2);
        la.Property(x => x.VoucherReferencia).HasMaxLength(100);
        la.Property(x => x.TipoTarjeta).HasMaxLength(10);
        la.Property(x => x.TarjetaUltimos4).HasMaxLength(4);
        la.HasIndex(x => new { x.LiquidacionRutaPtId, x.AlbaranPtId }).IsUnique();
        la.HasOne<AlbaranPt>().WithMany().HasForeignKey(x => x.AlbaranPtId).OnDelete(DeleteBehavior.Restrict);

        var ir = modelBuilder.Entity<ImprevistoRutaPt>();
        ir.ToTable("imprevistos_ruta_pt");
        ir.HasKey(x => x.Id);
        ir.Property(x => x.Id).ValueGeneratedNever();
        ir.Ignore(x => x.AggregateRootId);
        ir.Property(x => x.Descripcion).HasMaxLength(300);
        ir.Property(x => x.Importe).HasPrecision(18, 2);

        var av = modelBuilder.Entity<AvisoContraEntregaPt>();
        av.ToTable("avisos_contra_entrega_pt");
        av.HasKey(x => x.Id);
        av.Property(x => x.Id).ValueGeneratedNever();
        av.Property(x => x.Importe).HasPrecision(18, 2);
        av.HasIndex(x => x.AlbaranPtId).IsUnique();
        av.HasOne<AlbaranPt>().WithMany().HasForeignKey(x => x.AlbaranPtId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
