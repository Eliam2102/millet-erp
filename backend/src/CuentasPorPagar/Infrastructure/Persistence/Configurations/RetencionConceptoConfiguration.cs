using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Catalogos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class RetencionConceptoConfiguration : IEntityTypeConfiguration<RetencionConcepto>
{
    public void Configure(EntityTypeBuilder<RetencionConcepto> builder)
    {
        builder.ToTable("retenciones_concepto"); builder.HasKey(r => r.Id); builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Concepto).HasMaxLength(80); builder.Property(r => r.Descripcion).HasMaxLength(300);
        builder.Property(r => r.Impuesto).HasMaxLength(3); builder.Property(r => r.Tasa).HasPrecision(12, 8);
        builder.Property(r => r.Fuente).HasMaxLength(1000); builder.Property(r => r.MotivoCambio).HasMaxLength(500);
        builder.HasIndex(r => new { r.Concepto, r.Impuesto, r.Tasa }).IsUnique().HasFilter("deleted_at IS NULL");
        var fecha = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        var reglas = new[] {
            ("HONORARIOS_PF", "Honorarios de persona física a persona moral", "001", 0.10m, "https://wwwmat.sat.gob.mx/ordenamiento/18355/ley-del-impuesto-sobre-la-renta"),
            ("HONORARIOS_PF", "IVA: dos terceras partes a tasa general de 16 %", "002", 0.10666667m, "https://www.sat.gob.mx/minisitio/Factura/documentos/honorarios_servicios_contables.pdf"),
            ("ARRENDAMIENTO_PF", "Arrendamiento de persona física a persona moral", "001", 0.10m, "https://wwwmat.sat.gob.mx/ordenamiento/18355/ley-del-impuesto-sobre-la-renta"),
            ("ARRENDAMIENTO_PF", "IVA: dos terceras partes a tasa general de 16 %", "002", 0.10666667m, "https://www.sat.gob.mx/minisitio/Factura/documentos/arrendamiento_local_comercial.pdf"),
            ("FLETES", "Autotransporte terrestre de bienes recibido por persona moral", "002", 0.04m, "https://www.sat.gob.mx/cs/Satellite?blobcol=urldata&blobkey=id&blobtable=MungoBlobs&blobwhere=1461175803212&ssbinary=true"),
            ("RESICO_PF", "Pagos de persona moral a persona física RESICO; revisar excepciones", "001", 0.0125m, "https://wwwmat.sat.gob.mx/articulo/59511/articulo-113-j")
        };
        for (var i = 0; i < reglas.Length; i++)
        {
            var r = reglas[i];
            builder.HasData(new { Id = Guid.Parse($"00000007-000a-0000-0000-{i+1:000000000000}"),
                Concepto = r.Item1, Descripcion = r.Item2, Impuesto = r.Item3, Tasa = r.Item4, Fuente = r.Item5,
                Activa = true, MotivoCambio = RetencionConcepto.AvisoFiscal, CreatedAt = fecha, UpdatedAt = fecha,
                CreatedBy = "seed-P8", UpdatedBy = "seed-P8", Version = 1 });
        }
    }
}
