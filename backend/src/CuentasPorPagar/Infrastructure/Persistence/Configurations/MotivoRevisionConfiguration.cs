using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="MotivoRevision"/> + seed
/// inicial con los 14 motivos del §5.3 del 00-levantamiento. SLA
/// único de 5 días excepto "Disputa contractual" (15) e "Indicación
/// expresa" (NULL).
/// </summary>
public sealed class MotivoRevisionConfiguration : IEntityTypeConfiguration<MotivoRevision>
{
    public void Configure(EntityTypeBuilder<MotivoRevision> builder)
    {
        builder.ToTable("motivos_revision");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Codigo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(400);
        builder.Property(e => e.SlaDias);
        builder.Property(e => e.DependenciaRevisoraDefaultCodigo).HasMaxLength(40);
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => e.Codigo)
            .HasDatabaseName("ux_motivos_revision_codigo")
            .IsUnique();

        var seedTime = new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(SeedMotivos.Select(m => new
        {
            Id = m.Id,
            Codigo = m.Codigo,
            Nombre = m.Nombre,
            Descripcion = (string?)m.Descripcion,
            SlaDias = (int?)m.SlaDias,
            DependenciaRevisoraDefaultCodigo = (string?)m.Dependencia,
            Activo = true,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        }).ToArray());
    }

    /// <summary>14 motivos canónicos del §5.3. GUIDs deterministas para idempotencia.</summary>
    public static readonly IReadOnlyList<(Guid Id, string Codigo, string Nombre, string Descripcion, int? SlaDias, string Dependencia)> SeedMotivos =
    [
        (Guid.Parse("00000007-1001-0000-0000-000000000001"), "DISCREPANCIA_OC",       "Discrepancia con OC fuera de tolerancia", "Monto factura supera tolerancia del proveedor",                   5,    "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000002"), "DANOS_MERCANCIA",       "Daños o defectos en mercancía",            "Recepción reporta daño físico vía OcRecepcionRegistradaEvent",    5,    "ALMACEN"),
        (Guid.Parse("00000007-1001-0000-0000-000000000003"), "DIFERENCIA_PRECIO",     "Diferencia de precio",                      "Precio de factura diferente al acordado",                          5,    "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000004"), "DEVOLUCION_PENDIENTE",  "Devolución pendiente",                      "Devolución a proveedor abierta sin liquidar (Almacén sub-flujo 8.B)", 5,  "ALMACEN"),
        (Guid.Parse("00000007-1001-0000-0000-000000000005"), "NO_CONFORMIDAD",        "No conformidad con servicio o producto",   "Servicio no cumple especificación",                                5,    "AREA_USUARIA"),
        (Guid.Parse("00000007-1001-0000-0000-000000000006"), "TIEMPO_RESPUESTA",      "Tiempo de respuesta del proveedor",        "Entrega fuera de plazo",                                            5,    "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000007"), "CALIDAD_SERVICIO",      "Calidad de servicio insuficiente",         "Servicio recibido no satisfactorio",                                5,    "AREA_USUARIA"),
        (Guid.Parse("00000007-1001-0000-0000-000000000008"), "GARANTIA_ABIERTA",      "Reclamo de garantía abierto",              "Reclamo no resuelto",                                              5,    "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000009"), "FALTA_REPP",            "Falta complemento de pago + 5 días",        "REPP no emitido pasados 5 días del pago",                          5,    "CXP"),
        (Guid.Parse("00000007-1001-0000-0000-00000000000a"), "DOCUMENTACION_FISCAL", "Documentación fiscal incompleta",          "CFDI con datos inválidos, RFC mal, fechas inconsistentes",          5,    "CXP"),
        (Guid.Parse("00000007-1001-0000-0000-00000000000b"), "DISPUTA_CONTRACTUAL",  "Disputa contractual",                       "Reclamación legal",                                                15,   "DIRECCION_F"),
        (Guid.Parse("00000007-1001-0000-0000-00000000000c"), "FIRMA_PENDIENTE",      "Pendiente firma o autorización",            "Autorización formal pendiente",                                     5,    "DIRECCION_F"),
        (Guid.Parse("00000007-1001-0000-0000-00000000000d"), "INDICACION_EXPRESA",   "Indicación expresa",                        "Bloqueo manual por instrucción (sin SLA — libera el solicitante)",  null, "CXP"),
        (Guid.Parse("00000007-1001-0000-0000-00000000000e"), "OTROS",                "Otros motivos",                             "Caso no clasificable",                                              5,    "CXP"),
    ];
}
