using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Taller;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Taller;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Taller de sucursal (PT5.10): lo que bajó el inventario del taller por pedidos internos Serie 9000 y el cierre de
/// día del almacenista con su factor de optimización. El cierre lo captura quien gestiona salidas de su sucursal; una
/// vez cerrado solo lo corrige quien administra almacenes.
/// </summary>
public static class TallerEndpoints
{
    public sealed record DescargaItem(string NumeroPedido, int Posicion, string ArticuloRef, decimal Cantidad, decimal? AnchoMm,
        decimal? AltoMm, decimal? M2, string Estado, string? Folio, string? Error);
    public sealed record CierreResponse(Guid Id, Guid SucursalId, DateOnly Fecha, decimal HojasTomadas, decimal RetazosTomados,
        decimal M2Descargados, decimal M2Cortados, int PedidosServidos, int PedidosSerie9000, int Cortadores, decimal? FactorOptimizacion,
        string Formula, string? Notas, DateTimeOffset CerradoAt, DateTimeOffset? CorregidoAt);
    public sealed record PrellenadoResponse(Guid SucursalId, DateOnly Fecha, decimal HojasDescargadas, decimal M2Descargados,
        int PedidosSerie9000, int PedidosServidos, int EnRevision, IReadOnlyList<DescargaItem> Descargas, CierreResponse? Cierre);
    public sealed record CierreRequest(Guid SucursalId, DateOnly Fecha, decimal HojasTomadas, decimal RetazosTomados, decimal M2Cortados,
        int PedidosServidos, int Cortadores, string? Notas);
    public sealed record CorreccionRequest(decimal HojasTomadas, decimal RetazosTomados, decimal M2Cortados, int PedidosServidos,
        int Cortadores, string? Notas);

    // ponytail: día local de Mérida a UTC-6 fijo, igual que el lector de pedidos servidos.
    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6));

    public static IEndpointRouteBuilder MapTallerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/taller").WithTags("Almacen");
        const string Todas = PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales;

        group.MapGet("/cierres/prellenado", async ([FromQuery] Guid sucursalId, [FromQuery] DateOnly fecha, AlmacenDbContext db,
            IPedidosTallerDiaReadPort servidos, DocumentoSucursalScope documentos, CancellationToken ct) =>
        {
            await documentos.VerificarSucursalAsync(sucursalId, Todas, ct);
            return Results.Ok(await ArmarAsync(db, servidos, sucursalId, fecha, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("PrellenarCierreTaller")
        .WithSummary("Lo descargado por Serie 9000 y los pedidos servidos del día; trae el cierre si ya existe.")
        .Produces<PrellenadoResponse>(StatusCodes.Status200OK);

        group.MapGet("/cierres", async ([FromQuery] Guid sucursalId, AlmacenDbContext db, DocumentoSucursalScope documentos, CancellationToken ct) =>
        {
            await documentos.VerificarSucursalAsync(sucursalId, Todas, ct);
            var cierres = await db.CierresTaller.AsNoTracking().Where(c => c.SucursalId == sucursalId)
                .OrderByDescending(c => c.Fecha).Take(60).ToListAsync(ct);
            return Results.Ok(cierres.Select(Respuesta).ToList());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarCierresTaller")
        .Produces<List<CierreResponse>>(StatusCodes.Status200OK);

        group.MapPost("/cierres", async (CierreRequest req, AlmacenDbContext db, IPedidosTallerDiaReadPort servidos,
            ISucursalReadPort sucursales, DocumentoSucursalScope documentos, CancellationToken ct) =>
        {
            await documentos.VerificarSucursalAsync(req.SucursalId, Todas, ct);
            if (req.Fecha > Hoy())
                throw new BusinessRuleException("CIERRE_TALLER_FECHA_FUTURA", "No se puede cerrar un día que todavía no llega.");
            var sucursal = await sucursales.ObtenerAsync(req.SucursalId, ct);
            if (sucursal is null || !sucursal.EsActiva)
                throw new BusinessRuleException("CIERRE_TALLER_SUCURSAL", "La sucursal no existe o está inactiva.");
            if (await db.CierresTaller.AnyAsync(c => c.SucursalId == req.SucursalId && c.Fecha == req.Fecha, ct))
                throw new BusinessRuleException("CIERRE_TALLER_YA_CERRADO",
                    "Ese día ya está cerrado. Solo quien administra almacenes puede corregirlo.");
            var (_, m2, pedidos) = await DescargadoAsync(db, req.SucursalId, req.Fecha, ct);
            var cierre = new CierreTaller(req.SucursalId, req.Fecha, req.HojasTomadas, req.RetazosTomados, m2, req.M2Cortados,
                req.PedidosServidos, pedidos, req.Cortadores, req.Notas);
            db.CierresTaller.Add(cierre);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                throw new BusinessRuleException("CIERRE_TALLER_YA_CERRADO", "Ese día ya está cerrado.");
            }
            return Results.Created($"/api/v1/almacen/taller/cierres/{cierre.Id}", Respuesta(cierre));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasCapturar)
        .WithName("CerrarDiaTaller")
        .Produces<CierreResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/cierres/{id:guid}", async (Guid id, CorreccionRequest req, AlmacenDbContext db, DocumentoSucursalScope documentos, CancellationToken ct) =>
        {
            var cierre = await db.CierresTaller.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cierre is null) return Results.NotFound();
            await documentos.VerificarSucursalAsync(cierre.SucursalId, Todas, ct);
            var (_, m2, pedidos) = await DescargadoAsync(db, cierre.SucursalId, cierre.Fecha, ct);
            cierre.Capturar(req.HojasTomadas, req.RetazosTomados, m2, req.M2Cortados, req.PedidosServidos, pedidos, req.Cortadores, req.Notas);
            cierre.MarcarCorregido();
            await db.SaveChangesAsync(ct);
            return Results.Ok(Respuesta(cierre));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("CorregirCierreTaller")
        .Produces<CierreResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<(decimal Hojas, decimal M2, int Pedidos)> DescargadoAsync(AlmacenDbContext db, Guid sucursalId, DateOnly fecha, CancellationToken ct)
    {
        var recibidas = db.DescargasTallerAw.AsNoTracking()
            .Where(d => d.SucursalId == sucursalId && d.FechaPedido == fecha && d.Estado == DescargaTallerAw.Recibido);
        var hojas = await recibidas.SumAsync(d => (decimal?)d.Cantidad, ct) ?? 0m;
        var m2 = await recibidas.SumAsync(d => (decimal?)d.M2, ct) ?? 0m;
        var pedidos = await recibidas.Select(d => d.NumeroPedido).Distinct().CountAsync(ct);
        return (hojas, m2, pedidos);
    }

    private static async Task<PrellenadoResponse> ArmarAsync(AlmacenDbContext db, IPedidosTallerDiaReadPort servidos, Guid sucursalId,
        DateOnly fecha, CancellationToken ct)
    {
        var (hojas, m2, pedidos) = await DescargadoAsync(db, sucursalId, fecha, ct);
        var lista = await db.DescargasTallerAw.AsNoTracking().Where(d => d.SucursalId == sucursalId && d.FechaPedido == fecha)
            .OrderBy(d => d.NumeroPedido).ThenBy(d => d.Posicion).ToListAsync(ct);
        var enRevision = lista.Count(d => d.Estado == DescargaTallerAw.EnRevision);
        var cierre = await db.CierresTaller.AsNoTracking().FirstOrDefaultAsync(c => c.SucursalId == sucursalId && c.Fecha == fecha, ct);
        return new(sucursalId, fecha, hojas, m2, pedidos, await servidos.PedidosServidosAsync(sucursalId, fecha, ct), enRevision,
            lista.Select(d => new DescargaItem(d.NumeroPedido, d.Posicion, d.ArticuloRef, d.Cantidad, d.AnchoMm, d.AltoMm, d.M2,
                d.Estado, d.Folio, d.Error)).ToList(),
            cierre is null ? null : Respuesta(cierre));
    }

    private static CierreResponse Respuesta(CierreTaller c) =>
        new(c.Id, c.SucursalId, c.Fecha, c.HojasTomadas, c.RetazosTomados, c.M2Descargados, c.M2Cortados, c.PedidosServidos,
            c.PedidosSerie9000, c.Cortadores, c.FactorOptimizacion, FactorOptimizacionTaller.Formula, c.Notas, c.CreatedAt, c.CorregidoAt);
}
