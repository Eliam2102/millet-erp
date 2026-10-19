using System.Text.Json;
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
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await PrepararSalidasOtrAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }
    // Toda mutación CE pasa por SaveChanges; filas de salida y datos de negocio se confirman juntas.
    private async Task PrepararSalidasOtrAsync(CancellationToken ct)
    {
        ChangeTracker.DetectChanges();
        var cambios = new Dictionary<Guid, HashSet<string>>();
        void Agregar(Guid id, string tipo)
        { if (!cambios.TryGetValue(id, out var tipos)) cambios[id] = tipos = []; tipos.Add(tipo); }
        foreach (var vinculo in ChangeTracker.Entries<OtrEnvio>().Where(x => x.Entity.ExpedienteId != null &&
            (x.State == EntityState.Added || x.Property(e => e.ExpedienteId).IsModified)))
            Agregar(vinculo.Entity.ExpedienteId!.Value, "hito");
        foreach (var h in ChangeTracker.Entries<HitoExpediente>().Where(x => x.State is EntityState.Modified or EntityState.Added))
            if (h.Property(x => x.FechaReal).IsModified || h.Property(x => x.Omitido).IsModified || (h.State == EntityState.Added && h.Entity.FechaReal != null))
            { Agregar(h.Entity.ExpedienteId, "hito"); if (h.Entity.Clave is "despacho-aduanal" or "liberado" or "entregado" or "cerrado") Agregar(h.Entity.ExpedienteId, h.Entity.Clave); }
        foreach (var c in ChangeTracker.Entries<ContenedorExpediente>().Where(x => x.State is EntityState.Modified or EntityState.Added))
        {
            if (c.Property(x => x.Booking).IsModified || (c.State == EntityState.Added && c.Entity.Booking != null)) Agregar(c.Entity.ExpedienteId, c.Entity.Booking == null ? "booking-cancelado" : "booking");
            if (c.Property(x => x.FechaEmbarqueReal).IsModified) Agregar(c.Entity.ExpedienteId, "embarque");
            if (c.Property(x => x.Estado).IsModified) Agregar(c.Entity.ExpedienteId, "contenedor-cancelado");
        }
        foreach (var e in ChangeTracker.Entries<Expediente>().Where(x => x.State == EntityState.Modified && x.Property(e => e.Estado).IsModified)) Agregar(e.Entity.Id, "estado-expediente");
        foreach (var (id, tipos) in cambios)
        {
            var e = ChangeTracker.Entries<Expediente>().SingleOrDefault(x => x.Entity.Id == id)?.Entity;
            if (e is null || e.Tipo != TipoExpediente.Exportacion) continue;
            var envios = await OtrEnvios.Where(x => x.ExpedienteId == id && x.EmpresaId == e.EmpresaId).ToListAsync(ct);
            envios = envios.Concat(OtrEnvios.Local.Where(x => x.ExpedienteId == id)).DistinctBy(x => x.Id).ToList();
            foreach (var envio in envios)
            {
                var valor = JsonSerializer.Serialize(new { expedienteId = e.Id, estado = e.Estado.ToString(),
                    hito = e.Hitos.Where(h => h.FechaReal != null || h.Omitido).OrderByDescending(h => h.Orden).Select(h => h.Clave).FirstOrDefault(),
                    hitos = e.Hitos.Where(h => h.FechaReal != null).Select(h => new { h.Clave, h.FechaReal }),
                    contenedor = e.Contenedores.Where(c => c.Id == envio.ContenedorId).Select(c => new { c.Booking, c.FechaEmbarqueReal, estado = c.Estado.ToString() }) });
                foreach (var tipo in tipos)
                {
                    var persistida = await OtrSalidas.Where(s => s.OtrEnvioId == envio.Id && s.Tipo == tipo).Select(s => (int?)s.VersionErp).MaxAsync(ct) ?? 0;
                    var local = OtrSalidas.Local.Where(s => s.OtrEnvioId == envio.Id && s.Tipo == tipo).Select(s => s.VersionErp).DefaultIfEmpty(0).Max();
                    OtrSalidas.Add(envio.CrearSalida(tipo, Math.Max(persistida, local) + 1, valor, OtrSalidas.Local));
                }
            }
        }
    }
}
