using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Folios;
using Millet.Compras.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de <see cref="IComprasCrearRqAppPort"/> (contrato Almacenes v1 §8.1). Misma
/// mecánica que <see cref="ComprasCrearRqSistemaAdapter"/>: usuario de servicio (creador,
/// requisitante y empresa), depto de sistema, folio por <see cref="IFolioSecuenciaService"/> y
/// <c>Bypass()</c> acotado al guardado. La RQ nace Autorizada y se deja EnSurtido con
/// <c>CantidadDeCompra = piezas</c> para que Compras pueda crear la OC (sus handlers exigen EnSurtido).
/// <para>
/// Supuestos: el artículo se resuelve por <c>Articulo.ProductoAwId → ProductoAw.ReferenciaExterna ==
/// articuloAw</c>; la sucursal por <c>Sucursal.Clave</c> (o <c>ClaveAw</c>); sin almacén destino;
/// departamento = Reabastecimiento Automático (no hay depto propio de la app).
/// </para>
/// </summary>
public sealed class ComprasCrearRqAppAdapter : IComprasCrearRqAppPort
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _maestros;
    private readonly IFolioSecuenciaService _folios;
    private readonly IUsuarioServicioReadPort _usuarioServicio;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IPublisher _publisher;

    /// <summary>Motivo sembrado «Otro» (texto libre, aplica a cancelación): el texto lleva el motivo que manda la app.</summary>
    private const string MotivoOtro = "RECH-OTRO";

    public ComprasCrearRqAppAdapter(ComprasDbContext db, CompartidoDbContext maestros,
        IFolioSecuenciaService folios, IUsuarioServicioReadPort usuarioServicio, ICurrentEmpresaContext empresa, IPublisher publisher)
    {
        _db = db; _maestros = maestros; _folios = folios; _usuarioServicio = usuarioServicio; _empresa = empresa; _publisher = publisher;
    }

    public async Task<AvisoRqAppResultado> CancelarAsync(string folioExterno, string? motivo, CancellationToken ct, string? folioErp = null)
    {
        var sp = await _usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo)
            return new(null, "El usuario de servicio del ERP no está disponible; no se puede cancelar la requisición.");
        using var bypass = _empresa.Bypass();
        var rq = await RqAppAsync(sp.EmpresaId, folioExterno, folioErp, ct);
        if (rq is null || rq.Estado is not (EstadoRequisicion.Autorizada or EstadoRequisicion.EnSurtido))
            return new(rq?.Folio.Valor, null);
        var motivoId = await _db.MotivosRechazo.AsNoTracking().Where(m => m.Clave == MotivoOtro && m.Activo)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        if (motivoId is null) return new(rq.Folio.Valor, $"No existe el motivo {MotivoOtro} para cancelar la requisición.");
        try
        {
            // Mismo agregado y evento que la cancelación de la pantalla; la RQ de la app no tiene apartados ni OC borrador.
            var evento = rq.Cancelar(motivoId.Value, sp.Id, DateTimeOffset.UtcNow, Texto(motivo));
            await _publisher.Publish(evento, ct);
        }
        catch (BusinessRuleException ex)
        {
            return new(rq.Folio.Valor, ex.Message);
        }
        await _db.SaveChangesAsync(ct);
        return new(rq.Folio.Valor, null);
    }

    public async Task<AvisoRqAppResultado> ReducirAsync(string folioExterno, IReadOnlyList<LineaQuitadaRqApp> lineas, string? motivo, CancellationToken ct,
        string? folioErp = null)
    {
        var sp = await _usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo)
            return new(null, "El usuario de servicio del ERP no está disponible; no se puede reducir la requisición.");
        using var bypass = _empresa.Bypass();
        var rq = await RqAppAsync(sp.EmpresaId, folioExterno, folioErp, ct);
        if (rq is null || rq.Estado is not (EstadoRequisicion.Autorizada or EstadoRequisicion.EnSurtido))
            return new(rq?.Folio.Valor, null);
        try
        {
            rq.QuitarDeCompraApp(lineas.Select(l => (l.Posicion, (decimal)l.Piezas)).ToList(), Texto(motivo));
        }
        catch (BusinessRuleException ex)
        {
            return new(rq.Folio.Valor, ex.Message);
        }
        await _db.SaveChangesAsync(ct);
        return new(rq.Folio.Valor, null);
    }

    // v9: el aviso trae folioErp (el folio que el ERP acusó); manda sobre el folio de la app. Si no viene, no es un folio válido o no
    // existe, se busca por el folio de la app como antes.
    private async Task<Requisicion?> RqAppAsync(Guid empresaId, string folioExterno, string? folioErp, CancellationToken ct)
    {
        var app = _db.Requisiciones.Include(r => r.Lineas)
            .Where(r => r.EmpresaId == empresaId && r.Origen == OrigenRequisicion.AlmacenesApp);
        Folio? folio = null;
        try { if (!string.IsNullOrWhiteSpace(folioErp)) folio = Folio.Parse(folioErp.Trim()); }
        catch (BusinessRuleException) { folio = null; }
        return (folio is null ? null : await app.FirstOrDefaultAsync(r => r.Folio == folio, ct))
            ?? await app.FirstOrDefaultAsync(r => r.FolioExterno == folioExterno.Trim(), ct);
    }

    private static string Texto(string? motivo)
    {
        var t = string.IsNullOrWhiteSpace(motivo) ? "App Almacenes" : $"App Almacenes: {motivo.Trim()}";
        return t.Length > 500 ? t[..500] : t;
    }

    public async Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud s, CancellationToken ct)
    {
        var sp = await _usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo)
            return Error("El usuario de servicio del ERP no está disponible; no se puede crear la requisición.");

        using var bypass = _empresa.Bypass();

        var previo = await FolioPrevioAsync(sp.EmpresaId, s.FolioExterno, ct);
        if (previo is not null) return new(true, previo, null);

        var proveedorId = await _maestros.Proveedores.AsNoTracking()
            .Where(p => p.ClaveAw == s.ProveedorClaveAw).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (proveedorId is null)
            return Error($"No existe un proveedor con clave A+W {s.ProveedorClaveAw}.");

        var sucursal = await _maestros.Sucursales.AsNoTracking()
            .Where(x => x.Clave == s.SucursalCodigo || x.ClaveAw == s.SucursalCodigo)
            .Select(x => new { x.Id, x.Clave }).FirstOrDefaultAsync(ct);
        if (sucursal is null)
            return Error($"No existe la sucursal '{s.SucursalCodigo}'.");

        var claves = s.Lineas.Select(l => l.ArticuloAw).Distinct().ToList();
        var articulos = await (from a in _maestros.Articulos.AsNoTracking()
                               join p in _maestros.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                               where claves.Contains(p.ReferenciaExterna)
                               select new
                               {
                                   Clave = p.ReferenciaExterna, a.Id, a.UnidadMedidaDefault,
                                   a.PrecioReferenciaMonto, a.PrecioReferenciaMoneda,
                               }).ToListAsync(ct);
        var porClave = articulos.GroupBy(a => a.Clave).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in claves)
        {
            if (!porClave.TryGetValue(c, out var lista))
                return Error($"No existe un artículo ligado al artículo A+W {c}.");
            if (lista.Count > 1)
                return Error($"El artículo A+W {c} está ligado a más de un artículo del ERP.");
        }

        var folioAnio = (short)DateTimeOffset.UtcNow.Year;
        var folioStr = await _folios.SiguienteFolioRequisicionAsync(
            sp.EmpresaId, sucursal.Id, sucursal.Clave, folioAnio, ct);

        Requisicion rq;
        try
        {
            rq = Requisicion.CrearDesdeAlmacenesApp(
                Guid.CreateVersion7(), sp.EmpresaId, Folio.Parse(folioStr), folioAnio, sucursal.Id,
                DepartamentosSistema.ReabastecimientoAutomatico, null, sp.Id, sp.Id, DateTimeOffset.UtcNow,
                s.FolioExterno, proveedorId.Value,
                s.Lineas.Select(l =>
                {
                    var a = porClave[l.ArticuloAw][0];
                    // Las piezas de la app son hojas: en vidrio (M2) la cantidad es piezas × m² de la hoja.
                    return new LineaAppRequisicion(a.Id,
                        LineaRequisicion.CantidadDePiezasApp(l.Piezas, l.AnchoMm, l.AltoMm, a.UnidadMedidaDefault), a.UnidadMedidaDefault,
                        Money.Of(a.PrecioReferenciaMonto ?? 0m, a.PrecioReferenciaMoneda ?? "MXN"),
                        l.Piezas, l.AnchoMm, l.AltoMm, s.FechaRequerida,
                        l.Obra is { Length: > 200 } ? l.Obra[..200] : l.Obra, l.Posicion, s.CentroCostoId);
                }).ToList(),
                s.Comentario is { Length: > 500 } ? s.Comentario[..500] : s.Comentario);
            rq.RegistrarCubrimiento(
                rq.Lineas.Select(l => new CubrimientoLinea(l.Id, 0, l.Cantidad)).ToList(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException)
        {
            return Error(ex.Message);
        }

        _db.Requisiciones.Add(rq);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Carrera con otro envío del mismo folio: gana el primero.
            _db.ChangeTracker.Clear();
            var ganador = await FolioPrevioAsync(sp.EmpresaId, s.FolioExterno, ct);
            if (ganador is null) throw;
            return new(true, ganador, null);
        }
        return new(false, rq.Folio.Valor, null);
    }

    private async Task<string?> FolioPrevioAsync(Guid empresaId, string folioExterno, CancellationToken ct)
    {
        var fila = await _db.Requisiciones.AsNoTracking()
            .Where(r => r.EmpresaId == empresaId && r.Origen == OrigenRequisicion.AlmacenesApp
                        && r.FolioExterno == folioExterno)
            .Select(r => new { r.Folio }).FirstOrDefaultAsync(ct);
        return fila?.Folio.Valor;
    }

    private static CrearRqAppResultado Error(string texto) => new(false, null, texto);
}
