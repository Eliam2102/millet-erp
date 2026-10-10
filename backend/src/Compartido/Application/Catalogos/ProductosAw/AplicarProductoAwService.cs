using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Npgsql;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// Lectura de un producto A+W ya normalizada por el lector (ADM-07, doc
/// integration/06). Los campos "solo alta" (clave unidad SAT sugerida,
/// fracción, peso) nunca sobrescriben un producto existente.
/// <see cref="VersionEsperada"/> = <c>ProductoAw.Version</c> leída por quien
/// llama; si ya cambió, el resultado es Conflicto y no se escribe nada.
/// </summary>
public sealed record AplicarProductoAwSnapshot(
    string ReferenciaExterna,
    string Descripcion,
    string? UnidadCruda,
    bool Baja,
    IReadOnlyList<ProductoAwVarianteDato> Variantes,
    DateTime LeidoEnUtc,
    string VersionContrato,
    string VersionMapeo,
    DateTime? TransaccionOrigenUtc = null,
    Guid? EjecucionId = null,
    int? VersionEsperada = null,
    // Solo alta.
    string? ClaveUnidadSatSugerida = null,
    string? FraccionArancelaria = null,
    decimal? PesoUnitarioKg = null,
    // Auto-provisión de pedidos: si existe no se toca nada y una unidad fuera de catálogo no bloquea el alta.
    bool SoloCrear = false,
    // Dueño A+W. Componentes null = no tocar el árbol (rutas que no lo leen); lista vacía = A+W ya no tiene piezas.
    string? CodigoModelo = null,
    string? Grupo = null,
    string? Tipo = null,
    IReadOnlyList<ProductoAwComponenteDato>? Componentes = null,
    string? Wgr = null,
    string? WgrDescripcion = null,
    // Fiscales de la regla por tipo: al crear, y en un producto existente solo si el campo local está vacío.
    string? ClaveProdServSat = null,
    string? ObjetoImp = null,
    decimal? TasaIvaTraslado = null,
    string? UnidadAduana = null);

/// <summary><c>NoAplicado</c>: unidad sin equivalencia, no se creó/actualizó el producto (ver <c>Causa</c>).</summary>
public enum AplicarProductoAwAccion { Creado, Actualizado, SinCambios, Conflicto, NoAplicado }

public sealed record AplicarProductoAwResultado(
    ProductoAw? Producto,
    AplicarProductoAwAccion Accion,
    ResultadoSincronizacionAw Resultado,
    string? Causa = null);

/// <summary>
/// Único punto de escritura de la sincronización de productos A+W: busca por
/// <c>ReferenciaExterna</c>, crea producto + variantes + registro de origen
/// en una transacción, o actualiza descripción/unidad/variantes/baja. Nunca
/// toca claves SAT, tasas ni datos de aduana de un producto existente.
/// </summary>
public sealed class AplicarProductoAwService
{
    /// <summary>Versión de la normalización previa al hash; subirla cambia todos los hashes.</summary>
    public const string VersionNormalizacionHash = "p3";
    public const string CausaUnidadSinEquivalencia = "UNIDAD_SIN_EQUIVALENCIA";

    private const string PgUniqueViolation = "23505";

    private readonly CompartidoDbContext _db;

    public AplicarProductoAwService(CompartidoDbContext db) => _db = db;

    public async Task<AplicarProductoAwResultado> AplicarAsync(
        AplicarProductoAwSnapshot snap, CancellationToken ct)
    {
        var reintentoConcurrencia = false;
        for (var intento = 0; ; intento++)
        {
            var producto = await _db.ProductosAw.Include(p => p.Variantes).Include(p => p.Componentes)
                .FirstOrDefaultAsync(p => p.ReferenciaExterna == snap.ReferenciaExterna, ct);
            if (producto is not null)
            {
                try
                {
                    return await ActualizarAsync(producto, snap, ct);
                }
                catch (DbUpdateConcurrencyException) when (!reintentoConcurrencia)
                {
                    // Otro escritor ganó: soltar lo rastreado, releer y reevaluar (un solo reintento).
                    reintentoConcurrencia = true;
                    _db.ChangeTracker.Clear();
                    continue;
                }
            }

            var unidad = await ResolverUnidadAsync(snap.UnidadCruda, ct);
            // Sin equivalencia no se persiste nada inválido (doc 06 §6); la auto-provisión conserva el snapshot string.
            if (unidad is null && !snap.SoloCrear)
                return new(null, AplicarProductoAwAccion.NoAplicado, ResultadoSincronizacionAw.Pendiente, CausaUnidadSinEquivalencia);

            var nuevo = new ProductoAw(
                id: Guid.CreateVersion7(),
                referenciaExterna: snap.ReferenciaExterna,
                descripcion: snap.Descripcion,
                unidadMedida: unidad?.Codigo ?? snap.UnidadCruda!.Trim(),
                origen: OrigenMaster.Aw,
                unidadMedidaId: unidad?.Id,
                claveProdServSat: snap.ClaveProdServSat,
                claveUnidadSat: snap.ClaveUnidadSatSugerida,
                objetoImp: snap.ObjetoImp,
                tasaIvaTraslado: snap.TasaIvaTraslado,
                fraccionArancelaria: snap.FraccionArancelaria,
                unidadAduana: snap.UnidadAduana,
                pesoUnitarioKg: snap.PesoUnitarioKg);
            nuevo.AplicarVariantes(snap.Variantes);
            nuevo.AplicarClasificacion(snap.CodigoModelo, snap.Grupo, snap.Tipo, snap.Wgr, snap.WgrDescripcion);
            if (snap.Componentes is not null) nuevo.ReemplazarComponentes(snap.Componentes);
            if (snap.Baja) nuevo.DarDeBaja(DateTime.UtcNow);

            var registro = new ProductoSincronizacionAw(Guid.CreateVersion7(), nuevo.Id, snap.ReferenciaExterna);
            AplicarRegistro(registro, snap, CalcularHash(snap), ResultadoSincronizacionAw.Aplicado);
            _db.ProductosAw.Add(nuevo);
            _db.ProductosSincronizacionAw.Add(registro);
            try
            {
                await _db.SaveChangesAsync(ct);
                return new(nuevo, AplicarProductoAwAccion.Creado, ResultadoSincronizacionAw.Aplicado);
            }
            catch (DbUpdateException ex) when (intento == 0
                && ex.InnerException is PostgresException { SqlState: PgUniqueViolation })
            {
                // Carrera de alta por la misma referencia: recargar al ganador y aplicar como actualización.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<AplicarProductoAwResultado> ActualizarAsync(
        ProductoAw producto, AplicarProductoAwSnapshot snap, CancellationToken ct)
    {
        // Un producto manual con la misma referencia no se apropia.
        if (producto.Origen != OrigenMaster.Aw)
            return new(producto, AplicarProductoAwAccion.Conflicto, ResultadoSincronizacionAw.Conflicto);
        if (snap.SoloCrear)
            return new(producto, AplicarProductoAwAccion.SinCambios, ResultadoSincronizacionAw.SinCambios);
        // Edición manual o escritor con versión vieja: no se sobrescribe en silencio (doc 06 §8).
        if (snap.VersionEsperada is { } esperada && esperada != producto.Version)
            return new(producto, AplicarProductoAwAccion.Conflicto, ResultadoSincronizacionAw.Conflicto);

        var registro = await _db.ProductosSincronizacionAw.FirstOrDefaultAsync(r => r.ProductoAwId == producto.Id, ct);
        var hash = CalcularHash(snap);

        // Lectura atrasada (fuera de orden): la más reciente ya aplicada es la vigente.
        if (registro is not null && snap.LeidoEnUtc < registro.LeidoEnUtc)
            return new(producto, AplicarProductoAwAccion.SinCambios, registro.Resultado);

        if (registro is not null && registro.HashOrigen == hash && registro.VersionMapeo == snap.VersionMapeo
            && registro.Resultado is ResultadoSincronizacionAw.Aplicado or ResultadoSincronizacionAw.SinCambios)
        {
            registro.RegistrarComprobacion(snap.LeidoEnUtc, snap.EjecucionId);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // El latido es best-effort: otro escritor ya actualizó el registro.
                foreach (var e in ex.Entries) e.State = EntityState.Detached;
            }
            return new(producto, AplicarProductoAwAccion.SinCambios, registro.Resultado);
        }

        if (registro is null)
        {
            registro = new ProductoSincronizacionAw(Guid.CreateVersion7(), producto.Id, snap.ReferenciaExterna);
            _db.ProductosSincronizacionAw.Add(registro);
        }

        var unidad = await ResolverUnidadAsync(snap.UnidadCruda, ct);
        if (unidad is null)
        {
            // El producto queda intacto; solo el registro de origen refleja la causa.
            AplicarRegistro(registro, snap, hash, ResultadoSincronizacionAw.Pendiente,
                error: $"{CausaUnidadSinEquivalencia}: '{snap.UnidadCruda}' no existe en el catálogo de unidades.");
            await _db.SaveChangesAsync(ct);
            return new(producto, AplicarProductoAwAccion.NoAplicado, ResultadoSincronizacionAw.Pendiente, CausaUnidadSinEquivalencia);
        }

        var diferencias = Diferencias(producto, snap, registro);
        producto.ActualizarDatos(descripcion: snap.Descripcion);
        if (producto.UnidadMedidaId != unidad.Id || producto.UnidadMedida != unidad.Codigo)
            producto.AsignarUnidadMedida(unidad.Id, unidad.Codigo);
        producto.AplicarVariantes(snap.Variantes);
        // Las variantes nuevas llevan Id asignado: sin esto EF podría tratarlas como Modified.
        foreach (var v in producto.Variantes)
            if (_db.Entry(v).State == EntityState.Detached) _db.Entry(v).State = EntityState.Added;
        producto.AplicarClasificacion(snap.CodigoModelo, snap.Grupo, snap.Tipo, snap.Wgr, snap.WgrDescripcion);
        if (snap.Componentes is not null)
        {
            producto.ReemplazarComponentes(snap.Componentes);
            foreach (var c in producto.Componentes)
                if (_db.Entry(c).State == EntityState.Detached) _db.Entry(c).State = EntityState.Added;
        }

        // Fiscales de la regla: solo lo que el producto aún no tiene; nunca pisa lo capturado por el operador.
        producto.AsignarDatosFiscales(
            claveProdServSat: producto.ClaveProdServSat is null ? Vacio(snap.ClaveProdServSat) : null,
            claveUnidadSat: producto.ClaveUnidadSat is null ? Vacio(snap.ClaveUnidadSatSugerida) : null,
            objetoImp: producto.ObjetoImp is null ? Vacio(snap.ObjetoImp) : null,
            tasaIvaTraslado: producto.TasaIvaTraslado is null ? snap.TasaIvaTraslado : null);
        producto.AsignarDatosAduana(
            fraccionArancelaria: producto.FraccionArancelaria is null ? Vacio(snap.FraccionArancelaria) : null,
            unidadAduana: producto.UnidadAduana is null ? Vacio(snap.UnidadAduana) : null);

        if (snap.Baja)
            producto.DarDeBaja(DateTime.UtcNow);
        else if (producto.Estatus == EstatusCatalogo.Inactivo && registro.BajaOrigenCruda == "1")
            producto.Reactivar(); // solo revierte una baja que vino de A+W; una baja local se respeta

        AplicarRegistro(registro, snap, hash, ResultadoSincronizacionAw.Aplicado,
            diferencias: DiferenciaAplicacionAw.Serializar(diferencias));
        await _db.SaveChangesAsync(ct);
        return new(producto, AplicarProductoAwAccion.Actualizado, ResultadoSincronizacionAw.Aplicado);
    }

    /// <summary>Unidad del catálogo por código (como Provisionar); null = sin equivalencia.</summary>
    private async Task<UnidadMedida?> ResolverUnidadAsync(string? cruda, CancellationToken ct)
    {
        var codigo = cruda?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(codigo)) return null;
        return await _db.UnidadesMedida.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Codigo == codigo && u.Estatus == EstatusCatalogo.Activo, ct);
    }

    private static void AplicarRegistro(
        ProductoSincronizacionAw r, AplicarProductoAwSnapshot s, string hash, ResultadoSincronizacionAw resultado,
        string? error = null, string? diferencias = null) =>
        r.Aplicar(s.Descripcion, s.UnidadCruda, s.Baja ? "1" : "0", s.TransaccionOrigenUtc,
            s.EjecucionId, s.LeidoEnUtc, DateTime.UtcNow,
            hash, s.VersionContrato, s.VersionMapeo, resultado, error, diferencias);

    /// <summary>Lo recibido que la política deja fuera del producto existente (se ve en el detalle de origen).</summary>
    private static List<DiferenciaAplicacionAw> Diferencias(
        ProductoAw p, AplicarProductoAwSnapshot s, ProductoSincronizacionAw registro)
    {
        var d = new List<DiferenciaAplicacionAw>();
        void Dif(string campo, string? recibido, string? local, string motivo)
        {
            if (recibido is not null && local is not null
                && !string.Equals(recibido, local, StringComparison.OrdinalIgnoreCase))
                d.Add(new(campo, recibido, local, motivo));
        }
        Dif("Clave prod/serv SAT", Vacio(s.ClaveProdServSat), p.ClaveProdServSat, "Lo completado por el operador no se sobrescribe.");
        Dif("Clave unidad SAT", Vacio(s.ClaveUnidadSatSugerida), p.ClaveUnidadSat, "Lo completado por el operador no se sobrescribe.");
        Dif("Fracción arancelaria", Vacio(s.FraccionArancelaria), p.FraccionArancelaria, "Lo completado por el operador no se sobrescribe.");
        Dif("Peso unitario (kg)", s.PesoUnitarioKg?.ToString(CultureInfo.InvariantCulture),
            p.PesoUnitarioKg?.ToString(CultureInfo.InvariantCulture), "Lo completado por el operador no se sobrescribe.");
        if (!s.Baja && p.Estatus == EstatusCatalogo.Inactivo && registro.BajaOrigenCruda != "1")
            d.Add(new("Estatus", "Activo", "Inactivo", "La baja local no se revierte desde A+W."));
        return d;
    }

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>
    /// SHA-256 (hex) de lo consumido: descripción, unidad, baja, variantes
    /// ordenadas por clave, clasificación y componentes ordenados por orden. Prefijo de longitud, nulo = "~" (nulo != 0) y números
    /// invariantes. Excluye control (ejecución, lecturas, versión esperada).
    /// Cambiar algo exige subir <see cref="VersionNormalizacionHash"/> y VersionMapeo.
    /// </summary>
    internal static string CalcularHash(AplicarProductoAwSnapshot s)
    {
        var sb = new StringBuilder(VersionNormalizacionHash);
        void T(string? v)
        {
            v = Vacio(v);
            sb.Append('|').Append(v is null ? "~" : $"{v.Length}:{v}");
        }
        void N(decimal? v) => sb.Append('|').Append(v is null ? "~" : v.Value.ToString("0.###", CultureInfo.InvariantCulture));

        T(s.Descripcion);
        T(s.UnidadCruda?.ToUpperInvariant());
        sb.Append('|').Append(s.Baja ? '1' : '0');
        foreach (var v in s.Variantes.OrderBy(v => v.ClaveVariante, StringComparer.Ordinal))
        {
            T(v.ClaveVariante); N(v.AltoMm); N(v.AnchoMm); N(v.EspesorMm); T(v.Composicion);
        }
        T(s.CodigoModelo); T(s.Grupo); T(s.Tipo); T(s.Wgr); T(s.WgrDescripcion);
        T(s.ClaveProdServSat); T(s.ClaveUnidadSatSugerida); T(s.ObjetoImp); N(s.TasaIvaTraslado); T(s.FraccionArancelaria); T(s.UnidadAduana);
        // Componentes null (no se leen) y lista vacía comparten hash: ambos dejan el árbol sin hijos en el hash.
        foreach (var c in (s.Componentes ?? []).OrderBy(c => c.Orden))
        {
            sb.Append('|').Append(c.Orden).Append('|').Append(c.Nivel).Append('|').Append(c.PadreOrden?.ToString(CultureInfo.InvariantCulture) ?? "~");
            T(c.ComponenteRef); T(c.Descripcion); T(c.Tipo); N(c.EspesorMm);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
