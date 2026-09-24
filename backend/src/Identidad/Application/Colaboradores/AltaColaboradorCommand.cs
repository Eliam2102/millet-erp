using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Empleados;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Application.UsuarioSucursales;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Camino del paso "Acceso" del wizard de alta (plan 15, D2).
/// </summary>
public enum TipoAccesoColaborador
{
    /// <summary>Camino C: solo Empleado, sin usuario del ERP.</summary>
    SinAcceso = 0,

    /// <summary>Camino A: la persona ya tiene cuenta Microsoft.</summary>
    CuentaExistente = 1,

    /// <summary>
    /// Camino B: el ERP crea la cuenta en Entra. El alta deja al usuario en
    /// <see cref="EstadoAcceso.ProvisionandoCuenta"/> y el worker de
    /// provisión crea la cuenta y envía el acceso después del commit (F4).
    /// </summary>
    CuentaNueva = 2,
}

/// <summary>
/// Alta unificada de colaborador (plan 15, F3): el admin da de alta al
/// Empleado y, según <see cref="Acceso"/>, en la misma operación se crea o
/// reutiliza su Usuario, se le asigna el rol en la empresa de la sucursal y
/// se le asocia a la sucursal. Todo va en una sola transacción
/// (<see cref="TransaccionColaborador"/>) y con una sola correlación de
/// auditoría.
///
/// <list type="bullet">
///   <item><b>Camino A</b> (<see cref="TipoAccesoColaborador.CuentaExistente"/>):
///         el backend vuelve a consultar el directorio (no confía en la
///         validación del frontend). Reutiliza un Usuario existente con ese
///         correo u OID si no tiene empleado; 409 <c>USUARIO_YA_VINCULADO</c>
///         si ya lo tiene.</item>
///   <item><b>Camino C</b> (<see cref="TipoAccesoColaborador.SinAcceso"/>):
///         solo el Empleado; el acceso se da después ("Dar acceso", F6).</item>
///   <item><b>Camino B</b> (<see cref="TipoAccesoColaborador.CuentaNueva"/>):
///         valida que el correo nuevo esté disponible y crea el Usuario con
///         OID <c>pending:{correo}</c> en <see cref="EstadoAcceso.ProvisionandoCuenta"/>.
///         La cuenta en Entra y el correo de acceso los hace
///         <c>ProvisionCuentaEntraWorker</c> después del commit, así que un
///         rollback nunca deja cuentas huérfanas en el tenant (F4).</item>
/// </list>
///
/// El rol se toma de <see cref="RolId"/> o, si no viene, de
/// <c>Puesto.RolSugeridoId</c> (D3).
/// </summary>
public sealed record AltaColaboradorCommand(
    Guid Id,
    string Clave,
    string Nombre,
    Guid SucursalId,
    Guid DepartamentoId,
    Guid PuestoId,
    TipoAccesoColaborador Acceso,
    string? CorreoCorporativo = null,
    string? EmailContacto = null,
    Guid? RolId = null,
    Guid? JefeDirectoId = null,
    string? CodigoNomina = null) : IRequest<AltaColaboradorResponse>;

public sealed record AltaColaboradorResponse(
    EmpleadoResponse Empleado,
    AccesoColaboradorResponse? Acceso);

public sealed record AccesoColaboradorResponse(
    Guid UsuarioId,
    string Email,
    string EntraOid,
    EstadoAcceso EstadoAcceso,
    bool UsuarioReutilizado,
    Guid EmpresaId,
    Guid RolId,
    Guid SucursalId);

public sealed class AltaColaboradorValidator : AbstractValidator<AltaColaboradorCommand>
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public AltaColaboradorValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.DepartamentoId).NotEmpty();
        RuleFor(c => c.PuestoId).NotEmpty();
        RuleFor(c => c.Acceso).IsInEnum();

        RuleFor(c => c.CorreoCorporativo)
            .NotEmpty()
            .When(c => c.Acceso != TipoAccesoColaborador.SinAcceso)
            .WithMessage("El correo corporativo es obligatorio cuando el colaborador tendrá acceso.");
        RuleFor(c => c.CorreoCorporativo!)
            .MaximumLength(254)
            .Must(BeValidEmail)
            .WithMessage("CorreoCorporativo debe tener formato válido 'local@dominio.tld'.")
            .When(c => !string.IsNullOrWhiteSpace(c.CorreoCorporativo));

        RuleFor(c => c.EmailContacto!)
            .MaximumLength(254)
            .Must(BeValidEmail)
            .WithMessage("EmailContacto debe tener formato válido 'local@dominio.tld'.")
            .When(c => !string.IsNullOrWhiteSpace(c.EmailContacto));
        RuleFor(c => c.EmailContacto)
            .NotEmpty()
            .When(c => c.Acceso == TipoAccesoColaborador.CuentaNueva)
            .WithMessage("El correo de contacto es obligatorio para una cuenta nueva: ahí se envía el acceso.");

        RuleFor(c => c.RolId)
            .Null()
            .When(c => c.Acceso == TipoAccesoColaborador.SinAcceso)
            .WithMessage("Un colaborador sin acceso al ERP no lleva rol.");

        RuleFor(c => c.CodigoNomina!).NotEmpty().MaximumLength(20)
            .When(c => c.CodigoNomina is not null);
    }

    private static bool BeValidEmail(string email) => EmailRegex.IsMatch(email.Trim());
}

public sealed class AltaColaboradorHandler
    : IRequestHandler<AltaColaboradorCommand, AltaColaboradorResponse>
{
    private readonly IMediator _mediator;
    private readonly IdentidadDbContext _identidad;
    private readonly CompartidoDbContext _compartido;
    private readonly TransaccionColaborador _transaccion;
    private readonly IEntraDirectorioPort _directorio;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _entraOptions;
    private readonly IAuditCorrelationContext _correlacion;

    public AltaColaboradorHandler(
        IMediator mediator,
        IdentidadDbContext identidad,
        CompartidoDbContext compartido,
        TransaccionColaborador transaccion,
        IEntraDirectorioPort directorio,
        IOptionsMonitor<EntraDirectorioOptions> entraOptions,
        IAuditCorrelationContext correlacion)
    {
        _mediator = mediator;
        _identidad = identidad;
        _compartido = compartido;
        _transaccion = transaccion;
        _directorio = directorio;
        _entraOptions = entraOptions;
        _correlacion = correlacion;
    }

    public async Task<AltaColaboradorResponse> Handle(
        AltaColaboradorCommand command, CancellationToken cancellationToken)
    {
        using var correlacion = _correlacion.Begin(Guid.CreateVersion7());

        var sucursal = await _compartido.Sucursales.AsNoTracking()
            .Where(s => s.Id == command.SucursalId)
            .Select(s => new { s.Id, s.EmpresaId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException(
                "EMPLEADO_SUCURSAL_NO_EXISTE",
                $"No existe la sucursal '{command.SucursalId}'.");

        if (command.Acceso == TipoAccesoColaborador.SinAcceso)
        {
            var soloEmpleado = await _mediator.Send(
                CrearEmpleado(command, sucursal.EmpresaId, usuarioId: null), cancellationToken);
            return new AltaColaboradorResponse(soloEmpleado, Acceso: null);
        }

        var correo = command.CorreoCorporativo!.Trim();
        var rolId = await ResolverRolAsync(command, cancellationToken);
        var cuentaNueva = command.Acceso == TipoAccesoColaborador.CuentaNueva;
        CuentaEntra? cuenta = null;
        Usuario? existente = null;
        if (cuentaNueva)
        {
            await ValidarCorreoDisponibleAsync(correo, cancellationToken);
        }
        else
        {
            cuenta = await ResolverCuentaEntraAsync(correo, cancellationToken);
            existente = await BuscarUsuarioReutilizableAsync(correo, cuenta, cancellationToken);
        }

        return await _transaccion.EjecutarAsync(async () =>
        {
            Guid usuarioId;
            if (cuentaNueva)
            {
                // Sin OID explícito: CrearUsuario deja pending:{correo}.
                var creado = await _mediator.Send(
                    new CrearUsuarioCommand(
                        Guid.Empty, correo, EntraIdObjectId: null, command.Nombre, command.DepartamentoId),
                    cancellationToken);
                var usuarioNuevo = await _identidad.Usuarios.SingleAsync(u => u.Id == creado.Id, cancellationToken);
                usuarioNuevo.IniciarProvision();
                await _identidad.SaveChangesAsync(cancellationToken);
                usuarioId = usuarioNuevo.Id;
            }
            else if (existente is not null)
            {
                existente.VincularEntraOid(cuenta!.ObjectId);
                existente.AsignarDepartamento(command.DepartamentoId);
                await _identidad.SaveChangesAsync(cancellationToken);
                usuarioId = existente.Id;
            }
            else
            {
                var creado = await _mediator.Send(
                    new CrearUsuarioCommand(
                        Guid.Empty, correo, cuenta!.ObjectId, cuenta.NombreMostrado, command.DepartamentoId),
                    cancellationToken);
                usuarioId = creado.Id;
            }

            var empleado = await _mediator.Send(
                CrearEmpleado(command, sucursal.EmpresaId, usuarioId), cancellationToken);

            var tieneRol = await _identidad.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(
                    uer => uer.UsuarioId == usuarioId && uer.EmpresaId == sucursal.EmpresaId && uer.RolId == rolId,
                    cancellationToken);
            if (!tieneRol)
            {
                await _mediator.Send(
                    new AsignarRolAUsuarioCommand(usuarioId, sucursal.EmpresaId, rolId), cancellationToken);
            }

            var asignacionSucursal = await _identidad.UsuarioSucursales.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(us => us.UsuarioId == usuarioId && us.SucursalId == sucursal.Id,
                    cancellationToken);
            if (asignacionSucursal is null)
            {
                await _mediator.Send(
                    new AsignarUsuarioASucursalCommand(sucursal.Id, usuarioId), cancellationToken);
            }
            else if (asignacionSucursal.Estatus != EstatusCatalogo.Activo)
            {
                await _mediator.Send(
                    new ReactivarAsignacionUsuarioSucursalCommand(sucursal.Id, usuarioId), cancellationToken);
            }

            var usuario = await _identidad.Usuarios.AsNoTracking()
                .SingleAsync(u => u.Id == usuarioId, cancellationToken);

            return new AltaColaboradorResponse(
                empleado,
                new AccesoColaboradorResponse(
                    usuario.Id,
                    usuario.Email,
                    usuario.EntraOid,
                    usuario.EstadoAcceso,
                    UsuarioReutilizado: existente is not null,
                    sucursal.EmpresaId,
                    rolId,
                    sucursal.Id));
        }, cancellationToken);
    }

    private static CrearEmpleadoCommand CrearEmpleado(
        AltaColaboradorCommand command, Guid empresaId, Guid? usuarioId) => new(
            command.Id,
            empresaId,
            command.Clave,
            command.Nombre,
            Email: string.IsNullOrWhiteSpace(command.CorreoCorporativo) ? null : command.CorreoCorporativo.Trim(),
            PuestoId: command.PuestoId,
            JefeDirectoId: command.JefeDirectoId,
            SucursalId: command.SucursalId,
            DepartamentoId: command.DepartamentoId,
            UsuarioId: usuarioId,
            CodigoNomina: command.CodigoNomina,
            EmailContacto: string.IsNullOrWhiteSpace(command.EmailContacto) ? null : command.EmailContacto.Trim());

    private async Task<Guid> ResolverRolAsync(AltaColaboradorCommand command, CancellationToken ct)
    {
        if (command.RolId is Guid rolId) return rolId;

        var sugerido = await _compartido.Puestos.AsNoTracking()
            .Where(p => p.Id == command.PuestoId)
            .Select(p => p.RolSugeridoId)
            .FirstOrDefaultAsync(ct);
        return sugerido ?? throw new BusinessRuleException(
            "ALTA_ROL_REQUERIDO",
            "Indica el rol: el puesto no tiene un rol sugerido.");
    }

    /// <summary>
    /// Camino B: el correo nuevo tiene que estar libre en el directorio y en
    /// el ERP (misma regla que <c>puedeCrearCuentaNueva</c> del wizard, pero
    /// sin confiar en él).
    /// </summary>
    private async Task ValidarCorreoDisponibleAsync(string correo, CancellationToken ct)
    {
        if (!_entraOptions.CurrentValue.PermiteCorreo(correo))
        {
            throw new BusinessRuleException(
                "ENTRA_DOMINIO_NO_PERMITIDO",
                $"El dominio de '{correo}' no está permitido para cuentas corporativas.");
        }

        if (await _directorio.BuscarPorCorreoAsync(correo, ct) is not null)
        {
            throw new ConflictException(
                "ENTRA_UPN_EN_USO",
                $"Ya existe una cuenta Microsoft con el correo '{correo}'. Usa \"Ya tiene cuenta Microsoft\".");
        }

        var usuarioConCorreo = await _identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => u.Email == correo, ct);
        if (usuarioConCorreo)
        {
            throw new ConflictException(
                "USUARIO_EMAIL_DUPLICADO",
                $"Ya existe un usuario del ERP con el correo '{correo}'.");
        }
    }

    /// <summary>
    /// Vuelve a validar el correo contra el directorio (no se confía en la
    /// validación en línea del wizard).
    /// </summary>
    private async Task<CuentaEntra> ResolverCuentaEntraAsync(string correo, CancellationToken ct)
    {
        if (!_entraOptions.CurrentValue.PermiteCorreo(correo))
        {
            throw new BusinessRuleException(
                "ENTRA_DOMINIO_NO_PERMITIDO",
                $"El dominio de '{correo}' no está permitido para cuentas corporativas.");
        }

        var cuenta = await _directorio.BuscarPorCorreoAsync(correo, ct)
            ?? throw new BusinessRuleException(
                "ENTRA_CUENTA_NO_ENCONTRADA",
                $"No existe una cuenta Microsoft con el correo '{correo}'.");
        if (!cuenta.Habilitada)
        {
            throw new BusinessRuleException(
                "ENTRA_CUENTA_DESHABILITADA",
                $"La cuenta Microsoft '{correo}' está deshabilitada.");
        }
        return cuenta;
    }

    /// <summary>
    /// Usuario del ERP con ese correo o con el OID de la cuenta, si se puede
    /// reutilizar. Tracked: el alta le vincula el OID real y el departamento.
    /// </summary>
    private async Task<Usuario?> BuscarUsuarioReutilizableAsync(
        string correo, CuentaEntra cuenta, CancellationToken ct)
    {
        var candidatos = await _identidad.Usuarios.IgnoreQueryFilters()
            .Where(u => u.Email == correo || u.EntraOid == cuenta.ObjectId)
            .ToListAsync(ct);
        if (candidatos.Count == 0) return null;
        if (candidatos.Count > 1)
        {
            throw new ConflictException(
                "USUARIO_CORREO_OID_INCONSISTENTE",
                $"El correo '{correo}' y la cuenta Microsoft pertenecen a usuarios distintos del ERP.");
        }

        var usuario = candidatos[0];
        if (usuario.EsCuentaTecnica)
        {
            throw new ConflictException(
                "USUARIO_ES_CUENTA_TECNICA",
                $"'{correo}' es una cuenta técnica y no puede vincularse a un empleado.");
        }
        if (!usuario.Activo)
        {
            throw new ConflictException(
                "USUARIO_INACTIVO",
                $"El usuario '{correo}' está desactivado; reactívalo antes de vincularlo.");
        }

        var vinculado = await _compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(e => e.UsuarioId == usuario.Id, ct);
        if (vinculado)
        {
            throw new ConflictException(
                "USUARIO_YA_VINCULADO",
                $"El usuario '{correo}' ya está vinculado a otro empleado.");
        }
        return usuario;
    }
}
