using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
namespace Millet.ActivosFijos.Application;
public static class NumeroActivo
{
    public static async Task<string> ReservarAsync(ActivosFijosDbContext db, ClaseActivo clase, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Reserve la serie dentro de la transacción del activo.");
        string numero;
        do
        {
            var valores = await db.Database.SqlQuery<int>($"""
                INSERT INTO activos_fijos.secuencias (empresa_id, clase_id, ultimo_numero)
                VALUES ({clase.EmpresaId}, {clase.Id}, 1)
                ON CONFLICT (empresa_id, clase_id) DO UPDATE SET ultimo_numero = secuencias.ultimo_numero + 1
                RETURNING ultimo_numero AS "Value"
                """).ToListAsync(ct);
            numero = string.Create(CultureInfo.InvariantCulture, $"AF-{clase.Clave}-{valores[0]:D6}");
        } while (await db.Activos.AnyAsync(x => x.EmpresaId == clase.EmpresaId && x.Numero == numero, ct));
        return numero;
    }
}
