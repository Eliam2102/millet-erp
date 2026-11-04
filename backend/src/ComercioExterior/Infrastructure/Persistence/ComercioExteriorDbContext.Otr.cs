using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
namespace Millet.ComercioExterior.Infrastructure.Persistence;
public sealed partial class ComercioExteriorDbContext
{
    private static void ConfigurarOtr(ModelBuilder m)
    {
        var control = m.Entity<OtrEntradaControl>(); control.ToTable("otr_entrada_control"); control.HasKey(x => x.Id); control.Property(x => x.Id).ValueGeneratedNever();
        control.Property(x => x.IdOrigen).HasMaxLength(100); control.Property(x => x.UltimoError).HasMaxLength(500);
        control.HasIndex(x => new { x.EmpresaId, x.IdOrigen, x.VersionOrigen }).IsUnique();
        var e = m.Entity<OtrEnvio>(); e.ToTable("otr_envios"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.IdOrigen).HasMaxLength(100); e.Property(x => x.Hash).HasMaxLength(64);
        e.Property(x => x.ContenidoJson).HasColumnType("jsonb"); e.Property(x => x.EdicionesJson).HasColumnType("jsonb");
        e.Property(x => x.Estado).HasConversion<string>();
        e.HasIndex(x => new { x.EmpresaId, x.IdOrigen }).IsUnique();
        e.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Estado });
        e.HasOne<Expediente>().WithMany().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ContenedorExpediente>().WithMany().HasForeignKey(x => x.ContenedorId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lineas).WithOne().HasForeignKey(x => x.OtrEnvioId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Conflictos).WithOne().HasForeignKey(x => x.OtrEnvioId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Conflictos).UsePropertyAccessMode(PropertyAccessMode.Field);
        var l = m.Entity<OtrLinea>(); l.ToTable("otr_lineas"); l.HasKey(x => x.Id); l.Property(x => x.Id).ValueGeneratedNever(); l.Ignore(x => x.AggregateRootId);
        l.Property(x => x.NumeroPedido).HasMaxLength(50); l.Property(x => x.Unidad).HasMaxLength(20); l.Property(x => x.Motivo).HasMaxLength(1000);
        l.Property(x => x.Cantidad).HasPrecision(18, 6); l.Property(x => x.Estado).HasConversion<string>();
        l.HasIndex(x => new { x.OtrEnvioId, x.VersionOrigen, x.NumeroPedido, x.Posicion }).IsUnique();
        l.HasOne<PartidaEmbarcada>().WithMany().HasForeignKey(x => x.PartidaId).OnDelete(DeleteBehavior.SetNull);
        var c = m.Entity<OtrConflicto>(); c.ToTable("otr_conflictos"); c.HasKey(x => x.Id); c.Property(x => x.Id).ValueGeneratedNever(); c.Ignore(x => x.AggregateRootId);
        c.Ignore(x => x.Estado); c.Property(x => x.Campo).HasMaxLength(50); c.Property(x => x.Motivo).HasMaxLength(500);
        c.HasIndex(x => new { x.OtrEnvioId, x.VersionOrigen, x.Campo }).IsUnique();
        var o = m.Entity<OtrSalida>(); o.ToTable("otr_salidas"); o.HasKey(x => x.Id); o.Property(x => x.Id).ValueGeneratedNever();
        o.Property(x => x.Tipo).HasMaxLength(50); o.Property(x => x.ValorJson).HasColumnType("jsonb"); o.Property(x => x.Estado).HasConversion<string>();
        o.HasIndex(x => new { x.OtrEnvioId, x.Tipo, x.VersionErp, x.Intento }).IsUnique();
        o.Property(x => x.Intento).HasDefaultValue(1);
        o.HasOne<OtrSalida>().WithMany().HasForeignKey(x => x.ReemplazadaPorId).OnDelete(DeleteBehavior.Restrict);
        o.HasIndex(x => new { x.EmpresaId, x.Estado });
        o.HasOne<OtrEnvio>().WithMany().HasForeignKey(x => x.OtrEnvioId).OnDelete(DeleteBehavior.Restrict);
    }
}
