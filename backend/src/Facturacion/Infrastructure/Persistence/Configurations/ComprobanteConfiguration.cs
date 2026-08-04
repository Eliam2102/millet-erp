using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de la base <see cref="Comprobante"/> (F1-PR1). Tabla
/// base <c>facturacion.comprobante</c> de la jerarquía <b>TPT</b> (table-per-type):
/// concentra las columnas comunes; cada subtipo (<c>factura_venta</c>, …) vive
/// en su propia tabla con PK = FK 1:1 a ésta. EF infiere TPT porque cada tipo
/// mapea a una tabla distinta vía <c>ToTable</c>.
///
/// <para>Índices del §5.1 diseño: <c>uuid</c> único parcial, <c>(sucursal, folio)</c>
/// único, <c>estado</c>, <c>(empresa, periodo)</c>.</para>
/// </summary>
public sealed class ComprobanteConfiguration : IEntityTypeConfiguration<Comprobante>
{
    public void Configure(EntityTypeBuilder<Comprobante> builder)
    {
        builder.ToTable("comprobante");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();

        builder.Property(e => e.Folio).HasMaxLength(40).IsRequired();
        builder.Property(e => e.FolioNumero).IsRequired();

        builder.Property(e => e.SucursalId).IsRequired();

        // Lift-up de canal ([Decisión 12-D], CAJAS-PR2): la columna smallint
        // conserva el nombre que tenía en factura_venta; NULL = sin canal
        // (históricos pre-caja) → bucket "Sin asignar" de la Capa A.
        builder.Property(e => e.CanalVentaId).HasColumnName("canal_venta");

        builder.Property(e => e.CajaId);
        builder.Property(e => e.UsuarioEmisorId);

        // Receptor (snapshot al emitir).
        builder.Property(e => e.ReceptorRfc).HasMaxLength(13).IsRequired();
        builder.Property(e => e.ReceptorNombre).HasMaxLength(254).IsRequired();
        builder.Property(e => e.ReceptorRegimenFiscal).HasMaxLength(5);
        builder.Property(e => e.ReceptorCodigoPostal).HasMaxLength(10);
        builder.Property(e => e.ReceptorUsoCfdi).HasMaxLength(5);
        builder.Property(e => e.ReceptorPais).HasMaxLength(5).IsRequired();
        builder.Property(e => e.ReceptorEsGenerico).IsRequired();

        // Emisor (snapshot al emitir, F12-PR1). NOT NULL con default '' para
        // que los comprobantes históricos (timbre stub) no requieran backfill.
        builder.Property(e => e.RfcEmisor).HasMaxLength(13).IsRequired();
        builder.Property(e => e.RegimenFiscalEmisor).HasMaxLength(5);
        builder.Property(e => e.NombreEmisor).HasMaxLength(254).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(e => e.LugarExpedicion).HasMaxLength(5).IsRequired().HasDefaultValue(string.Empty);

        // Datos de pago.
        builder.Property(e => e.MetodoPago).HasMaxLength(5).IsRequired();
        builder.Property(e => e.FormaPago).HasMaxLength(5).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        // Totales (decimal(18,2) para totales — §1 cuidados-infra).
        builder.Property(e => e.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Descuento).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        builder.Property(e => e.PeriodoAnio).IsRequired();
        builder.Property(e => e.PeriodoMes).IsRequired();

        // Datos del timbre (null hasta Timbrado). Sellos son base64 largos → text.
        builder.Property(e => e.Uuid).HasMaxLength(36);
        builder.Property(e => e.SelloCfdi).HasColumnType("text");
        builder.Property(e => e.SelloSat).HasColumnType("text");
        builder.Property(e => e.NoCertificadoSat).HasMaxLength(20);
        builder.Property(e => e.FechaTimbrado);
        builder.Property(e => e.RfcProveedorCertificacion).HasMaxLength(13);
        builder.Property(e => e.CfdiArchivoId);

        // Folio asignado por el PAC (atributo Folio del XML; FiscalAPI lo
        // calcula por RFC emisor — el folio interno de Millet no viaja).
        // Ej. "FUNK671228PH6-50" ⇒ 40 chars da holgura.
        builder.Property(e => e.FolioPac).HasMaxLength(40);

        builder.Property(e => e.TimbradoErrorCodigo).HasMaxLength(50);
        builder.Property(e => e.TimbradoErrorMensaje).HasMaxLength(500);

        builder.Property(e => e.EnviadoCorreo).IsRequired();

        // Relaciones CFDI (hijas; nodo CfdiRelacionados). F4-PR2.
        builder.HasMany(e => e.Relaciones)
            .WithOne()
            .HasForeignKey(r => r.ComprobanteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Índices §5.1.
        builder.HasIndex(e => e.Uuid)
            .IsUnique()
            .HasDatabaseName("ix_comprobante_uuid")
            .HasFilter("uuid IS NOT NULL");

        builder.HasIndex(e => new { e.SucursalId, e.Folio })
            .IsUnique()
            .HasDatabaseName("ix_comprobante_sucursal_folio");

        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_comprobante_estado");

        builder.HasIndex(e => new { e.EmpresaId, e.PeriodoAnio, e.PeriodoMes })
            .HasDatabaseName("ix_comprobante_periodo");

        // Capa A de Cajas: las bandejas filtran por pares (sucursal, canal).
        builder.HasIndex(e => new { e.SucursalId, e.CanalVentaId })
            .HasDatabaseName("ix_comprobante_sucursal_canal");
    }
}
