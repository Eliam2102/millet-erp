using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Auditoria;

/// <summary>
/// Query consolidada sobre el log de auditoría (<c>core.audit_log</c>,
/// ADR-0008). Cierre del <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c> y F1-ADM-03.
///
/// <para>
/// El rango de fechas (<see cref="Desde"/>, <see cref="Hasta"/>) es
/// obligatorio para evitar table scans completos. Rango máximo: 90 días.
/// Validador del command lo enforce → 400 ProblemDetails con detalle.
/// </para>
/// </summary>
public sealed record ConsultarBitacoraQuery(
    DateOnly Desde,
    DateOnly Hasta,
    string? Modulo = null,
    string? Recurso = null,
    string? Accion = null,
    Guid? UsuarioId = null,
    Guid? EmpresaId = null,
    Guid? SucursalId = null,
    Guid? EntidadId = null,
    Guid? AggregateRootId = null,
    string? ActorTipo = null,
    string? Q = null,
    int Offset = 0,
    int Limit = 50,
    string? ZonaHoraria = null) : IRequest<ConsultarBitacoraResponse>;

public sealed class ConsultarBitacoraQueryValidator : AbstractValidator<ConsultarBitacoraQuery>
{
    public ConsultarBitacoraQueryValidator()
    {
        RuleFor(x => x.Desde)
            .Must((q, _) => q.Desde <= q.Hasta)
            .WithMessage("'desde' debe ser menor o igual que 'hasta'.");

        RuleFor(x => x.Hasta)
            .Must((q, _) => (q.Hasta.ToDateTime(TimeOnly.MinValue) - q.Desde.ToDateTime(TimeOnly.MinValue)).TotalDays <= 90)
            .WithMessage("El rango máximo permitido es 90 días (Hasta - Desde).");

        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 200);
        RuleFor(x => x.ZonaHoraria)
            .Must(zona => zona is null || EsZonaHorariaValida(zona))
            .WithMessage("La zona horaria debe ser un identificador IANA válido.");
    }

    private static bool EsZonaHorariaValida(string zona)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(zona); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}

/// <summary>
/// Una fila del log de auditoría enriquecida para UI y reportes.
/// Proyecta las columnas snapshot (ActorNombre, ActorTipo, EntidadEtiqueta, Resumen)
/// y mantiene UsuarioNombre por compatibilidad con el frontend.
/// </summary>
public sealed record AuditLogEntryResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UsuarioId,
    string? UsuarioNombre,
    Guid? EmpresaId,
    string Modulo,
    string Entidad,
    Guid? EntidadId,
    Guid? AggregateRootId,
    string Operacion,
    string Cambios,
    Guid CorrelationId,
    Guid? SucursalId,
    string? SucursalClave,
    string ActorNombre,
    string ActorTipo,
    string? ActorEmail,
    string EntidadEtiqueta,
    string Resumen,
    string? Origen);

public sealed record ConsultarBitacoraResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Total);

public sealed class ConsultarBitacoraHandler
    : IRequestHandler<ConsultarBitacoraQuery, ConsultarBitacoraResponse>
{
    private readonly CoreDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly CompartidoDbContext _compartidoDb;

    public ConsultarBitacoraHandler(
        CoreDbContext db,
        ICurrentEmpresaContext empresaContext,
        CompartidoDbContext compartidoDb)
    {
        _db = db;
        _empresaContext = empresaContext;
        _compartidoDb = compartidoDb;
    }

    public async Task<ConsultarBitacoraResponse> Handle(
        ConsultarBitacoraQuery request,
        CancellationToken cancellationToken)
    {
        // Las fechas del filtro representan días locales de quien consulta.
        // Sin zona horaria explícita se conserva el comportamiento UTC anterior.
        var zona = request.ZonaHoraria is null
            ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(request.ZonaHoraria);
        var desdeUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            request.Desde.ToDateTime(TimeOnly.MinValue), zona));
        var hastaUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            request.Hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), zona));

        var query = _db.AuditLog
            .AsNoTracking()
            .Where(a => a.Timestamp >= desdeUtc && a.Timestamp < hastaUtc);

        // La bitácora no tiene query filter global. Un evento sin empresa
        // puede pertenecer a una identidad de otra razón social; no se debe
        // exponer en la vista de una empresa por el solo hecho de ser global.
        if (_empresaContext.Current is Guid empresaActual)
            query = query.Where(a => a.EmpresaId == empresaActual);
        else
            query = query.Where(a => false);

        if (request.Modulo is { Length: > 0 })
            query = query.Where(a => a.Modulo == request.Modulo);
        if (request.Recurso is { Length: > 0 })
            query = query.Where(a => a.Entidad == request.Recurso);
        if (request.Accion is { Length: > 0 })
        {
            var accionNorm = request.Accion.Trim().ToLowerInvariant();
            if (accionNorm is "autorizacion" or "autorización")
                query = query.Where(a => a.Operacion == "autorizacion");
            else if (accionNorm is "eliminar" or "borrar")
                query = query.Where(a => a.Operacion == "borrar" || a.Operacion == "eliminar");
            else
                query = query.Where(a => EF.Functions.ILike(a.Operacion, request.Accion));
        }
        if (request.UsuarioId is Guid u)
            query = query.Where(a => a.UsuarioId == u);
        if (request.EmpresaId is Guid e)
            query = query.Where(a => a.EmpresaId == e);
        if (request.EntidadId is Guid entId)
            query = query.Where(a => a.EntidadId == entId);
        if (request.AggregateRootId is Guid rootId)
            query = query.Where(a => a.AggregateRootId == rootId);
        if (request.ActorTipo is { Length: > 0 })
            query = query.Where(a => a.ActorTipo == request.ActorTipo);
        if (request.Q is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.ActorNombre, pattern) ||
                EF.Functions.ILike(a.EntidadEtiqueta, pattern) ||
                EF.Functions.ILike(a.Resumen, pattern));
        }
        if (request.SucursalId is Guid sucursal)
        {
            var filtro = System.Text.Json.JsonSerializer.Serialize(new { sucursalId = sucursal });
            query = query.Where(a => a.Metadatos != null &&
                EF.Functions.JsonContains(a.Metadatos, filtro));
        }

        var total = await query.CountAsync(cancellationToken);

        var rawRows = await query
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UsuarioId,
                a.ActorNombre,
                a.ActorTipo,
                a.ActorEmail,
                a.EntidadEtiqueta,
                a.Resumen,
                a.EmpresaId,
                a.Modulo,
                a.Entidad,
                a.EntidadId,
                a.AggregateRootId,
                a.Operacion,
                a.Cambios,
                a.CorrelationId,
                a.Metadatos
            })
            .ToListAsync(cancellationToken);


        var sucursalIds = new HashSet<Guid>();
        var departamentoIds = new HashSet<Guid>();
        var puestoIds = new HashSet<Guid>();
        var empleadoIds = new HashSet<Guid>();

        // 1. Recolectar IDs relevantes de las filas consultadas
        foreach (var r in rawRows)
        {
            if (r.EntidadId.HasValue)
            {
                var ent = r.Entidad;
                var id = r.EntidadId.Value;
                if (ent == "Sucursal") sucursalIds.Add(id);
                else if (ent == "Departamento") departamentoIds.Add(id);
                else if (ent == "Puesto") puestoIds.Add(id);
                else if (ent == "Empleado") empleadoIds.Add(id);
            }

            if (!string.IsNullOrWhiteSpace(r.Metadatos))
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.Metadatos);
                    if (doc.RootElement.TryGetProperty("sucursalId", out var sid) && sid.TryGetGuid(out var val))
                    {
                        sucursalIds.Add(val);
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(r.Cambios))
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.Cambios);
                    var root = doc.RootElement;

                    void CollectFromProps(JsonElement el)
                    {
                        if (el.ValueKind != JsonValueKind.Object) return;
                        foreach (var prop in el.EnumerateObject())
                        {
                            var name = prop.Name;
                            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.TryGetGuid(out var g))
                            {
                                if (name.Equals("DepartamentoId", StringComparison.OrdinalIgnoreCase)) departamentoIds.Add(g);
                                else if (name.Equals("PuestoId", StringComparison.OrdinalIgnoreCase)) puestoIds.Add(g);
                                else if (name.Equals("JefeDirectoId", StringComparison.OrdinalIgnoreCase) || name.Equals("EmpleadoId", StringComparison.OrdinalIgnoreCase)) empleadoIds.Add(g);
                                else if (name.Equals("SucursalId", StringComparison.OrdinalIgnoreCase)) sucursalIds.Add(g);
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                if (prop.Value.TryGetProperty("antes", out var antes) && antes.ValueKind == JsonValueKind.String && antes.TryGetGuid(out var gAntes))
                                {
                                    if (name.Equals("DepartamentoId", StringComparison.OrdinalIgnoreCase)) departamentoIds.Add(gAntes);
                                    else if (name.Equals("PuestoId", StringComparison.OrdinalIgnoreCase)) puestoIds.Add(gAntes);
                                    else if (name.Equals("JefeDirectoId", StringComparison.OrdinalIgnoreCase) || name.Equals("EmpleadoId", StringComparison.OrdinalIgnoreCase)) empleadoIds.Add(gAntes);
                                    else if (name.Equals("SucursalId", StringComparison.OrdinalIgnoreCase)) sucursalIds.Add(gAntes);
                                }
                                if (prop.Value.TryGetProperty("despues", out var despues) && despues.ValueKind == JsonValueKind.String && despues.TryGetGuid(out var gDesp))
                                {
                                    if (name.Equals("DepartamentoId", StringComparison.OrdinalIgnoreCase)) departamentoIds.Add(gDesp);
                                    else if (name.Equals("PuestoId", StringComparison.OrdinalIgnoreCase)) puestoIds.Add(gDesp);
                                    else if (name.Equals("JefeDirectoId", StringComparison.OrdinalIgnoreCase) || name.Equals("EmpleadoId", StringComparison.OrdinalIgnoreCase)) empleadoIds.Add(gDesp);
                                    else if (name.Equals("SucursalId", StringComparison.OrdinalIgnoreCase)) sucursalIds.Add(gDesp);
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("snapshot", out var snap)) CollectFromProps(snap);
                    if (root.TryGetProperty("snapshot_pre_borrado", out var snapPre)) CollectFromProps(snapPre);
                    if (root.TryGetProperty("diff", out var diff)) CollectFromProps(diff);
                }
                catch { }
            }
        }

        // 2. Resolver diccionarios en paralelo o batch desde _compartidoDb
        var deptos = departamentoIds.Count > 0
            ? await _compartidoDb.Departamentos.AsNoTracking()
                .Where(d => departamentoIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => string.IsNullOrEmpty(d.Clave) ? d.Nombre : $"{d.Nombre} ({d.Clave})", cancellationToken)
            : new Dictionary<Guid, string>();

        var puestos = puestoIds.Count > 0
            ? await _compartidoDb.Puestos.AsNoTracking()
                .Where(p => puestoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => string.IsNullOrEmpty(p.Clave) ? p.Nombre : $"{p.Nombre} ({p.Clave})", cancellationToken)
            : new Dictionary<Guid, string>();

        var empleados = empleadoIds.Count > 0
            ? await _compartidoDb.Empleados.AsNoTracking()
                .Where(e => empleadoIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => string.IsNullOrEmpty(e.Clave) ? e.Nombre : $"{e.Nombre} ({e.Clave})", cancellationToken)
            : new Dictionary<Guid, string>();

        var sucursales = sucursalIds.Count > 0
            ? await _compartidoDb.Sucursales.AsNoTracking()
                .Where(s => sucursalIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => (Clave: s.Clave, Nombre: s.Nombre), cancellationToken)
            : new Dictionary<Guid, (string Clave, string Nombre)>();

        // 3. Mapear y enriquecer las filas para que la respuesta sea 100% amigable y sin GUIDs crudos
        var items = new List<AuditLogEntryResponse>(rawRows.Count);

        foreach (var r in rawRows)
        {
            Guid? sucursalId = null;
            string? sucursalClave = null;
            string? origen = null;

            if (!string.IsNullOrWhiteSpace(r.Metadatos))
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.Metadatos);
                    if (doc.RootElement.TryGetProperty("sucursalId", out var sid) && sid.TryGetGuid(out var val))
                    {
                        sucursalId = val;
                    }
                    if (doc.RootElement.TryGetProperty("sucursalClave", out var sc) && sc.ValueKind == JsonValueKind.String)
                    {
                        sucursalClave = sc.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("origen", out var orig) && orig.ValueKind == JsonValueKind.String)
                    {
                        origen = orig.GetString();
                    }
                }
                catch { }
            }

            if (sucursalId.HasValue && sucursalClave is null && sucursales.TryGetValue(sucursalId.Value, out var sInfo))
            {
                sucursalClave = sInfo.Clave;
            }

            var actorNombre = !string.IsNullOrWhiteSpace(r.ActorNombre)
                ? r.ActorNombre
                : (r.UsuarioId.HasValue ? $"Usuario {r.UsuarioId.Value.ToString()[..8]}" : "Sistema");
            var actorTipo = !string.IsNullOrWhiteSpace(r.ActorTipo) ? r.ActorTipo : "usuario";

            // Enriquecer EntidadEtiqueta
            var entidadEtiqueta = r.EntidadEtiqueta;
            if (string.IsNullOrWhiteSpace(entidadEtiqueta) || Guid.TryParse(entidadEtiqueta, out _) || entidadEtiqueta.StartsWith(r.Entidad + " ", StringComparison.Ordinal))
            {
                if (r.Entidad == "Empleado" && r.EntidadId.HasValue && empleados.TryGetValue(r.EntidadId.Value, out var empNom))
                    entidadEtiqueta = empNom;
                else if (r.Entidad == "Departamento" && r.EntidadId.HasValue && deptos.TryGetValue(r.EntidadId.Value, out var depNom))
                    entidadEtiqueta = depNom;
                else if (r.Entidad == "Puesto" && r.EntidadId.HasValue && puestos.TryGetValue(r.EntidadId.Value, out var pstNom))
                    entidadEtiqueta = pstNom;
                else if (r.Entidad == "Sucursal" && r.EntidadId.HasValue && sucursales.TryGetValue(r.EntidadId.Value, out var sucData))
                    entidadEtiqueta = $"{sucData.Nombre} ({sucData.Clave})";
                else
                    entidadEtiqueta = !string.IsNullOrWhiteSpace(r.EntidadEtiqueta)
                        ? r.EntidadEtiqueta
                        : $"{r.Entidad} {(r.EntidadId.HasValue ? r.EntidadId.Value.ToString()[..8] : string.Empty)}".Trim();
            }

            // Enriquecer Resumen reemplazando GUIDs conocidos y nombres técnicos
            var resumen = !string.IsNullOrWhiteSpace(r.Resumen) ? r.Resumen : $"{r.Operacion} {r.Entidad}";
            resumen = resumen
                .Replace("DepartamentoId:", "Departamento:")
                .Replace("PuestoId:", "Puesto:")
                .Replace("JefeDirectoId:", "Jefe directo:")
                .Replace("SucursalId:", "Sucursal:")
                .Replace("EmpresaId:", "Empresa:")
                .Replace("UsuarioId:", "Usuario:")
                .Replace("RolId:", "Rol:")
                .Replace("RolSugeridoId:", "Rol sugerido:");

            foreach (var (gid, nombre) in deptos) resumen = resumen.Replace(gid.ToString(), nombre);
            foreach (var (gid, nombre) in puestos) resumen = resumen.Replace(gid.ToString(), nombre);
            foreach (var (gid, nombre) in empleados) resumen = resumen.Replace(gid.ToString(), nombre);
            foreach (var (gid, sData) in sucursales) resumen = resumen.Replace(gid.ToString(), sData.Nombre);

            // Enriquecer Cambios inyectando snapshotTexto y antesTexto/despuesTexto
            var cambios = r.Cambios;
            if (!string.IsNullOrWhiteSpace(cambios))
            {
                try
                {
                    var node = JsonNode.Parse(cambios);
                    if (node is JsonObject rootObj)
                    {
                        var modificado = false;
                        if (rootObj["snapshot"] is JsonObject snapObj)
                        {
                            var snapTexto = rootObj["snapshotTexto"] as JsonObject ?? new JsonObject();
                            if (snapObj.TryGetPropertyValue("DepartamentoId", out var dVal) && dVal?.ToString() is string dStr && Guid.TryParse(dStr, out var dId) && deptos.TryGetValue(dId, out var dNom))
                            {
                                snapTexto["DepartamentoId"] = dNom;
                                modificado = true;
                            }
                            if (snapObj.TryGetPropertyValue("PuestoId", out var pVal) && pVal?.ToString() is string pStr && Guid.TryParse(pStr, out var pId) && puestos.TryGetValue(pId, out var pNom))
                            {
                                snapTexto["PuestoId"] = pNom;
                                modificado = true;
                            }
                            if (snapObj.TryGetPropertyValue("JefeDirectoId", out var jVal) && jVal?.ToString() is string jStr && Guid.TryParse(jStr, out var jId) && empleados.TryGetValue(jId, out var jNom))
                            {
                                snapTexto["JefeDirectoId"] = jNom;
                                modificado = true;
                            }
                            if (snapObj.TryGetPropertyValue("SucursalId", out var sVal) && sVal?.ToString() is string sStr && Guid.TryParse(sStr, out var sId) && sucursales.TryGetValue(sId, out var sNom))
                            {
                                snapTexto["SucursalId"] = sNom.Nombre;
                                modificado = true;
                            }
                            if (modificado)
                            {
                                rootObj["snapshotTexto"] = snapTexto;
                            }
                        }

                        if (rootObj["diff"] is JsonObject diffObj)
                        {
                            void EnrichDiffField(string propName, IReadOnlyDictionary<Guid, string> dict)
                            {
                                if (diffObj[propName] is JsonObject itemObj)
                                {
                                    if (itemObj["antes"]?.ToString() is string aStr && Guid.TryParse(aStr, out var aId) && dict.TryGetValue(aId, out var aNom))
                                    {
                                        itemObj["antesTexto"] = aNom;
                                        modificado = true;
                                    }
                                    if (itemObj["despues"]?.ToString() is string dStr && Guid.TryParse(dStr, out var dId) && dict.TryGetValue(dId, out var dNom))
                                    {
                                        itemObj["despuesTexto"] = dNom;
                                        modificado = true;
                                    }
                                }
                            }
                            EnrichDiffField("DepartamentoId", deptos);
                            EnrichDiffField("PuestoId", puestos);
                            EnrichDiffField("JefeDirectoId", empleados);
                            if (diffObj["SucursalId"] is JsonObject sucDiff)
                            {
                                if (sucDiff["antes"]?.ToString() is string saStr && Guid.TryParse(saStr, out var saId) && sucursales.TryGetValue(saId, out var saNom))
                                {
                                    sucDiff["antesTexto"] = saNom.Nombre;
                                    modificado = true;
                                }
                                if (sucDiff["despues"]?.ToString() is string sdStr && Guid.TryParse(sdStr, out var sdId) && sucursales.TryGetValue(sdId, out var sdNom))
                                {
                                    sucDiff["despuesTexto"] = sdNom.Nombre;
                                    modificado = true;
                                }
                            }
                        }

                        if (modificado)
                        {
                            cambios = rootObj.ToJsonString();
                        }
                    }
                }
                catch { }
            }

            items.Add(new AuditLogEntryResponse(
                Id: r.Id,
                Timestamp: r.Timestamp,
                UsuarioId: r.UsuarioId,
                UsuarioNombre: actorNombre,
                EmpresaId: r.EmpresaId,
                Modulo: r.Modulo,
                Entidad: r.Entidad,
                EntidadId: r.EntidadId,
                AggregateRootId: r.AggregateRootId,
                Operacion: r.Operacion,
                Cambios: cambios,
                CorrelationId: r.CorrelationId,
                SucursalId: sucursalId,
                SucursalClave: sucursalClave,
                ActorNombre: actorNombre,
                ActorTipo: actorTipo,
                ActorEmail: r.ActorEmail,
                EntidadEtiqueta: entidadEtiqueta,
                Resumen: resumen,
                Origen: origen));
        }

        return new ConsultarBitacoraResponse(items, total);
    }
}
