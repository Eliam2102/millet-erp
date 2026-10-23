using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.DiferenciasFiscales;

/// <summary>Qué comparar. <c>EntidadId</c> acota a un cliente o producto (lo usa "Marcar revisado").</summary>
public sealed record FiltroDiferenciasFiscales(TipoEntidadDiferenciaFiscal? Tipo = null, string? Campo = null, Guid? EntidadId = null);

/// <summary>Diferencias vigentes (sin aplicar revisiones) y lo que no se pudo comparar.</summary>
public sealed record ResultadoComparacionFiscal(
    IReadOnlyList<DiferenciaFiscalDetectada> Diferencias, int ClientesComparados, int ProductosComparados, IReadOnlyList<string> Avisos);

/// <summary>
/// Calcula en vivo las diferencias fiscales A+W vs ERP (U1.8b). Clientes: lee A+W SOLO por el puerto
/// <see cref="IAwClientesOrigen"/> (respeta Real/Demo) y compara contra el maestro vigente; nunca escribe en
/// A+W ni aplica nada al ERP. Productos: A+W no tiene datos SAT, así que se compara contra la regla fiscal por
/// familia (<see cref="ReglaFiscalAw"/>, luego <c>IntegracionesAw:Productos:ReglasFiscales</c>) y el mapeo de
/// unidad (<c>IntegracionesAw:Pedidos:MapeoUnidadSat</c>), con la clasificación y unidad que ya sincronizó ADM-07.
/// No usa las <c>Diferencias</c> guardadas por la sincronización (pueden estar viejas y en Real no traen RFC/CP).
/// </summary>
public sealed class DiferenciasFiscalesAwService
{
    private readonly CompartidoDbContext _db;
    private readonly IAwClientesOrigen _origen;
    private readonly AwClientesOptions _clientes;
    private readonly AwProductosOptions _productos;
    private readonly AwPedidosOptions _pedidos;
    private readonly DiferenciasFiscalesAwOptions _options;
    private readonly ILogger<DiferenciasFiscalesAwService> _logger;

    public DiferenciasFiscalesAwService(
        CompartidoDbContext db,
        IAwClientesOrigen origen,
        IOptions<AwClientesOptions> clientes,
        IOptions<AwProductosOptions> productos,
        IOptions<AwPedidosOptions> pedidos,
        IOptions<DiferenciasFiscalesAwOptions> options,
        ILogger<DiferenciasFiscalesAwService> logger)
    {
        _db = db;
        _origen = origen;
        _clientes = clientes.Value;
        _productos = productos.Value;
        _pedidos = pedidos.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResultadoComparacionFiscal> CompararAsync(FiltroDiferenciasFiscales filtro, CancellationToken ct)
    {
        var diferencias = new List<DiferenciaFiscalDetectada>();
        var avisos = new List<string>();
        var clientes = filtro.Tipo is null or TipoEntidadDiferenciaFiscal.Cliente
            ? await CompararClientesAsync(filtro.EntidadId, diferencias, avisos, ct) : 0;
        var productos = filtro.Tipo is null or TipoEntidadDiferenciaFiscal.Producto
            ? await CompararProductosAsync(filtro.EntidadId, diferencias, ct) : 0;
        var filtradas = filtro.Campo is null ? diferencias : diferencias.Where(d => d.Campo == filtro.Campo).ToList();
        return new(filtradas, clientes, productos, avisos);
    }

    /// <summary>Revisiones vigentes por (tipo, entidad, campo).</summary>
    public async Task<Dictionary<(TipoEntidadDiferenciaFiscal, Guid, string), DiferenciaFiscalAwRevision>> RevisionesAsync(CancellationToken ct) =>
        (await _db.DiferenciasFiscalesAwRevisiones.AsNoTracking().ToListAsync(ct))
            .ToDictionary(r => (r.Tipo, r.EntidadId, r.Campo));

    private async Task<int> CompararClientesAsync(
        Guid? entidadId, List<DiferenciaFiscalDetectada> diferencias, List<string> avisos, CancellationToken ct)
    {
        var q = _db.Clientes.AsNoTracking().Where(c => c.ReferenciaExterna != null && c.Estatus == EstatusCatalogo.Activo);
        if (entidadId is { } id) q = q.Where(c => c.Id == id);
        var clientes = await q
            .Select(c => new ClienteFiscalErp(c.Id, c.ReferenciaExterna!, c.RazonSocial, c.Rfc, c.RegimenFiscal, c.CodigoPostalFiscal))
            .ToListAsync(ct);
        if (clientes.Count == 0) return 0;

        if (!_clientes.LecturaHabilitada)
        {
            avisos.Add("No se compararon clientes: la lectura de clientes de A+W está apagada en este ambiente.");
            return 0;
        }

        Dictionary<string, AwClienteOrigenFila> aw;
        try
        {
            aw = await LeerAwAsync(clientes, ct);
        }
        catch (AwReaderException ex)
        {
            // Solo el tipo de falla: el mensaje del driver puede traer el host.
            _logger.LogWarning("Diferencias fiscales: lectura de clientes A+W falló ({Kind}).", ex.Kind);
            avisos.Add($"No se compararon clientes: A+W no respondió ({ex.Kind}). Vuelve a ejecutar el reporte más tarde.");
            return 0;
        }
        catch (AwClientesSyncException ex)
        {
            avisos.Add("No se compararon clientes: " + ex.Message);
            return 0;
        }
        catch (BusinessRuleException ex) when (ex.Code == "AW_ORIGEN_SIN_CONFIGURAR")
        {
            avisos.Add("No se compararon clientes: " + ex.Message);
            return 0;
        }

        var sinFila = 0;
        foreach (var c in clientes)
        {
            if (aw.TryGetValue(c.Referencia, out var fila)) diferencias.AddRange(ComparadorDiferenciasFiscales.CompararCliente(c, fila));
            else sinFila++;
        }
        if (sinFila > 0)
            avisos.Add($"{sinFila} cliente(s) del ERP con clave de A+W no aparecen en A+W; no se compararon.");
        return clientes.Count - sinFila;
    }

    private async Task<Dictionary<string, AwClienteOrigenFila>> LeerAwAsync(List<ClienteFiscalErp> clientes, CancellationToken ct)
    {
        var resultado = new Dictionary<string, AwClienteOrigenFila>(StringComparer.Ordinal);
        if (clientes.Count <= Math.Max(1, _options.MaxLecturasPorReferencia))
        {
            foreach (var c in clientes)
                if (await _origen.LeerPorReferenciaAsync(c.Referencia, ct) is { } f) resultado[c.Referencia] = f;
            return resultado;
        }

        var buscadas = clientes.Select(c => c.Referencia).ToHashSet(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var pagina = await _origen.LeerPaginaAsync(cursor, AwClientesOptions.TamanoLoteMaximo, ct);
            foreach (var f in pagina.Filas)
            {
                var referencia = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (buscadas.Contains(referencia)) resultado[referencia] = f;
            }
            cursor = pagina.SiguienteCursor;
        } while (cursor is not null);
        return resultado;
    }

    private async Task<int> CompararProductosAsync(Guid? entidadId, List<DiferenciaFiscalDetectada> diferencias, CancellationToken ct)
    {
        var q = _db.ProductosAw.AsNoTracking().Where(p => p.Estatus == EstatusCatalogo.Activo);
        if (entidadId is { } id) q = q.Where(p => p.Id == id);
        var productos = await q.ToListAsync(ct);
        if (productos.Count == 0) return 0;

        var reglas = await _db.Set<ReglaFiscalAw>().AsNoTracking().Where(r => r.Activa).ToListAsync(ct);
        foreach (var p in productos)
        {
            var erp = new ProductoFiscalErp(p.Id, p.ReferenciaExterna, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
                DiferenciaIntencional: p.OrigenFiscal == OrigenFiscalProducto.ExcepcionProducto || p.EstadoFiscal == EstadoFiscalProducto.Confirmada);
            diferencias.AddRange(ComparadorDiferenciasFiscales.CompararProducto(erp,
                Esperado(p, reglas, _productos.ReglasFiscales, _pedidos.MapeoUnidadSat)));
        }
        return productos.Count;
    }

    /// <summary>
    /// Lo esperado para el producto, con la misma precedencia que la sincronización: regla de la tabla
    /// (Wgr &gt; Grupo &gt; Tipo), luego regla de configuración por Tipo. La unidad SAT sale de la regla y, si la
    /// regla no la trae, del mapeo de unidad A+W → SAT.
    /// </summary>
    public static EsperadoFiscalProducto Esperado(
        ProductoAw p,
        IReadOnlyList<ReglaFiscalAw> reglas,
        IReadOnlyDictionary<string, ReglaFiscalProductoAw> reglasConfiguracion,
        IReadOnlyDictionary<string, string> mapeoUnidadSat)
    {
        string? prodServ = null, unidad = null, objetoImp = null;
        var fuenteRegla = "Sin regla fiscal";
        var regla = reglas.Where(r => r.Coincide(p)).OrderByDescending(r => r.Campo == "Wgr" ? 3 : r.Campo == "Grupo" ? 2 : 1).FirstOrDefault();
        if (regla is not null)
        {
            (prodServ, unidad, objetoImp) = (regla.Ficha.ClaveProdServSat, regla.Ficha.ClaveUnidadSat, regla.Ficha.ObjetoImp);
            fuenteRegla = $"Regla {regla.Campo} = {regla.Valor}";
        }
        else if (p.Tipo is { } tipo
                 && reglasConfiguracion.FirstOrDefault(kv => string.Equals(kv.Key, tipo, StringComparison.OrdinalIgnoreCase)).Value is { } rc)
        {
            (prodServ, unidad, objetoImp) = (rc.ClaveProdServSat, rc.ClaveUnidadSat, rc.ObjetoImp);
            fuenteRegla = $"Configuración Tipo = {tipo}";
        }

        var fuenteUnidad = fuenteRegla;
        if (string.IsNullOrWhiteSpace(unidad))
        {
            var mapeo = mapeoUnidadSat.FirstOrDefault(kv => string.Equals(kv.Key, p.UnidadMedida, StringComparison.OrdinalIgnoreCase));
            unidad = mapeo.Value;
            fuenteUnidad = mapeo.Value is null ? "Sin regla ni mapeo de unidad" : $"Mapeo de unidad {p.UnidadMedida}";
        }
        return new(new(prodServ, fuenteRegla), new(unidad, fuenteUnidad), new(objetoImp, fuenteRegla));
    }
}
