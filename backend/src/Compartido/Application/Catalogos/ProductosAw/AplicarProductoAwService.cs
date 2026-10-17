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
            await AplicarFichaAsync(nuevo, ct, nuevo: true);
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
        {
            await AplicarFichaAsync(producto, ct);
            await _db.SaveChangesAsync(ct);
            return new(producto, AplicarProductoAwAccion.SinCambios, ResultadoSincronizacionAw.SinCambios);
        }
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
            await AplicarFichaAsync(producto, ct);
            registro.RegistrarComprobacion(snap.LeidoEnUtc, snap.EjecucionId);
            var pendientes = new List<CambioMaestroAw>();
            if (EditadoEnErp(producto, snap, registro)) pendientes.Add(ConflictoDescripcion(producto, snap, hash));
            await AgregarCambiosAsync(pendientes, snap.ReferenciaExterna, hash, ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // El latido es best-effort: otro escritor ya actualizó el registro.
                foreach (var e in ex.Entries) e.State = EntityState.Detached;
                DesacoplarCambios();
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
        var cambios = new List<CambioMaestroAw>();
        var descripcionAntes = producto.Descripcion;
        var unidadAntes = producto.UnidadMedida;
        var medidasAntes = Medidas(producto.Variantes.Select(v => v.ClaveVariante));
        var editadoEnErp = EditadoEnErp(producto, snap, registro);
        if (editadoEnErp)
            cambios.Add(ConflictoDescripcion(producto, snap, hash)); // se conserva el valor del ERP hasta «Aplicar A+W»
        else
            producto.ActualizarDatos(descripcion: snap.Descripcion);
        if (producto.UnidadMedidaId != unidad.Id || producto.UnidadMedida != unidad.Codigo)
            producto.AsignarUnidadMedida(unidad.Id, unidad.Codigo);
        var variantesPrevias = producto.Variantes.Select(v => v.Id).ToHashSet();
        producto.AplicarVariantes(snap.Variantes);
        // Las variantes nuevas llevan Id asignado: EF las detectaría como Modified (UPDATE de 0 filas) si no se marcan Added.
        foreach (var v in producto.Variantes.Where(v => !variantesPrevias.Contains(v.Id)).ToList())
            _db.Entry(v).State = EntityState.Added;
        producto.AplicarClasificacion(snap.CodigoModelo, snap.Grupo, snap.Tipo, snap.Wgr, snap.WgrDescripcion);
        if (snap.Componentes is not null)
        {
            producto.ReemplazarComponentes(snap.Componentes);
            foreach (var c in producto.Componentes)
                if (_db.Entry(c).State == EntityState.Detached) _db.Entry(c).State = EntityState.Added;
        }

        // Compatibilidad #79: fiscales aportados por la configuración solo completan vacíos.
        if (producto.OrigenFiscal != OrigenFiscalProducto.ExcepcionProducto)
            producto.AsignarDatosFiscales(
                claveProdServSat: producto.ClaveProdServSat is null ? Vacio(snap.ClaveProdServSat) : null,
                claveUnidadSat: producto.ClaveUnidadSat is null ? Vacio(snap.ClaveUnidadSatSugerida) : null,
                objetoImp: producto.ObjetoImp is null ? Vacio(snap.ObjetoImp) : null,
                tasaIvaTraslado: producto.TasaIvaTraslado is null ? snap.TasaIvaTraslado : null);
        await AplicarFichaAsync(producto, ct);
        producto.AsignarDatosAduana(
            fraccionArancelaria: producto.FraccionArancelaria is null ? Vacio(snap.FraccionArancelaria) : null,
            unidadAduana: producto.UnidadAduana is null ? Vacio(snap.UnidadAduana) : null);

        var estatusAntes = producto.Estatus;
        if (snap.Baja)
            producto.DarDeBaja(DateTime.UtcNow);
        else if (producto.Estatus == EstatusCatalogo.Inactivo && registro.BajaOrigenCruda == "1")
            producto.Reactivar(); // solo revierte una baja que vino de A+W; una baja local se respeta

        if (!editadoEnErp) AgregarSiCambio(cambios, producto, snap, hash, "Descripción", descripcionAntes, producto.Descripcion);
        AgregarSiCambio(cambios, producto, snap, hash, "Unidad", unidadAntes, producto.UnidadMedida);
        AgregarSiCambio(cambios, producto, snap, hash, "Medidas (variantes)", medidasAntes, Medidas(producto.Variantes.Select(v => v.ClaveVariante)));
        if (producto.Estatus != estatusAntes)
        {
            var baja = producto.Estatus == EstatusCatalogo.Inactivo;
            cambios.Add(NuevoCambio(producto.Id, snap, hash, "Estatus", estatusAntes.ToString(), producto.Estatus.ToString(),
                baja ? TipoCambioMaestroAw.Baja : TipoCambioMaestroAw.Reactivacion, EstadoCambioMaestroAw.Informativo,
                baja ? "A+W dio de baja el producto (KZ_GESPERRT); no se borra."
                     : "A+W levantó una baja que vino de A+W; se reactiva."));
        }

        AplicarRegistro(registro, snap, hash, ResultadoSincronizacionAw.Aplicado,
            diferencias: DiferenciaAplicacionAw.Serializar(diferencias));
        await AgregarCambiosAsync(cambios, snap.ReferenciaExterna, hash, ct);
        await _db.SaveChangesAsync(ct);
        return new(producto, AplicarProductoAwAccion.Actualizado, ResultadoSincronizacionAw.Aplicado);
    }

    private static bool Difiere(string? a, string? b) => !string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);

    /// <summary>El ERP editó la descripción de A+W (difiere de la última leída) y A+W trae otro valor.</summary>
    private static bool EditadoEnErp(ProductoAw p, AplicarProductoAwSnapshot snap, ProductoSincronizacionAw? registro) =>
        registro is not null && registro.DescripcionOrigen is not null
        && Difiere(p.Descripcion, registro.DescripcionOrigen) && Difiere(p.Descripcion, snap.Descripcion);

    private static string Medidas(IEnumerable<string> claves) => string.Join(", ", claves.Order(StringComparer.Ordinal));

    private static CambioMaestroAw NuevoCambio(Guid productoId, AplicarProductoAwSnapshot snap, string hash, string campo,
        string? antes, string? despues, TipoCambioMaestroAw tipo, EstadoCambioMaestroAw estado, string causa) =>
        new(Guid.CreateVersion7(), "ProductoAw", productoId, snap.ReferenciaExterna, campo, antes, despues,
            MatrizCampoDuenoAw.DuenoAw, tipo, estado, causa, hash, MatrizCampoDuenoAw.Version, snap.EjecucionId, DateTime.UtcNow);

    private static CambioMaestroAw ConflictoDescripcion(ProductoAw p, AplicarProductoAwSnapshot snap, string hash) =>
        NuevoCambio(p.Id, snap, hash, "Descripción", p.Descripcion, snap.Descripcion, TipoCambioMaestroAw.EditadoEnErp,
            EstadoCambioMaestroAw.Pendiente, "El campo es de A+W y se cambió en el ERP; se conserva el valor del ERP hasta aplicar A+W.");

    private static void AgregarSiCambio(List<CambioMaestroAw> lista, ProductoAw p, AplicarProductoAwSnapshot snap, string hash,
        string campo, string? antes, string? despues)
    {
        if (Difiere(antes, despues))
            lista.Add(NuevoCambio(p.Id, snap, hash, campo, antes, despues, TipoCambioMaestroAw.Aplicado,
                EstadoCambioMaestroAw.Informativo, "Cambio recibido de A+W."));
    }

    /// <summary>Idempotencia: una sola consulta trae lo ya registrado para esta referencia y versión de evento.</summary>
    private async Task AgregarCambiosAsync(List<CambioMaestroAw> cambios, string referencia, string hash, CancellationToken ct)
    {
        if (cambios.Count == 0) return;
        var existentes = await _db.CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.TipoEntidad == "ProductoAw" && c.ReferenciaExterna == referencia && c.VersionEvento == hash)
            .Select(c => new { c.Campo, c.Tipo }).ToListAsync(ct);
        foreach (var c in cambios.Where(c => !existentes.Any(e => e.Campo == c.Campo && e.Tipo == c.Tipo)))
            _db.Add(c);
    }

    /// <summary>Tras un choque de concurrencia las filas de bitácora pendientes se sueltan para no insertarlas dos veces.</summary>
    private void DesacoplarCambios()
    {
        foreach (var e in _db.ChangeTracker.Entries<CambioMaestroAw>().Where(e => e.State == EntityState.Added).ToList())
            e.State = EntityState.Detached;
    }

    private async Task AplicarFichaAsync(ProductoAw producto, CancellationToken ct, bool nuevo = false)
    {
        if (producto.OrigenFiscal == OrigenFiscalProducto.ExcepcionProducto || producto.ConfirmadoEn.HasValue) return;
        var reglas = await _db.Set<ReglaFiscalAw>().AsNoTracking().Where(r => r.Activa).ToListAsync(ct);
        var regla = reglas.Where(r => r.Coincide(producto)).OrderByDescending(r => r.Campo == "Wgr" ? 3 : r.Campo == "Grupo" ? 2 : 1).FirstOrDefault();
        if (regla is null) return;
        if (producto.ReglaFiscalId == regla.Id && producto.DatosFiscalesCompletos) return;
        var antes = System.Text.Json.JsonSerializer.Serialize(producto.FichaFiscal());
        producto.AplicarReglaFiscal(regla, soloVacios: !nuevo);
        _db.Add(new CambioFiscalAw(Guid.CreateVersion7(), producto.Id, "Producto", "Aplicar regla", antes,
            System.Text.Json.JsonSerializer.Serialize(producto.FichaFiscal()), null));
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
