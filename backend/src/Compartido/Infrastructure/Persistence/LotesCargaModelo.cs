using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Domain.Cargas;

namespace Millet.Compartido.Infrastructure.Persistence;

/// <summary>M-047 (86e3jvvmf): lote común de cargas (<c>compartido.lotes_carga</c>).</summary>
internal static class LotesCargaModelo
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var l = modelBuilder.Entity<LoteCarga>();
        l.ToTable("lotes_carga", t =>
        {
            t.HasCheckConstraint("ck_lotes_carga_estado", "estado BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_lotes_carga_rechazo_con_motivo",
                "estado <> 3 OR (motivo_rechazo IS NOT NULL AND char_length(motivo_rechazo) >= 5)");
            t.HasCheckConstraint("ck_lotes_carga_otra_persona", "estado NOT IN (2, 4) OR decidido_por_id <> subido_por_id");
        });
        l.HasKey(x => x.Id);
        l.Property(x => x.Id).ValueGeneratedNever();
        l.Property(x => x.Tipo).HasMaxLength(80).IsRequired();
        l.Property(x => x.NombreArchivo).HasMaxLength(260).IsRequired();
        l.Property(x => x.Huella).HasMaxLength(64).IsRequired();
        l.Property(x => x.FechaCorte).HasColumnType("date");
        l.Property(x => x.Estado).HasConversion<short>().IsRequired();
        l.Property(x => x.MapeoJson).HasColumnType("jsonb").IsRequired();
        l.Property(x => x.FilasJson).HasColumnType("jsonb").IsRequired();
        l.Property(x => x.ValidacionJson).HasColumnType("jsonb").IsRequired();
        l.Property(x => x.HistorialJson).HasColumnType("jsonb").IsRequired();
        l.Property(x => x.SubidoPorNombre).HasMaxLength(200).IsRequired();
        l.Property(x => x.DecididoPorNombre).HasMaxLength(200);
        l.Property(x => x.MotivoRechazo).HasMaxLength(LoteCarga.MotivoMax);
        l.Property(x => x.Referencia).HasMaxLength(100);
        l.Property(x => x.FolioResultado).HasMaxLength(60);
        l.Property(x => x.UltimoError).HasMaxLength(2000);
        l.HasIndex(x => new { x.EmpresaId, x.Tipo, x.SubidoEn }).HasDatabaseName("ix_lotes_carga_empresa_tipo_subido");
        l.HasIndex(x => new { x.EmpresaId, x.Tipo, x.Huella }).HasDatabaseName("ix_lotes_carga_empresa_tipo_huella");
    }
}
