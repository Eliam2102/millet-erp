using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas;

/// <summary>Respuesta común de las mutaciones de caja (la versión alimenta el ETag).</summary>
public sealed record CajaMutadaResponse(Guid Id, int Version);

// ---- Crear ----

/// <summary>Da de alta una caja (CAJAS-PR1, 12-cajas.md §7). Nace activa y sin alcance.</summary>
public sealed record CrearCajaCommand(string Nombre, string? Descripcion) : IRequest<CajaMutadaResponse>;

/// <summary>Validación estructural; el negocio vive en el dominio.</summary>
public sealed class CrearCajaValidator : AbstractValidator<CrearCajaCommand>
{
    public CrearCajaValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Descripcion!).MaximumLength(254).When(c => c.Descripcion is not null);
    }
}

public sealed class CrearCajaHandler : IRequestHandler<CrearCajaCommand, CajaMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public CrearCajaHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db;
        _empresa = empresa;
    }

    public async Task<CajaMutadaResponse> Handle(CrearCajaCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var nombre = command.Nombre.Trim();
        var duplicada = await _db.Cajas.AsNoTracking()
            .AnyAsync(c => c.Nombre == nombre, cancellationToken);
        if (duplicada)
            throw new BusinessRuleException("CAJA_NOMBRE_DUPLICADO", $"Ya existe una caja llamada '{nombre}'.");

        var caja = Caja.Crear(empresaId, command.Nombre, command.Descripcion);
        _db.Cajas.Add(caja);
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMutadaResponse(caja.Id, caja.Version);
    }
}

// ---- Actualizar (datos generales + estatus) ----

/// <summary>
/// Edita nombre/descripción y activa/desactiva la caja.
/// <c>VersionEsperada</c> viene del header If-Match (ETag, ADR-0012).
/// </summary>
public sealed record ActualizarCajaCommand(
    Guid Id,
    int VersionEsperada,
    string Nombre,
    string? Descripcion,
    bool Activa) : IRequest<CajaMutadaResponse>;

public sealed class ActualizarCajaValidator : AbstractValidator<ActualizarCajaCommand>
{
    public ActualizarCajaValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Descripcion!).MaximumLength(254).When(c => c.Descripcion is not null);
    }
}

public sealed class ActualizarCajaHandler : IRequestHandler<ActualizarCajaCommand, CajaMutadaResponse>
{
    private readonly FacturacionDbContext _db;

    public ActualizarCajaHandler(FacturacionDbContext db) => _db = db;

    public async Task<CajaMutadaResponse> Handle(ActualizarCajaCommand command, CancellationToken cancellationToken)
    {
        var caja = await _db.Cajas
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CAJA_NO_ENCONTRADA", $"No existe la caja '{command.Id}'.");

        if (caja.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Caja), command.Id);

        var nombre = command.Nombre.Trim();
        var duplicada = await _db.Cajas.AsNoTracking()
            .AnyAsync(c => c.Id != caja.Id && c.Nombre == nombre, cancellationToken);
        if (duplicada)
            throw new BusinessRuleException("CAJA_NOMBRE_DUPLICADO", $"Ya existe una caja llamada '{nombre}'.");

        caja.Actualizar(command.Nombre, command.Descripcion);
        if (command.Activa) caja.Activar();
        else caja.Desactivar();

        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMutadaResponse(caja.Id, caja.Version);
    }
}

// ---- Replace-set de alcances (sucursales / canales / usuarios) ----

/// <summary>Reemplaza el conjunto de sucursales del alcance (PUT replace-set, If-Match).</summary>
public sealed record ReemplazarSucursalesCajaCommand(
    Guid Id, int VersionEsperada, IReadOnlyList<Guid> SucursalIds) : IRequest<CajaMutadaResponse>;

/// <summary>Reemplaza el conjunto de canales de venta del alcance (PUT replace-set, If-Match).</summary>
public sealed record ReemplazarCanalesCajaCommand(
    Guid Id, int VersionEsperada, IReadOnlyList<short> CanalVentaIds) : IRequest<CajaMutadaResponse>;

/// <summary>Reemplaza el conjunto de cajeros relacionados (PUT replace-set, If-Match).</summary>
public sealed record ReemplazarUsuariosCajaCommand(
    Guid Id, int VersionEsperada, IReadOnlyList<Guid> UsuarioIds) : IRequest<CajaMutadaResponse>;

public sealed class ReemplazarSucursalesCajaHandler
    : IRequestHandler<ReemplazarSucursalesCajaCommand, CajaMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ISucursalesReadPort _sucursales;

    public ReemplazarSucursalesCajaHandler(FacturacionDbContext db, ISucursalesReadPort sucursales)
    {
        _db = db;
        _sucursales = sucursales;
    }

    public async Task<CajaMutadaResponse> Handle(
        ReemplazarSucursalesCajaCommand command, CancellationToken cancellationToken)
    {
        var caja = await CajasCargador.CargarConVersionAsync(
            _db, command.Id, command.VersionEsperada, c => c.Sucursales, cancellationToken);

        var invalidas = await _sucursales.FiltrarNoActivasAsync(command.SucursalIds, cancellationToken);
        if (invalidas.Count > 0)
            throw new BusinessRuleException(
                "CAJA_SUCURSAL_NO_ACTIVA",
                $"Sucursales inexistentes o inactivas: {string.Join(", ", invalidas)}.");

        caja.ReemplazarSucursales(command.SucursalIds);
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMutadaResponse(caja.Id, caja.Version);
    }
}

public sealed class ReemplazarCanalesCajaHandler
    : IRequestHandler<ReemplazarCanalesCajaCommand, CajaMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICanalesVentaReadPort _canales;

    public ReemplazarCanalesCajaHandler(FacturacionDbContext db, ICanalesVentaReadPort canales)
    {
        _db = db;
        _canales = canales;
    }

    public async Task<CajaMutadaResponse> Handle(
        ReemplazarCanalesCajaCommand command, CancellationToken cancellationToken)
    {
        var caja = await CajasCargador.CargarConVersionAsync(
            _db, command.Id, command.VersionEsperada, c => c.Canales, cancellationToken);

        foreach (var canalId in command.CanalVentaIds.Distinct())
        {
            if (!await _canales.ExisteActivoAsync(canalId, cancellationToken))
                throw new BusinessRuleException(
                    "CAJA_CANAL_NO_ACTIVO",
                    $"El canal de venta '{canalId}' no existe o está inactivo.");
        }

        caja.ReemplazarCanales(command.CanalVentaIds);
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMutadaResponse(caja.Id, caja.Version);
    }
}

public sealed class ReemplazarUsuariosCajaHandler
    : IRequestHandler<ReemplazarUsuariosCajaCommand, CajaMutadaResponse>
{
    private readonly FacturacionDbContext _db;

    public ReemplazarUsuariosCajaHandler(FacturacionDbContext db) => _db = db;

    public async Task<CajaMutadaResponse> Handle(
        ReemplazarUsuariosCajaCommand command, CancellationToken cancellationToken)
    {
        var caja = await CajasCargador.CargarConVersionAsync(
            _db, command.Id, command.VersionEsperada, c => c.Usuarios, cancellationToken);

        // UsuarioId opaco (sin FK a Identidad): la UI alimenta desde el picker
        // de usuarios; el dominio valida no-vacío. Mismo trato que
        // Comprobante.UsuarioEmisorId.
        caja.ReemplazarUsuarios(command.UsuarioIds);
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMutadaResponse(caja.Id, caja.Version);
    }
}

/// <summary>Carga compartida del agregado con include + verificación If-Match (409 al choque).</summary>
internal static class CajasCargador
{
    public static async Task<Caja> CargarConVersionAsync<TChild>(
        FacturacionDbContext db,
        Guid id,
        int versionEsperada,
        System.Linq.Expressions.Expression<Func<Caja, IEnumerable<TChild>>> include,
        CancellationToken cancellationToken)
    {
        var caja = await db.Cajas
            .Include(include)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("CAJA_NO_ENCONTRADA", $"No existe la caja '{id}'.");

        if (caja.Version != versionEsperada)
            throw new ConcurrencyException(nameof(Caja), id);

        return caja;
    }
}
