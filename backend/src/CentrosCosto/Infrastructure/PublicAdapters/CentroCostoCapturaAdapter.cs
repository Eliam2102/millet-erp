using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Departamentos;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

public sealed class CentroCostoCapturaAdapter(CentrosCostoDbContext db, IAlcanceDim3Evaluator alcance,
    IDim3ElegibilidadPort elegibilidad) : ICentroCostoCapturaPort
{
    public async Task<CentroCostoCaptura> ObtenerAsync(Guid sucursalId, Guid departamentoId, CancellationToken ct)
    {
        var id = await db.DepartamentoCentrosCosto.AsNoTracking()
            .Where(x => x.SucursalId == sucursalId && x.DepartamentoId == departamentoId)
            .Select(x => (Guid?)x.CentroCostoId).SingleOrDefaultAsync(ct);
        var nodos = await CentroCostoCatalogoLectura.ObtenerAsync(db, ct);
        var heredado = nodos.SingleOrDefault(x => x.Id == id);
        var opciones = await FiltrarAsync(nodos, false, ct);
        var maquinas = opciones.Where(x => x.Nivel == 3).ToArray();
        var unica = opciones.Count == 1 ? opciones[0] : maquinas.Length == 1 ? maquinas[0] : null;
        return new(heredado, opciones.Count > 0, unica,
            heredado is null ? CentroCostoCapturaRegla.SinEquivalencia : !heredado.Activo ? "El centro de costo de tu departamento está inactivo; pídelo a Contabilidad." : null);
    }
    public async Task<Guid> ResolverAsync(Guid sucursalId, Guid departamentoId, Guid? elegido, CancellationToken ct)
    {
        var contexto = await ObtenerAsync(sucursalId, departamentoId, ct);
        if (contexto.Heredado is null && elegido is Guid explicito)
            await ValidarAsync(explicito, true, ct);
        var id = CentroCostoCapturaRegla.Resolver(contexto, elegido);
        await ValidarAsync(id, id != contexto.Heredado?.Id, ct);
        return id;
    }
    private async Task ValidarAsync(Guid id, bool aplicarAlcance, CancellationToken ct)
    {
        var resultado = await elegibilidad.EvaluarAsync(id, aplicarAlcance, ct);
        if (resultado == Dim3Elegibilidad.Valida) return;
        var motivo = resultado switch
        {
            Dim3Elegibilidad.Inactiva => "centro de costo inactivo",
            Dim3Elegibilidad.FueraDeAlcance => "centro de costo fuera de su alcance",
            _ => "el centro de costo no existe",
        };
        throw new BusinessRuleException("CECO_INVALIDO", $"Centro de costo inválido: {motivo}.");
    }
    public async Task<IReadOnlyList<CentroCostoOpcion>> BuscarAsync(string? q, bool abierto, CancellationToken ct)
    {
        var nodos = await FiltrarAsync(await CentroCostoCatalogoLectura.ObtenerAsync(db, ct), abierto, ct);
        return nodos.Where(x => string.IsNullOrWhiteSpace(q) || x.Clave.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Nombre.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Nivel).ThenBy(x => x.Clave).Take(1000).ToArray();
    }
    private async Task<IReadOnlyList<CentroCostoOpcion>> FiltrarAsync(IReadOnlyList<CentroCostoOpcion> nodos, bool abierto, CancellationToken ct)
    {
        if (abierto) return nodos.Where(x => x.Activo).ToArray();
        var a = await alcance.ResolverAsync(ct);
        if (a.EsTotal) return nodos.Where(x => x.Activo).ToArray();
        // El alcance congelado sigue en Dim3. Sólo sus ancestros son centros elegibles;
        // esto no asigna las máquinas hermanas ni expande asignaciones futuras.
        var maquinas = nodos.Where(x => x.Nivel == 3 && x.Activo && a.Dim3Ids.Contains(x.Id)).ToArray();
        return nodos.Where(x => x.Activo && (a.Dim3Ids.Contains(x.Id) ||
            (x.Nivel == 2 && maquinas.Any(m => m.Dim2Id == x.Id)) ||
            (x.Nivel == 1 && maquinas.Any(m => m.Dim1Id == x.Id)))).ToArray();
    }
}
