using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Infrastructure.Persistence;
public sealed partial class ComercioExteriorDbContext
{
    private static void ConfigurarIncidencias(ModelBuilder model)
    {
        var e = model.Entity<Expediente>();
        e.Property(x => x.NumeroPedimentoVigenteImportacion).HasMaxLength(100);
        e.HasMany(x => x.Incidencias).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Incidencias).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.AvisosIncidencias).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.AvisosIncidencias).UsePropertyAccessMode(PropertyAccessMode.Field);
        var i = model.Entity<IncidenciaExpediente>();
        i.ToTable("incidencias"); i.HasKey(x => x.Id); i.Property(x => x.Id).ValueGeneratedNever();
        i.Property(x => x.CantidadAnterior).HasPrecision(18, 6); i.Property(x => x.CantidadNueva).HasPrecision(18, 6);
        i.Property(x => x.DiferenciaCantidad).HasPrecision(18, 6);
        i.HasIndex(x => new { x.ExpedienteId, x.PartidaId }).IsUnique().HasFilter("automatica AND estado IN ('Abierta', 'EnAtencion')");
        i.Ignore(x => x.AggregateRootId); i.Ignore(x => x.Pendiente);
        i.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40); i.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
        i.Property(x => x.Motivo).HasMaxLength(500); i.Property(x => x.MotivoCancelacion).HasMaxLength(500);
        i.Property(x => x.SiguienteAccion).HasMaxLength(2000);
        i.Property(x => x.Descripcion).HasMaxLength(2000); i.Property(x => x.AccionCorrectiva).HasMaxLength(2000); i.Property(x => x.EfectoPosterior).HasMaxLength(2000);
        i.Property(x => x.HitoClave).HasMaxLength(40); i.Property(x => x.PedimentoAnterior).HasMaxLength(100); i.Property(x => x.PedimentoNuevo).HasMaxLength(100);
        i.HasIndex(x => new { x.EmpresaId, x.Estado, x.Fecha });
        i.HasMany(x => x.Historial).WithOne().HasForeignKey(x => x.IncidenciaId).OnDelete(DeleteBehavior.Restrict);
        i.Navigation(x => x.Historial).UsePropertyAccessMode(PropertyAccessMode.Field);
        var h = model.Entity<EstadoIncidenciaHistorial>();
        h.ToTable("incidencias_historial"); h.HasKey(x => x.Id); h.Property(x => x.Id).ValueGeneratedNever(); h.Ignore(x => x.AggregateRootId);
        h.Property(x => x.SiguienteAccionAnterior).HasMaxLength(2000); h.Property(x => x.SiguienteAccionNueva).HasMaxLength(2000);
        h.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20); h.Property(x => x.Motivo).HasMaxLength(2000);
        var a = model.Entity<AvisoIncidencia>();
        a.ToTable("avisos_incidencia"); a.HasKey(x => x.Id); a.Property(x => x.Id).ValueGeneratedNever(); a.Ignore(x => x.AggregateRootId);
        a.HasIndex(x => new { x.IncidenciaId, x.EsVencimiento }).IsUnique(); a.HasOne<IncidenciaExpediente>().WithMany().HasForeignKey(x => x.IncidenciaId).OnDelete(DeleteBehavior.Restrict);
    }
}
