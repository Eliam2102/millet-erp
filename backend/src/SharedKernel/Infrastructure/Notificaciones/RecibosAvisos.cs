using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Domain.Notificaciones;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.SharedKernel.Infrastructure.Notificaciones;

public sealed class AvisoLecturaConfiguration : IEntityTypeConfiguration<AvisoLectura>
{
    public void Configure(EntityTypeBuilder<AvisoLectura> builder)
    {
        builder.ToTable("aviso_lectura", "core");
        builder.HasKey(x => new { x.UsuarioId, x.EmpresaId, x.ClaveOcurrencia });
        builder.Property(x => x.ClaveOcurrencia).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PrimeraDeteccionSec).UseIdentityByDefaultColumn();
        builder.HasIndex(x => new { x.UsuarioId, x.EmpresaId, x.PrimeraDeteccionSec });
    }
}

public sealed class RecibosAvisos(CoreDbContext db, IClock clock) : IRecibosAvisos
{
    public async Task<T> ConBloqueoAsync<T>(ContextoAvisos contexto, Func<Task<T>> operacion, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"avisos/{contexto.UsuarioId:D}/{contexto.EmpresaId:D}"));
        var llave = BinaryPrimitives.ReadInt64BigEndian(bytes);
        // La secuencia debe seguir el orden de commit, incluso entre navegadores del mismo usuario.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({llave})", ct);
        var resultado = await operacion();
        await tx.CommitAsync(ct);
        return resultado;
    }

    public async Task<RecibosSincronizados> SincronizarAsync(ContextoAvisos contexto, IReadOnlyList<AvisoUsuario> avisos, CancellationToken ct)
    {
        var claves = avisos.Select(x => x.ClaveOcurrencia).Distinct().ToArray();
        var recibos = await db.AvisosLectura.Where(x => x.UsuarioId == contexto.UsuarioId && x.EmpresaId == contexto.EmpresaId && claves.Contains(x.ClaveOcurrencia))
            .ToDictionaryAsync(x => x.ClaveOcurrencia, ct);
        foreach (var aviso in avisos)
        {
            if (!recibos.TryGetValue(aviso.ClaveOcurrencia, out var recibo))
            {
                recibo = new AvisoLectura(contexto.UsuarioId, contexto.EmpresaId, aviso.ClaveOcurrencia, aviso.LeidoOrigenAt);
                db.AvisosLectura.Add(recibo);
                recibos.Add(aviso.ClaveOcurrencia, recibo);
            }
            else if (aviso.LeidoOrigenAt is { } leido) recibo.MarcarLeido(leido);
        }
        await db.SaveChangesAsync(ct);
        var hasta = await db.AvisosLectura.Where(x => x.UsuarioId == contexto.UsuarioId && x.EmpresaId == contexto.EmpresaId)
            .MaxAsync(x => (long?)x.PrimeraDeteccionSec, ct) ?? 0;
        return new RecibosSincronizados(recibos.ToDictionary(x => x.Key, x => new ReciboAviso(x.Key, x.Value.PrimeraDeteccionSec, x.Value.LeidoAt)), hasta);
    }

    public async Task MarcarLeidoAsync(ContextoAvisos contexto, string clave, CancellationToken ct)
    {
        var recibo = await db.AvisosLectura.SingleOrDefaultAsync(x => x.UsuarioId == contexto.UsuarioId && x.EmpresaId == contexto.EmpresaId && x.ClaveOcurrencia == clave, ct);
        if (recibo is null) return;
        recibo.MarcarLeido(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
