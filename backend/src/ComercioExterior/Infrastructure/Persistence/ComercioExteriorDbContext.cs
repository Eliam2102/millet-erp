using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.ComercioExterior.Infrastructure.Persistence;
public sealed class ComercioExteriorDbContext(DbContextOptions<ComercioExteriorDbContext> options, ICurrentEmpresaContext empresa) : BaseDbContext(options, empresa)
{
    public const string SchemaName = "comercio_exterior";
    public DbSet<Expediente> Expedientes => Set<Expediente>();
    public DbSet<ParticipanteExpediente> Participantes => Set<ParticipanteExpediente>();
    public DbSet<DocumentoExpediente> Documentos => Set<DocumentoExpediente>();
    public DbSet<HitoExpediente> Hitos => Set<HitoExpediente>();
    public DbSet<AvisoHitoVencido> Avisos => Set<AvisoHitoVencido>();
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.HasSequence<long>("folio_seq", SchemaName);
        var e = modelBuilder.Entity<Expediente>();
        e.ToTable("expedientes");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Folio).HasMaxLength(24);
        e.Property(x => x.Referencia).HasMaxLength(200);
        e.Property(x => x.Moneda).HasMaxLength(3);
        e.Property(x => x.Tc).HasPrecision(18, 6);
        e.Property(x => x.FuenteTc).HasMaxLength(200);
        e.Property(x => x.MotivoCancelacion).HasMaxLength(500);
        e.Property(x => x.Tipo).HasConversion<string>();
        e.Property(x => x.Estado).HasConversion<string>();
        e.HasIndex(x => new { x.EmpresaId, x.Folio }).IsUnique();
        e.HasIndex(x => new { x.EmpresaId, x.OcId }).IsUnique().HasDatabaseName("ux_ce_oc_abierta").HasFilter("oc_id IS NOT NULL AND estado = 'Abierto'");
        e.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Estado });
        e.HasMany(x => x.Participantes).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Participantes).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Documentos).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Documentos).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Hitos).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Hitos).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Avisos).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Avisos).UsePropertyAccessMode(PropertyAccessMode.Field);
        var h = modelBuilder.Entity<HitoExpediente>();
        h.ToTable("hitos");
        h.HasKey(x => x.Id);
        h.Property(x => x.Id).ValueGeneratedNever();
        h.Ignore(x => x.AggregateRootId);
        h.Property(x => x.Clave).HasMaxLength(40);
        h.Property(x => x.Motivo).HasMaxLength(500);
        h.Property(x => x.MotivoCambioCompromiso).HasMaxLength(500);
        h.HasIndex(x => new { x.ExpedienteId, x.Clave }).IsUnique();
        h.HasIndex(x => new { x.ExpedienteId, x.Orden }).IsUnique();
        h.HasIndex(x => x.FechaCompromiso).HasFilter("fecha_real IS NULL AND NOT omitido");
        var a = modelBuilder.Entity<AvisoHitoVencido>();
        a.ToTable("avisos_hito_vencido");
        a.HasKey(x => x.Id);
        a.Property(x => x.Id).ValueGeneratedNever();
        a.Ignore(x => x.AggregateRootId);
        a.HasIndex(x => new { x.HitoId, x.FechaCompromiso }).IsUnique();
        a.HasOne<HitoExpediente>().WithMany().HasForeignKey(x => x.HitoId).OnDelete(DeleteBehavior.Restrict);
        var p = modelBuilder.Entity<ParticipanteExpediente>();
        p.ToTable("participantes");
        p.HasKey(x => x.Id);
        p.Property(x => x.Id).ValueGeneratedNever();
        p.Ignore(x => x.AggregateRootId);
        p.Property(x => x.Nombre).HasMaxLength(200);
        p.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(80);
        p.Property(x => x.Descripcion).HasMaxLength(300);
        p.Property(x => x.Contacto).HasMaxLength(300);
        p.Property(x => x.Papel).HasConversion<string>();
        var d = modelBuilder.Entity<DocumentoExpediente>();
        d.ToTable("documentos");
        d.HasKey(x => x.Id);
        d.Property(x => x.Id).ValueGeneratedNever();
        d.Ignore(x => x.AggregateRootId);
        d.Property(x => x.Estado).HasConversion<string>();
        d.Property(x => x.Motivo).HasMaxLength(500);
        d.HasIndex(x => new { x.ExpedienteId, x.TipoDocumentoId, x.NumeroVersion }).IsUnique();
        d.HasIndex(x => new { x.ExpedienteId, x.TipoDocumentoId }).IsUnique().HasFilter("estado = 'Vigente'");
        d.HasOne<DocumentoExpediente>().WithMany().HasForeignKey(x => x.ReemplazaA).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
