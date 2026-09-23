using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Empleado del catálogo <c>compartido.empleados</c>
/// (doc 10-catalogo-puestos-empleados, ADM-PR1). Master de personas para
/// reglas de negocio por empleado: solicitante de viáticos (puesto → tope,
/// jefe directo → autorizador N1), responsable de comprobaciones de
/// gastos, titular de TC empresarial.
///
/// <para>
/// <b>No es el padrón de nómina de RH</b>: los préstamos formales
/// (códigos <c>Axxxx</c>) viven fuera del ERP; <see cref="CodigoNomina"/>
/// es solo referencia sin lógica (decisión D4). El vínculo con Identidad
/// es opcional vía <see cref="UsuarioId"/> (D5), sin FK cross-módulo.
/// </para>
/// </summary>
public sealed class Empleado : BaseEntity, IAuditable
{
    public Guid EmpresaId { get; private set; }

    /// <summary>Business key inmutable, única por empresa.</summary>
    public string Clave { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;
    public string? Email { get; private set; }

    /// <summary>
    /// Correo personal de contacto (alta unificada, plan 15 D1): destino
    /// del acceso inicial cuando el ERP crea la cuenta corporativa en
    /// Entra ID. No es el correo de inicio de sesión.
    /// </summary>
    public string? EmailContacto { get; private set; }

    /// <summary>
    /// FK a <see cref="Puesto"/>. Nullable en el alta (empleados sin regla
    /// de viáticos), pero requerido por CxP para validar el tope de la
    /// política al solicitar viáticos.
    /// </summary>
    public Guid? PuestoId { get; private set; }

    /// <summary>
    /// Self-FK: jefe directo (autorizador N1 de viáticos, decisión D2).
    /// La solicitud lo prellena; el form permite override manual.
    /// </summary>
    public Guid? JefeDirectoId { get; private set; }

    public Guid? SucursalId { get; private set; }

    /// <summary>Lo consume el <c>IEmpleadoReadPort</c> de Almacén.</summary>
    public Guid? DepartamentoId { get; private set; }

    /// <summary>
    /// Correlación opcional con el Usuario de Identidad del empleado
    /// (captura su propia comprobación). Sin FK física (cross-módulo).
    /// </summary>
    public Guid? UsuarioId { get; private set; }

    /// <summary>Código de nómina/SAP (<c>Axxxx</c>), solo referencia.</summary>
    public string? CodigoNomina { get; private set; }

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Empleado() { }

    public Empleado(
        Guid id,
        Guid empresaId,
        string clave,
        string nombre,
        string? email = null,
        Guid? puestoId = null,
        Guid? jefeDirectoId = null,
        Guid? sucursalId = null,
        Guid? departamentoId = null,
        Guid? usuarioId = null,
        string? codigoNomina = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? emailContacto = null) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("EMPLEADO_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("EMPLEADO_EMPRESA_INVALIDA", "La empresa es obligatoria.");
        ValidarClave(clave);
        ValidarNombre(nombre);
        ValidarEmail(email);
        ValidarEmailContacto(emailContacto);
        ValidarCodigoNomina(codigoNomina);
        ValidarJefe(id, jefeDirectoId);

        EmpresaId = empresaId;
        Clave = clave;
        Nombre = nombre;
        Email = Normalizar(email);
        EmailContacto = Normalizar(emailContacto);
        PuestoId = puestoId;
        JefeDirectoId = jefeDirectoId;
        SucursalId = sucursalId;
        DepartamentoId = departamentoId;
        UsuarioId = usuarioId;
        CodigoNomina = Normalizar(codigoNomina);
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial. Convención: parámetro <c>null</c> = no tocar; los
    /// flags <c>limpiar*</c> ponen el campo nullable en <c>null</c>.
    /// Inmutables: <see cref="Clave"/> (business key) y
    /// <see cref="EmpresaId"/>.
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? email = null,
        bool limpiarEmail = false,
        Guid? puestoId = null,
        bool limpiarPuesto = false,
        Guid? jefeDirectoId = null,
        bool limpiarJefeDirecto = false,
        Guid? sucursalId = null,
        bool limpiarSucursal = false,
        Guid? departamentoId = null,
        bool limpiarDepartamento = false,
        Guid? usuarioId = null,
        bool limpiarUsuario = false,
        string? codigoNomina = null,
        bool limpiarCodigoNomina = false,
        string? emailContacto = null,
        bool limpiarEmailContacto = false)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }

        if (limpiarEmail) Email = null;
        else if (email is not null)
        {
            ValidarEmail(email);
            Email = Normalizar(email);
        }

        if (limpiarPuesto) PuestoId = null;
        else if (puestoId is not null) PuestoId = puestoId;

        if (limpiarJefeDirecto) JefeDirectoId = null;
        else if (jefeDirectoId is not null)
        {
            ValidarJefe(Id, jefeDirectoId);
            JefeDirectoId = jefeDirectoId;
        }

        if (limpiarSucursal) SucursalId = null;
        else if (sucursalId is not null) SucursalId = sucursalId;

        if (limpiarDepartamento) DepartamentoId = null;
        else if (departamentoId is not null) DepartamentoId = departamentoId;

        if (limpiarUsuario) UsuarioId = null;
        else if (usuarioId is not null) UsuarioId = usuarioId;

        if (limpiarCodigoNomina) CodigoNomina = null;
        else if (codigoNomina is not null)
        {
            ValidarCodigoNomina(codigoNomina);
            CodigoNomina = Normalizar(codigoNomina);
        }

        if (limpiarEmailContacto) EmailContacto = null;
        else if (emailContacto is not null)
        {
            ValidarEmailContacto(emailContacto);
            EmailContacto = Normalizar(emailContacto);
        }
    }

    /// <summary>Reactiva al empleado (recontratación). Idempotente.</summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva al empleado (baja). Las referencias históricas (viáticos,
    /// comprobaciones) se conservan.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("EMPLEADO_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("EMPLEADO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }

    private static void ValidarEmail(string? email)
    {
        if (email is not null
            && (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254))
            throw new BusinessRuleException("EMPLEADO_EMAIL_INVALIDO",
                "El email no puede ser vacío ni exceder 254 caracteres.");
    }

    private static void ValidarEmailContacto(string? emailContacto)
    {
        if (emailContacto is not null
            && (string.IsNullOrWhiteSpace(emailContacto) || emailContacto.Trim().Length > 254))
            throw new BusinessRuleException("EMPLEADO_EMAIL_CONTACTO_INVALIDO",
                "El email de contacto no puede ser vacío ni exceder 254 caracteres.");
    }

    private static void ValidarCodigoNomina(string? codigoNomina)
    {
        if (codigoNomina is not null
            && (string.IsNullOrWhiteSpace(codigoNomina) || codigoNomina.Trim().Length > 20))
            throw new BusinessRuleException("EMPLEADO_CODIGO_NOMINA_INVALIDO",
                "El código de nómina no puede ser vacío ni exceder 20 caracteres.");
    }

    private static void ValidarJefe(Guid id, Guid? jefeDirectoId)
    {
        if (jefeDirectoId == id)
            throw new BusinessRuleException("EMPLEADO_JEFE_INVALIDO",
                "Un empleado no puede ser su propio jefe directo.");
    }

    private static string? Normalizar(string? valor) => valor?.Trim();
}
