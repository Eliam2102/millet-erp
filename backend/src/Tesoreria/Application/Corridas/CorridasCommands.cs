using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Devoluciones;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Corridas;

// ============================================================================
// T9.1-A: armar y autorizar la corrida (ficha 86e3ke5bt). Armar = permiso
// `tesoreria.corridas.crear`; firmar/rechazar = `tesoreria.corridas.autorizar`
// (el endpoint lo exige; aquí se aplica «quien arma no autoriza»). Versión
// esperada en cada transición (ADR-0012).
// ============================================================================

internal static class CorridaArmado
{
    public static async Task<CorridaPago> CargarAsync(TesoreriaDbContext db, Guid id, int versionEsperada, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await db.CorridasPago.Include(x => x.Lineas).Include(x => x.Firmas).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CORRIDA_NO_ENCONTRADA", $"No se encontró la corrida '{id}'.");
        if (c.Version != versionEsperada) throw new ConcurrencyException(nameof(CorridaPago), c.Id);
        return c;
    }

    /// <summary>Valida y agrega los pasivos: pagables (hook <see cref="PasivosPagables"/>), libres de otra corrida viva y con proveedor/cuenta validados.</summary>
    public static async Task AgregarPasivosAsync(TesoreriaDbContext db, IProveedorBancoReadPort proveedores, CorridaPago corrida,
        IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var pasivos = await PasivosPagables.Elegibles(db, null).Where(p => facturaIds.Contains(p.FacturaProveedorId)).ToListAsync(ct);
        var faltan = facturaIds.Except(pasivos.Select(p => p.FacturaProveedorId)).ToList();
        if (faltan.Count > 0)
            throw new BusinessRuleException("CORRIDA_PASIVO_NO_PAGABLE",
                $"Pasivo(s) no pagables (no autorizados, retirados por CxP o sin saldo): {string.Join(", ", faltan)}.");

        var enOtra = await (from l in db.Set<CorridaPagoLinea>()
                            join c in db.CorridasPago on l.CorridaId equals c.Id
                            where l.Viva && l.CorridaId != corrida.Id && facturaIds.Contains(l.FacturaProveedorId)
                            select new { l.FacturaProveedorId, c.Nombre }).FirstOrDefaultAsync(ct);
        if (enOtra is not null)
            throw new BusinessRuleException("CORRIDA_PASIVO_EN_OTRA_CORRIDA",
                $"El pasivo {enOtra.FacturaProveedorId} ya está en la corrida '{enOtra.Nombre}'.");

        // Plano 7.2 paso 2: proveedor activo y cuenta bancaria validada (Estado «Activa») en la moneda de la corrida.
        var info = await proveedores.ObtenerVariosAsync(pasivos.Select(p => p.ProveedorId).Distinct().ToList(), ct);
        foreach (var proveedorId in pasivos.Select(p => p.ProveedorId).Distinct())
            ReglasDestinoProveedor.Resolver(info.GetValueOrDefault(proveedorId), corrida.Moneda, null);

        foreach (var pasivo in pasivos)
            if (corrida.AgregarLinea(pasivo) is { } linea) db.Add(linea);
    }
}

// --------------------------------------------------- Crear

public sealed record CrearCorridaCommand(Guid CuentaBancariaId, IReadOnlyList<Guid> Facturas, string? Nombre = null) : IRequest<CorridaResponse>;

public sealed class CrearCorridaValidator : AbstractValidator<CrearCorridaCommand>
{
    public CrearCorridaValidator()
    {
        RuleFor(c => c.CuentaBancariaId).NotEmpty();
        RuleFor(c => c.Nombre).MaximumLength(120);
        RuleFor(c => c.Facturas).NotEmpty().WithMessage("Elige al menos un pasivo.");
        RuleFor(c => c.Facturas).Must(f => f.Distinct().Count() == f.Count).WithMessage("Hay pasivos repetidos.");
    }
}

public sealed class CrearCorridaHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext user,
    IProveedorBancoReadPort proveedores, IClock clock) : IRequestHandler<CrearCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(CrearCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = DevolucionTransiciones.Contexto(empresa, user, "arma la corrida");
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, ct)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", $"No se encontró la cuenta bancaria '{command.CuentaBancariaId}'.");
        if (!cuenta.Activa)
            throw new BusinessRuleException("CORRIDA_CUENTA_INACTIVA", "La cuenta bancaria está inactiva.");

        var corrida = CorridaPago.Crear(empresaId, command.Nombre, cuenta.Id, cuenta.Moneda, usuarioId, clock.UtcNow);
        await CorridaArmado.AgregarPasivosAsync(db, proveedores, corrida, command.Facturas, ct);
        db.CorridasPago.Add(corrida);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, corrida, ct);
    }
}

// --------------------------------------------------- Agregar / quitar pasivos (solo borrador)

public sealed record AgregarPasivosCorridaCommand(Guid Id, IReadOnlyList<Guid> Facturas, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class AgregarPasivosCorridaValidator : AbstractValidator<AgregarPasivosCorridaCommand>
{
    public AgregarPasivosCorridaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Facturas).NotEmpty();
    }
}

public sealed class AgregarPasivosCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores)
    : IRequestHandler<AgregarPasivosCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(AgregarPasivosCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        await CorridaArmado.AgregarPasivosAsync(db, proveedores, c, command.Facturas.Distinct().ToList(), ct);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

public sealed record QuitarPasivoCorridaCommand(Guid Id, Guid FacturaProveedorId, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class QuitarPasivoCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores)
    : IRequestHandler<QuitarPasivoCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(QuitarPasivoCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        c.QuitarLinea(command.FacturaProveedorId);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

// --------------------------------------------------- Enviar a autorización

public sealed record EnviarCorridaAAutorizacionCommand(Guid Id, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class EnviarCorridaAAutorizacionHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores)
    : IRequestHandler<EnviarCorridaAAutorizacionCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(EnviarCorridaAAutorizacionCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        var escalones = await PoliticaFirmas.CargarAsync(db, c.EmpresaId, c.Moneda, ct);
        var (firmas, rol) = PoliticaFirmaCorrida.Requisito(escalones, c.Total);
        c.EnviarAAutorizacion(firmas, rol);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

// --------------------------------------------------- Autorizar (firmar)

public sealed record AutorizarCorridaCommand(Guid Id, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class AutorizarCorridaHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext user,
    IFirmanteRolesReadPort roles, IProveedorBancoReadPort proveedores, IClock clock) : IRequestHandler<AutorizarCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(AutorizarCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = DevolucionTransiciones.Contexto(empresa, user, "firma la corrida");
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        var firma = c.Firmar(usuarioId, await roles.CodigosRolAsync(usuarioId, empresaId, ct), clock.UtcNow);
        db.Add(firma);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

// --------------------------------------------------- Rechazar / cancelar

public sealed record RechazarCorridaCommand(Guid Id, string Motivo, int VersionEsperada) : IRequest<CorridaResponse>;
public sealed record CancelarCorridaCommand(Guid Id, string Motivo, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class RechazarCorridaValidator : AbstractValidator<RechazarCorridaCommand>
{
    public RechazarCorridaValidator() { RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400); }
}

public sealed class CancelarCorridaValidator : AbstractValidator<CancelarCorridaCommand>
{
    public CancelarCorridaValidator() { RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400); }
}

public sealed class RechazarCorridaHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext user, IProveedorBancoReadPort proveedores)
    : IRequestHandler<RechazarCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(RechazarCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (_, usuarioId) = DevolucionTransiciones.Contexto(empresa, user, "rechaza la corrida");
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        c.Rechazar(command.Motivo, usuarioId);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

public sealed class CancelarCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores)
    : IRequestHandler<CancelarCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(CancelarCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, ct);
        c.Cancelar(command.Motivo);
        await db.SaveChangesAsync(ct);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, ct);
    }
}

// --------------------------------------------------- Parámetro de firmas por monto y moneda

public sealed record EscalonFirmaResponse(string Moneda, decimal MontoDesde, int FirmasRequeridas, string? RolRequerido);

internal static class PoliticaFirmas
{
    /// <summary>
    /// Escalones de la moneda. Si la empresa aún no tiene ninguno se siembra el DEMO de la ficha (500,000 MXN → 2 firmas, una
    /// con rol «gerente»); queda en la tabla y se edita con <see cref="GuardarPoliticaFirmasCommand"/>. Lo persiste el SaveChanges del llamador.
    /// </summary>
    public static async Task<List<PoliticaFirmaCorrida>> CargarAsync(TesoreriaDbContext db, Guid empresaId, string? moneda, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var todos = await db.PoliticasFirmaCorrida.ToListAsync(ct);
        if (todos.Count == 0) { var demo = PoliticaFirmaCorrida.Demo(empresaId); db.PoliticasFirmaCorrida.Add(demo); todos.Add(demo); }
        return moneda is null ? todos : todos.Where(e => e.Moneda == moneda).ToList();
    }
}

public sealed record ObtenerPoliticaFirmasQuery : IRequest<IReadOnlyList<EscalonFirmaResponse>>;

public sealed class ObtenerPoliticaFirmasHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa)
    : IRequestHandler<ObtenerPoliticaFirmasQuery, IReadOnlyList<EscalonFirmaResponse>>
{
    public async Task<IReadOnlyList<EscalonFirmaResponse>> Handle(ObtenerPoliticaFirmasQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        var todos = await PoliticaFirmas.CargarAsync(db, empresaId, null, ct);
        await db.SaveChangesAsync(ct);
        return todos.OrderBy(e => e.Moneda).ThenBy(e => e.MontoDesde)
            .Select(e => new EscalonFirmaResponse(e.Moneda, e.MontoDesde, e.FirmasRequeridas, e.RolRequerido)).ToList();
    }
}

public sealed record EscalonFirmaItem(decimal MontoDesde, int FirmasRequeridas, string? RolRequerido);

/// <summary>Reemplaza los escalones de una moneda. Sin escalones aplicables basta una firma.</summary>
public sealed record GuardarPoliticaFirmasCommand(string Moneda, IReadOnlyList<EscalonFirmaItem> Escalones) : IRequest<IReadOnlyList<EscalonFirmaResponse>>;

public sealed class GuardarPoliticaFirmasValidator : AbstractValidator<GuardarPoliticaFirmasCommand>
{
    public GuardarPoliticaFirmasValidator()
    {
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Escalones).NotNull();
        RuleFor(c => c.Escalones).Must(e => e.Select(x => x.MontoDesde).Distinct().Count() == e.Count)
            .WithMessage("Hay dos escalones con el mismo monto.");
    }
}

public sealed class GuardarPoliticaFirmasHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa)
    : IRequestHandler<GuardarPoliticaFirmasCommand, IReadOnlyList<EscalonFirmaResponse>>
{
    public async Task<IReadOnlyList<EscalonFirmaResponse>> Handle(GuardarPoliticaFirmasCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        var moneda = command.Moneda.Trim().ToUpperInvariant();
        // Siembra el DEMO si la empresa está vacía para que guardar una moneda no la deje sin escalón MXN por accidente.
        var todos = await PoliticaFirmas.CargarAsync(db, empresaId, null, ct);
        db.PoliticasFirmaCorrida.RemoveRange(todos.Where(e => e.Moneda == moneda));
        var nuevos = command.Escalones.Select(e => new PoliticaFirmaCorrida(empresaId, moneda, e.MontoDesde, e.FirmasRequeridas, e.RolRequerido)).ToList();
        foreach (var n in nuevos) db.PoliticasFirmaCorrida.Add(n);
        await db.SaveChangesAsync(ct);
        return todos.Where(e => e.Moneda != moneda).Concat(nuevos).OrderBy(e => e.Moneda).ThenBy(e => e.MontoDesde)
            .Select(e => new EscalonFirmaResponse(e.Moneda, e.MontoDesde, e.FirmasRequeridas, e.RolRequerido)).ToList();
    }
}
