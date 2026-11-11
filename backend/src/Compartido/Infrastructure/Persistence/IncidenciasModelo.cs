using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Infrastructure.Persistence;

/// <summary>U-4 (86e3yzemw): incidencias comunes del ERP, su historial y el tipo de adjunto de evidencia.</summary>
internal static class IncidenciasModelo
{
    public const string TipoEntidadAdjunto = "incidencia";
    public static readonly Guid TipoDocumentoEvidenciaId = Guid.Parse("00000011-0008-0000-0000-000000000001");

    public static void Configure(ModelBuilder modelBuilder)
    {
        var i = modelBuilder.Entity<Incidencia>();
        i.ToTable("incidencias", t =>
        {
            t.HasCheckConstraint("ck_incidencias_estado", "estado BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_incidencias_motivo", "char_length(motivo) BETWEEN 5 AND 1000");
            t.HasCheckConstraint("ck_incidencias_resuelta_coherente",
                "(estado = 3 AND resuelta_en IS NOT NULL AND resolucion IS NOT NULL) OR (estado <> 3 AND resuelta_en IS NULL)");
        });
        i.HasKey(x => x.Id);
        i.Property(x => x.Id).ValueGeneratedNever();
        i.Ignore(x => x.Pendiente);
        i.Property(x => x.Numero).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
        i.Property(x => x.Numero).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        i.Property(x => x.Modulo).HasMaxLength(40).IsRequired();
        i.Property(x => x.TipoDocumento).HasMaxLength(60).IsRequired();
        i.Property(x => x.DocumentoEtiqueta).HasMaxLength(200).IsRequired();
        i.Property(x => x.Paso).HasMaxLength(100).IsRequired();
        i.Property(x => x.CodigoMotivo).HasMaxLength(100);
        i.Property(x => x.Motivo).HasMaxLength(1000).IsRequired();
        i.Property(x => x.Detalle).HasMaxLength(2000);
        i.Property(x => x.DetalleClave).HasMaxLength(100).IsRequired();
        i.Property(x => x.ResponsableNombre).HasMaxLength(200).IsRequired();
        i.Property(x => x.FechaCompromiso).HasColumnType("date");
        i.Property(x => x.AbiertaPorNombre).HasMaxLength(200).IsRequired();
        i.Property(x => x.Estado).HasConversion<short>().IsRequired();
        i.Property(x => x.PasoReanudacion).HasMaxLength(100);
        i.Property(x => x.RutaReanudacion).HasMaxLength(300);
        i.Property(x => x.ResueltaPorNombre).HasMaxLength(200);
        i.Property(x => x.Resolucion).HasMaxLength(1000);

        i.HasMany(x => x.Historial).WithOne().HasForeignKey(m => m.IncidenciaId).OnDelete(DeleteBehavior.Cascade);
        i.Navigation(x => x.Historial).UsePropertyAccessMode(PropertyAccessMode.Field);

        i.HasIndex(x => x.Numero).IsUnique();
        i.HasIndex(x => new { x.ResponsableUsuarioId, x.Estado });
        i.HasIndex(x => new { x.DocumentoId, x.Paso });
        i.HasIndex(x => new { x.EmpresaId, x.Modulo, x.Estado });
        // Una sola pendiente por (empresa, paso, documento, detalle): abrir de nuevo devuelve la existente.
        i.HasIndex(x => new { x.EmpresaId, x.Paso, x.DocumentoId, x.DetalleClave })
            .IsUnique()
            .HasDatabaseName("ux_incidencias_pendiente")
            .HasFilter("estado IN (1, 2)");

        var m = modelBuilder.Entity<IncidenciaMovimiento>();
        m.ToTable("incidencia_historial");
        m.HasKey(x => x.Id);
        m.Property(x => x.Id).ValueGeneratedNever();
        m.Property(x => x.Accion).HasMaxLength(30).IsRequired();
        m.Property(x => x.EstadoAnterior).HasConversion<short?>();
        m.Property(x => x.EstadoNuevo).HasConversion<short>().IsRequired();
        m.Property(x => x.UsuarioNombre).HasMaxLength(200).IsRequired();
        m.Property(x => x.Nota).HasMaxLength(1000);
        m.HasIndex(x => new { x.IncidenciaId, x.Orden }).IsUnique();

        var fecha = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        modelBuilder.Entity<AdjuntoTipoDocumento>().HasData(new
        {
            Id = TipoDocumentoEvidenciaId, Condicional = false, TipoEntidad = TipoEntidadAdjunto, Codigo = "evidencia_incidencia", Nombre = "Evidencia de la incidencia",
            Orden = 1, Obligatorio = false, ObligatorioPersonaMoral = false, ObligatorioPersonaFisica = false, VigenciaMeses = (int?)null,
            SoloPersonaMoral = false, AplicaOrigen = AdjuntoAplicaOrigen.Todos, Activo = true, Version = 1, CreatedAt = fecha, UpdatedAt = fecha,
            CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null,
        });
    }
}
