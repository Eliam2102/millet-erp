using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Movimientos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="MovimientoInventario"/> (F2-PR1).
/// Tabla polimórfica <c>almacen.movimientos_inventario</c> con
/// discriminador <c>tipo</c>. Incluye todas las columnas del §5.1
/// (oc_id, rq_id, vale_blob_ref, etc.) como nullables — cada flujo
/// posterior (F2-PR2 recepción, F4-PR1 salida, F6-PR1 devolución a
/// proveedor) las activa en sus handlers.
///
/// <para>
/// <b>Índices del §5.2</b>: parciales para bandejas (recepciones del
/// día, vales pendientes, conteos activos) + por OC / RQ.
/// </para>
/// </summary>
public sealed class MovimientoInventarioConfiguration : IEntityTypeConfiguration<MovimientoInventario>
{
    public void Configure(EntityTypeBuilder<MovimientoInventario> builder)
    {
        builder.ToTable("movimientos_inventario", t =>
        {
            t.HasCheckConstraint("ck_movimientos_tipo",
                "tipo BETWEEN 0 AND 9");
            t.HasCheckConstraint("ck_movimientos_estado",
                "estado BETWEEN 0 AND 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Folio).HasMaxLength(30);
        builder.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        // PR6a: SubAlmacenId salió de la cabecera; se deriva de la línea vía la
        // vista almacen.v_movimiento_sub_almacen. Ya no hay propiedad ni columna.
        // PR4: helper de cabecera (bin N4 que el almacenista auto-aplicó a las
        // líneas). Nullable — NULL significa "no usó el helper". Dato de
        // reportería; la ubicación que manda para saldos es la de cada línea.
        builder.Property(x => x.UbicacionHelperId);
        builder.Property(x => x.FechaMovimiento).IsRequired();
        builder.Property(x => x.FechaRegistro).IsRequired();
        builder.Property(x => x.EmpresaId).IsRequired();

        // Vinculaciones cross-módulo (todas nullables; las activan los flujos específicos).
        builder.Property(x => x.OcId);
        builder.Property(x => x.OcLineaId);
        builder.Property(x => x.FacturaId);
        builder.Property(x => x.CfdiRecibidoId);
        builder.Property(x => x.CfdiUuidFiscal).HasMaxLength(36);
        builder.Property(x => x.PackingListBlobRef).HasMaxLength(500);
        builder.Property(x => x.RqId);
        builder.Property(x => x.ValeBlobRef).HasMaxLength(500);
        builder.Property(x => x.SalidaOrigenId);
        builder.Property(x => x.RecepcionOrigenId);
        builder.Property(x => x.ConteoId);
        builder.Property(x => x.ProveedorId);

        builder.Property(x => x.PendienteRegularizacion).IsRequired();
        builder.Property(x => x.FechaLimiteRegularizacion);
        builder.Property(x => x.RqRegularizadoraId);

        builder.Property(x => x.PersonaDestinatariaId);
        builder.Property(x => x.ComentarioLibre);
        builder.Property(x => x.Motivo).HasMaxLength(500);
        builder.Property(x => x.EstadoMaterial).HasMaxLength(50);

        builder.Property(x => x.FacturaIdOrigenDiff);
        builder.Property(x => x.RegistradoPor);
        builder.Property(x => x.RegistradoAt);

        // UNIQUE folio (sólo cuando NOT NULL — usamos filtro parcial).
        builder.HasIndex(x => x.Folio)
            .IsUnique()
            .HasDatabaseName("ux_movimientos_folio")
            .HasFilter("folio IS NOT NULL");

        // Bandeja "Recepciones del día". PR6a: el sub-almacén salió de la
        // cabecera; el filtro por sub lo hace ahora el lector vía la vista, así
        // que el índice conserva sólo la fecha (mismo nombre y filtro parcial).
        builder.HasIndex(x => x.FechaMovimiento)
            .HasDatabaseName("ix_movimientos_recepciones")
            .HasFilter("tipo = 0 AND estado = 2");
        // tipo 0 = EntradaCompra; estado 2 = Registrado

        // Bandeja "Vales pendientes de regularización" (A14).
        builder.HasIndex(x => x.FechaLimiteRegularizacion)
            .HasDatabaseName("ix_movimientos_vale_pendientes")
            .HasFilter("tipo = 2 AND pendiente_regularizacion = true");
        // tipo 2 = SalidaPorVale

        // Movimientos por OC (sincronía con Compras).
        builder.HasIndex(x => new { x.OcId, x.Tipo })
            .HasDatabaseName("ix_movimientos_por_oc")
            .HasFilter("oc_id IS NOT NULL");

        // Recepciones variante A con folio fiscal capturado a mano, aún sin
        // CfdiRecibidoId — el enlace diferido (cuando CxP procesa el XML)
        // busca por UUID exacto sobre este parcial.
        builder.HasIndex(x => x.CfdiUuidFiscal)
            .HasDatabaseName("ix_movimientos_cfdi_uuid_pendiente")
            .HasFilter("cfdi_uuid_fiscal IS NOT NULL AND cfdi_recibido_id IS NULL");

        // Salidas por RQ (sincronía con Compras Requisiciones).
        builder.HasIndex(x => new { x.RqId, x.Tipo })
            .HasDatabaseName("ix_movimientos_por_rq")
            .HasFilter("rq_id IS NOT NULL");

        // PR6a: la FK a almacen.sub_almacenes se retiró con la columna de
        // cabecera. La integridad del sub-almacén vive ahora en la FK de la
        // ubicación de cada línea (lineas_movimiento.ubicacion_id → ubicaciones).

        // PR4: FK física del helper a almacen.ubicaciones (mismo patrón que la
        // ubicación de la línea). Restrict: no se borra un bin referenciado.
        builder.HasOne<Almacen.Domain.Catalogo.Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionHelperId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hijos: líneas.
        builder.HasMany(x => x.Lineas)
            .WithOne()
            .HasForeignKey(l => l.MovimientoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
