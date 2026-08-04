using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>
/// Aggregate root del módulo Compras / submódulo Requisiciones. Modela una
/// solicitud de compra de no-producción desde su captura (Borrador) hasta
/// su cierre o terminación. La state machine y las invariantes están
/// documentadas en el diseño §5 (transiciones) y §10.1 (estructura).
///
/// F1-PR1 introduce solo la cabecera: campos estructurales + estado inicial
/// <c>Borrador</c>, sin métodos de transición. Las transiciones y entidades
/// hijas (<c>LineaRequisicion</c>, <c>Autorizacion</c>) entran en F2.
///
/// Implementa:
/// <list type="bullet">
///   <item><see cref="IPerteneceAEmpresa"/>: query filter por empresa (ADR-0011).</item>
///   <item><see cref="IAuditable"/>: registro automático en <c>core.audit_log</c> (ADR-0008).</item>
///   <item><see cref="IFiscalmenteRelevante"/>: soft delete vía <c>DeletedAt</c> (ADR-0008).</item>
/// </list>
/// </summary>
public sealed class Requisicion : BaseEntity, IPerteneceAEmpresa, IAuditable, IFiscalmenteRelevante
{
    public Guid EmpresaId { get; set; }

    public Folio Folio { get; private set; } = default!;

    /// <summary>Año del folio. Junto con el valor del folio forma el UNIQUE de la tabla.</summary>
    public short FolioAnio { get; private set; }

    public Clasificacion Clasificacion { get; private set; }

    public Guid SucursalId { get; private set; }
    public Guid DepartamentoId { get; private set; }

    /// <summary>
    /// Almacén-por-línea PR3: NULLABLE. La RQ manual (capturada por el
    /// requisitante) nace con este campo en <c>null</c> — el usuario ya no
    /// captura almacén. Solo el motor de reorden (ADR-0047, RQ de origen
    /// <see cref="OrigenRequisicion.Sistema"/>) lo puebla. No se dropea:
    /// el reorden lo sigue usando (dedup del "pedido vivo").
    /// </summary>
    public Guid? AlmacenDestinoId { get; private set; }

    /// <summary>Usuario para quien se crea la requisición.</summary>
    public Guid RequisitanteId { get; private set; }

    /// <summary>
    /// Usuario que capturó la fila. Puede diferir del <see cref="RequisitanteId"/>
    /// si quien capturó tiene permiso <c>compras.requisiciones.seleccionar-requisitante</c>
    /// (delegación). Semántico, no redundante con <c>CreatedBy</c> del framework
    /// de auditoría — ese registra el HTTP user.
    /// </summary>
    public Guid CreadorId { get; private set; }

    public string? Descripcion { get; private set; }

    public Prioridad Prioridad { get; private set; }

    public DateTimeOffset FechaSolicitud { get; private set; }

    public DateOnly? FechaEntregaDeseada { get; private set; }

    public Guid? ProveedorSugeridoId { get; private set; }

    public EstadoRequisicion Estado { get; private set; }

    /// <summary>
    /// Origen de la RQ (ADR-0047 PR5): <see cref="OrigenRequisicion.Manual"/> (default,
    /// captura humana) o <see cref="OrigenRequisicion.Sistema"/> (motor de reorden).
    /// Toda RQ existente/manual queda Manual; PR5.C setea Sistema al generar la RQ
    /// automática. El cálculo del faltante (PR5.B) solo cuenta como "lo vivo" los
    /// documentos de origen Sistema.
    /// </summary>
    public OrigenRequisicion Origen { get; private set; } = OrigenRequisicion.Manual;

    // --- Campos de terminación (Rechazada / Eliminada / Cancelada) ---
    // F1-PR1 los expone como NULL; los métodos que los llenan (Rechazar,
    // Eliminar, Cancelar) entran en F2.
    public Guid? MotivoTerminacionId { get; private set; }
    public string? MotivoTerminacionTexto { get; private set; }
    public Guid? ActorTerminacionId { get; private set; }
    public DateTimeOffset? FechaTerminacion { get; private set; }

    // --- F4-PR1 (OC): compromiso exclusivo con una OC activa ---
    // Diseño OC §3.bis.1. Una RQ Autorizada puede estar en uno de dos
    // lugares: el pool de disponibles (ComprometidaEnOcId = null) o
    // comprometida en una OC activa (Borrador / EnAutorización /
    // Autorizada). Se setea al agregar línea con FK a esta RQ y se
    // limpia al eliminar la última línea o cancelar la OC en estado
    // sin recepciones. El listener cross-aggregate vive en F4-PR2.

    /// <summary>FK opcional a la OC activa que tiene comprometida esta RQ. Null = disponible en el pool.</summary>
    public Guid? ComprometidaEnOcId { get; private set; }

    // --- Líneas (collection navigation) ---
    private readonly List<LineaRequisicion> _lineas = [];
    public IReadOnlyCollection<LineaRequisicion> Lineas => _lineas.AsReadOnly();

    // --- Autorizaciones (collection navigation) ---
    private readonly List<Autorizacion> _autorizaciones = [];
    public IReadOnlyCollection<Autorizacion> Autorizaciones => _autorizaciones.AsReadOnly();

    /// <summary>Constructor para EF Core.</summary>
    private Requisicion() { }

    /// <summary>
    /// Construye una requisición nueva en estado <see cref="EstadoRequisicion.Borrador"/>.
    /// Aplica invariantes estructurales: GUIDs no vacíos, descripción acotada,
    /// folio ya parseado por el caller.
    /// </summary>
    public Requisicion(
        Guid id,
        Guid empresaId,
        Folio folio,
        short folioAnio,
        Clasificacion clasificacion,
        Guid sucursalId,
        Guid departamentoId,
        Guid? almacenDestinoId,
        Guid requisitanteId,
        Guid creadorId,
        Prioridad prioridad,
        DateTimeOffset fechaSolicitud,
        DateOnly? fechaEntregaDeseada = null,
        Guid? proveedorSugeridoId = null,
        string? descripcion = null,
        OrigenRequisicion origen = OrigenRequisicion.Manual) : base(id)
    {
        EnsureNotEmpty(empresaId, nameof(empresaId));
        EnsureNotEmpty(sucursalId, nameof(sucursalId));
        EnsureNotEmpty(departamentoId, nameof(departamentoId));
        // PR3: almacenDestinoId es nullable (RQ manual = null). Si viene con
        // valor (RQ de sistema/reorden), no puede ser Guid.Empty.
        if (almacenDestinoId is Guid almacenNoNulo)
        {
            EnsureNotEmpty(almacenNoNulo, nameof(almacenDestinoId));
        }
        EnsureNotEmpty(requisitanteId, nameof(requisitanteId));
        EnsureNotEmpty(creadorId, nameof(creadorId));

        if (folioAnio < 2000 || folioAnio > 2100)
        {
            throw new BusinessRuleException(
                "FOLIO_ANIO_INVALIDO",
                $"Año de folio fuera de rango razonable (2000-2100): {folioAnio}.");
        }

        if (descripcion is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "DESCRIPCION_DEMASIADO_LARGA",
                "La descripción no puede exceder 500 caracteres.");
        }

        EmpresaId = empresaId;
        Folio = folio ?? throw new BusinessRuleException(
            "FOLIO_REQUERIDO",
            "El folio es requerido para crear una requisición.");
        FolioAnio = folioAnio;
        Clasificacion = clasificacion;
        SucursalId = sucursalId;
        DepartamentoId = departamentoId;
        AlmacenDestinoId = almacenDestinoId;
        RequisitanteId = requisitanteId;
        CreadorId = creadorId;
        Prioridad = prioridad;
        FechaSolicitud = fechaSolicitud;
        FechaEntregaDeseada = fechaEntregaDeseada;
        ProveedorSugeridoId = proveedorSugeridoId;
        Descripcion = descripcion;
        Estado = EstadoRequisicion.Borrador;
        Origen = origen;
    }

    // --- Métodos de manipulación de líneas (F2-PR1) ---

    private static readonly EstadoRequisicion[] EstadosTerminales =
    [
        EstadoRequisicion.Cerrada,
        EstadoRequisicion.Cancelada,
        EstadoRequisicion.Rechazada,
        EstadoRequisicion.Eliminada,
        // ADR-0043: terminales de cierre manual del jefe de almacén. Aditivos
        // en el PR #1 (ningún flujo transiciona a ellos todavía); el cierre
        // manual que los alcanza llega en el PR #2.
        EstadoRequisicion.CerradaSinSurtir,
        EstadoRequisicion.CerradaSurtidaParcial,
    ];

    /// <summary>
    /// Editar cabecera (B.4): muta los campos editables en estado
    /// <see cref="EstadoRequisicion.Borrador"/>. Cualquier parámetro
    /// nullable que llegue como <c>null</c> NO se modifica (PATCH
    /// parcial). Para limpiar un nullable a null se usan los flags
    /// <c>limpiarX</c>. Los inmutables (<c>EmpresaId</c>,
    /// <c>SucursalId</c>, <c>DepartamentoId</c>,
    /// <c>AlmacenDestinoId</c>, <c>RequisitanteId</c>) requieren
    /// recrear la RQ.
    /// </summary>
    public void EditarCabecera(
        string? descripcion,
        DateOnly? fechaEntregaDeseada,
        Prioridad? prioridad,
        Guid? proveedorSugeridoId,
        Clasificacion? clasificacion,
        bool limpiarDescripcion = false,
        bool limpiarFechaEntregaDeseada = false,
        bool limpiarProveedorSugeridoId = false)
    {
        if (Estado != EstadoRequisicion.Borrador)
        {
            throw new BusinessRuleException(
                "EDITAR_CABECERA_SOLO_EN_BORRADOR",
                "La cabecera solo puede editarse en estado Borrador.");
        }

        // Nullables editables: la convención es "si llega null y limpiar=false,
        // no toques; si llega valor, asigna; si limpiar=true, set a null".
        if (descripcion is not null) Descripcion = descripcion;
        else if (limpiarDescripcion) Descripcion = null;

        if (fechaEntregaDeseada is not null) FechaEntregaDeseada = fechaEntregaDeseada;
        else if (limpiarFechaEntregaDeseada) FechaEntregaDeseada = null;

        if (proveedorSugeridoId is not null) ProveedorSugeridoId = proveedorSugeridoId;
        else if (limpiarProveedorSugeridoId) ProveedorSugeridoId = null;

        // No-nullables: solo asignar si llega valor.
        if (prioridad is Prioridad p) Prioridad = p;
        if (clasificacion is Clasificacion c) Clasificacion = c;
    }

    /// <summary>
    /// Agrega una línea a la requisición. Solo permitido en
    /// <see cref="EstadoRequisicion.Borrador"/>. La <see cref="LineaRequisicion.Posicion"/>
    /// se asigna automáticamente como la siguiente disponible.
    /// </summary>
    public LineaRequisicion AgregarLinea(
        Guid lineaId,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        Money precioEstimado,
        Guid? cuentaContableId = null,
        Guid? centroCostoId = null,
        string? proyecto = null,
        DateOnly? fechaRequerida = null,
        string? notas = null)
    {
        if (Estado != EstadoRequisicion.Borrador)
        {
            throw new BusinessRuleException(
                "LINEAS_SOLO_EN_BORRADOR",
                $"Solo se pueden agregar líneas en estado Borrador (actual: {Estado}).");
        }

        var siguientePosicion = _lineas.Count == 0
            ? (short)1
            : (short)(_lineas.Max(l => l.Posicion) + 1);

        var linea = new LineaRequisicion(
            id: lineaId,
            requisicionId: Id,
            posicion: siguientePosicion,
            articuloId: articuloId,
            cantidad: cantidad,
            unidadMedida: unidadMedida,
            precioEstimado: precioEstimado,
            cuentaContableId: cuentaContableId,
            centroCostoId: centroCostoId,
            proyecto: proyecto,
            fechaRequerida: fechaRequerida,
            notas: notas);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>
    /// Modifica los campos estructurales de una línea (artículo, cantidad,
    /// precio, etc.). Permitido solo en <see cref="EstadoRequisicion.Borrador"/>
    /// y solo si la línea aún no tiene cubrimiento (§4.2 invariante).
    /// </summary>
    public void ActualizarLineaEstructural(
        Guid lineaId,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        Money precioEstimado,
        Guid? cuentaContableId = null,
        Guid? centroCostoId = null,
        string? proyecto = null,
        DateOnly? fechaRequerida = null)
    {
        if (Estado != EstadoRequisicion.Borrador)
        {
            throw new BusinessRuleException(
                "LINEAS_SOLO_EN_BORRADOR",
                $"Solo se pueden modificar líneas estructuralmente en estado Borrador (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        linea.ActualizarEstructural(
            articuloId, cantidad, unidadMedida, precioEstimado,
            cuentaContableId, centroCostoId, proyecto, fechaRequerida);
    }

    /// <summary>
    /// Actualiza solo las notas de una línea. Permitido en cualquier
    /// estado **no terminal** (§4.2 invariante; útil para anotaciones
    /// operativas durante el flujo).
    /// </summary>
    public void ActualizarLineaNotas(Guid lineaId, string? notas)
    {
        if (EstadosTerminales.Contains(Estado))
        {
            throw new BusinessRuleException(
                "NOTAS_NO_EN_ESTADO_TERMINAL",
                $"No se pueden editar las notas de una requisición en estado terminal (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        linea.ActualizarNotas(notas);
    }

    /// <summary>
    /// Elimina una línea. Solo permitido en <see cref="EstadoRequisicion.Borrador"/>.
    /// </summary>
    public void EliminarLinea(Guid lineaId)
    {
        if (Estado != EstadoRequisicion.Borrador)
        {
            throw new BusinessRuleException(
                "LINEAS_SOLO_EN_BORRADOR",
                $"Solo se pueden eliminar líneas en estado Borrador (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        _lineas.Remove(linea);
    }

    private LineaRequisicion BuscarLineaOLanzar(Guid lineaId)
    {
        var linea = _lineas.FirstOrDefault(l => l.Id == lineaId)
            ?? throw new BusinessRuleException(
                "LINEA_NO_ENCONTRADA",
                $"La requisición no contiene la línea {lineaId}.");
        return linea;
    }

    // --- F2-PR3: transmitir y autorizar ---

    /// <summary>
    /// Transición <c>Borrador → EnAutorizacion</c>. Valida que haya al
    /// menos una línea. Devuelve un evento de dominio que el handler
    /// publica vía MediatR después de <c>SaveChanges</c>.
    /// </summary>
    public Events.RequisicionEnviadaAAutorizacionEvent EnviarAAutorizacion(DateTimeOffset ocurridoEn)
    {
        if (Estado != EstadoRequisicion.Borrador)
        {
            throw new BusinessRuleException(
                "TRANSMITIR_SOLO_DESDE_BORRADOR",
                $"Solo se puede transmitir desde Borrador (actual: {Estado}).");
        }

        if (_lineas.Count == 0)
        {
            throw new BusinessRuleException(
                "TRANSMITIR_SIN_LINEAS",
                "La requisición debe tener al menos una línea para enviarse a autorización.");
        }

        Estado = EstadoRequisicion.EnAutorizacion;

        return new Events.RequisicionEnviadaAAutorizacionEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            Folio: Folio.Valor,
            OcurridoEn: ocurridoEn);
    }

    /// <summary>
    /// Registra una autorización. Invariantes:
    /// <list type="bullet">
    ///   <item>Estado debe ser <see cref="EstadoRequisicion.EnAutorizacion"/>.</item>
    ///   <item>No puede existir ya una autorización para el mismo nivel.</item>
    ///   <item><see cref="NivelAutorizacion.Nivel2"/> requiere que ya exista <see cref="NivelAutorizacion.Nivel1"/>.</item>
    /// </list>
    ///
    /// El parámetro <paramref name="requiereNivel"/> viene del evaluator
    /// de matriz (<see cref="Matriz.IRequiereNivelEvaluator"/>) y permite
    /// que el agregado decida si la autorización registrada cumple la
    /// matriz; si la cumple, transiciona a <see cref="EstadoRequisicion.Autorizada"/>.
    /// </summary>
    public RegistrarAutorizacionResultado RegistrarAutorizacion(
        Guid autorizacionId,
        NivelAutorizacion nivel,
        Guid usuarioId,
        DateTimeOffset fechaHora,
        Matriz.RequiereNivel requiereNivel,
        string? notas = null)
    {
        if (Estado != EstadoRequisicion.EnAutorizacion)
        {
            throw new BusinessRuleException(
                "AUTORIZAR_SOLO_EN_AUTORIZACION",
                $"Solo se puede autorizar en estado EnAutorizacion (actual: {Estado}).");
        }

        if (_autorizaciones.Any(a => a.Nivel == nivel))
        {
            throw new BusinessRuleException(
                "AUTORIZACION_NIVEL_DUPLICADO",
                $"Ya existe una autorización de nivel {nivel} para esta requisición.");
        }

        if (nivel == NivelAutorizacion.Nivel2
            && !_autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1))
        {
            throw new BusinessRuleException(
                "AUTORIZACION_NIVEL2_SIN_NIVEL1",
                "Nivel2 requiere que primero exista Nivel1.");
        }

        var autorizacion = new Autorizacion(
            id: autorizacionId,
            requisicionId: Id,
            nivel: nivel,
            usuarioId: usuarioId,
            fechaHora: fechaHora,
            notas: notas);

        _autorizaciones.Add(autorizacion);

        // Si las autorizaciones registradas cumplen la matriz, transiciona
        // a Autorizada y emite el evento que el handler (F4-PR2) publica
        // post-commit. F4-PR4 wirea handlers in-proc del evento.
        Events.MatrizAprobacionSatisfechaEvent? matrizEvento = null;
        if (CumpleMatriz(requiereNivel))
        {
            Estado = EstadoRequisicion.Autorizada;
            matrizEvento = new Events.MatrizAprobacionSatisfechaEvent(
                RequisicionId: Id,
                EmpresaId: EmpresaId,
                OcurridoEn: fechaHora);
        }

        return new RegistrarAutorizacionResultado(autorizacion, matrizEvento);
    }

    private bool CumpleMatriz(Matriz.RequiereNivel requiereNivel)
    {
        return requiereNivel switch
        {
            Matriz.RequiereNivel.SoloN1 =>
                _autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1),
            Matriz.RequiereNivel.N1YN2 =>
                _autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1)
                && _autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel2),
            _ => false,
        };
    }

    // --- F2-PR4: rechazar y eliminar ---

    /// <summary>
    /// Rechaza una requisición desde <see cref="EstadoRequisicion.EnAutorizacion"/>
    /// → <see cref="EstadoRequisicion.Rechazada"/> (terminal). Llena los
    /// campos de terminación. La validación cross-table del motivo
    /// (existencia, activo, bitmask aplica_a, texto libre) la hace el
    /// handler de Application; el agregado solo aplica los valores.
    /// </summary>
    public Events.RequisicionRechazadaEvent Rechazar(
        Guid motivoId,
        Guid actorId,
        DateTimeOffset fechaHora,
        string? motivoTexto = null)
    {
        if (Estado != EstadoRequisicion.EnAutorizacion)
        {
            throw new BusinessRuleException(
                "RECHAZAR_SOLO_DESDE_EN_AUTORIZACION",
                $"Solo se puede rechazar una requisición en EnAutorizacion (actual: {Estado}).");
        }

        AplicarTerminacion(EstadoRequisicion.Rechazada, motivoId, actorId, fechaHora, motivoTexto);

        return new Events.RequisicionRechazadaEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            MotivoId: motivoId,
            MotivoTexto: motivoTexto,
            ActorId: actorId,
            OcurridoEn: fechaHora);
    }

    /// <summary>
    /// Elimina una requisición pre-autorización (Borrador o EnAutorizacion)
    /// → <see cref="EstadoRequisicion.Eliminada"/> (terminal). Llena los
    /// campos de terminación. NO toca <see cref="BaseEntity.DeletedAt"/>:
    /// el estado <c>Eliminada</c> es la marca de "eliminada por flujo de
    /// negocio"; las RQs siguen siendo visibles en bandejas (filtradas
    /// por estado, no por DeletedAt). El soft delete del framework queda
    /// para purgas administrativas, fuera del scope.
    /// </summary>
    public Events.RequisicionEliminadaEvent Eliminar(
        Guid motivoId,
        Guid actorId,
        DateTimeOffset fechaHora,
        string? motivoTexto = null)
    {
        if (Estado != EstadoRequisicion.Borrador && Estado != EstadoRequisicion.EnAutorizacion)
        {
            throw new BusinessRuleException(
                "ELIMINAR_SOLO_DESDE_BORRADOR_O_EN_AUTORIZACION",
                $"Solo se puede eliminar desde Borrador o EnAutorizacion (actual: {Estado}).");
        }

        AplicarTerminacion(EstadoRequisicion.Eliminada, motivoId, actorId, fechaHora, motivoTexto);

        return new Events.RequisicionEliminadaEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            MotivoId: motivoId,
            MotivoTexto: motivoTexto,
            ActorId: actorId,
            OcurridoEn: fechaHora);
    }

    // --- F4-PR3: cancelar ---

    /// <summary>
    /// Cancela una requisición post-autorización (<see cref="EstadoRequisicion.Autorizada"/>
    /// o <see cref="EstadoRequisicion.EnSurtido"/>) → <see cref="EstadoRequisicion.Cancelada"/>
    /// (terminal). Llena los campos de terminación y devuelve el
    /// <see cref="Events.RequisicionCanceladaEvent"/> que el handler
    /// publica post-commit.
    ///
    /// <para>
    /// El handler invoca este método dentro de la TX que también libera
    /// reservas en Almacén y aborta OC borrador (cross-port atómico).
    /// La validación cross-table del motivo (activo, <c>aplica_a</c>
    /// incluye <c>Cancelacion</c>, texto si <c>permite_texto_libre</c>)
    /// vive en el handler, no aquí.
    /// </para>
    /// </summary>
    public Events.RequisicionCanceladaEvent Cancelar(
        Guid motivoId,
        Guid actorId,
        DateTimeOffset fechaHora,
        string? motivoTexto = null)
    {
        if (Estado != EstadoRequisicion.Autorizada && Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "CANCELAR_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO",
                $"Solo se puede cancelar desde Autorizada o EnSurtido (actual: {Estado}).");
        }

        AplicarTerminacion(EstadoRequisicion.Cancelada, motivoId, actorId, fechaHora, motivoTexto);

        return new Events.RequisicionCanceladaEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            MotivoId: motivoId,
            MotivoTexto: motivoTexto,
            ActorId: actorId,
            OcurridoEn: fechaHora);
    }

    /// <summary>
    /// Cierre manual del jefe de almacén / almacenista (ADR-0043 R3): cierra
    /// una RQ autorizada o en surtido que el requisitante ya no necesita. El
    /// estado terminal se <b>deriva de lo entregado</b>:
    /// <list type="bullet">
    ///   <item>alguna línea con <c>CantidadEntregada &gt; 0</c> →
    ///     <see cref="EstadoRequisicion.CerradaSurtidaParcial"/>;</item>
    ///   <item>nada entregado →
    ///     <see cref="EstadoRequisicion.CerradaSinSurtir"/>.</item>
    /// </list>
    ///
    /// <para>Mismos orígenes que <see cref="Cancelar"/> (<c>Autorizada</c> /
    /// <c>EnSurtido</c>). El material no entregado queda como stock libre. El
    /// handler libera las reservas del tramo de stock (<c>ReservaId</c>) en la
    /// misma TX — el material recibido por compra no tiene reserva (R2,
    /// diferida), así que no se toca. La validación cross-table del motivo
    /// (activo, <c>aplica_a</c> incluye <c>CierreManual</c>, texto si
    /// <c>permite_texto_libre</c>) vive en el handler.</para>
    /// </summary>
    public Events.RequisicionCerradaManualmenteEvent CerrarManual(
        Guid motivoId,
        Guid actorId,
        DateTimeOffset fechaHora,
        string? motivoTexto = null)
    {
        if (Estado != EstadoRequisicion.Autorizada && Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "CIERRE_MANUAL_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO",
                $"El cierre manual solo procede desde Autorizada o EnSurtido (actual: {Estado}).");
        }

        var terminal = _lineas.Any(l => l.CantidadEntregada > 0)
            ? EstadoRequisicion.CerradaSurtidaParcial
            : EstadoRequisicion.CerradaSinSurtir;

        AplicarTerminacion(terminal, motivoId, actorId, fechaHora, motivoTexto);

        return new Events.RequisicionCerradaManualmenteEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            EstadoFinal: terminal,
            MotivoId: motivoId,
            MotivoTexto: motivoTexto,
            ActorId: actorId,
            OcurridoEn: fechaHora);
    }

    // --- F5-PR1: registrar recepción + cierre automático ---

    /// <summary>
    /// Registra una recepción incremental de material desde OC sobre
    /// la línea indicada. Solo permitido desde
    /// <see cref="EstadoRequisicion.EnSurtido"/> (alineado con diseño
    /// §5.2: las recepciones cierran líneas que estaban esperando OC).
    ///
    /// <para>
    /// Tras aplicar la recepción, si todas las líneas tienen
    /// <c>CantidadPendiente == 0</c> (cubrimiento completo: almacén +
    /// recibido cubre la cantidad original), la requisición transiciona
    /// a <see cref="EstadoRequisicion.Cerrada"/> y se devuelve un
    /// <see cref="Events.RequisicionCerradaEvent"/> que el handler
    /// publica post-commit. Si todavía queda pendiente, devuelve
    /// <c>null</c> y el estado sigue en <c>EnSurtido</c>.
    /// </para>
    /// </summary>
    public Events.RequisicionCerradaEvent? RegistrarRecepcion(
        Guid lineaId,
        decimal cantidadRecibida,
        DateTimeOffset ocurridoEn)
    {
        if (Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "RECEPCION_SOLO_DESDE_ENSURTIDO",
                $"Solo se pueden registrar recepciones en EnSurtido (actual: {Estado}).");
        }

        var linea = BuscarLineaOLanzar(lineaId);
        linea.RegistrarRecepcion(cantidadRecibida);

        // ADR-0043 #3 (conmutación): la recepción YA NO cierra la RQ. Solo
        // acumula CantidadRecibida y la RQ se queda en EnSurtido; el único
        // auto-cierre es por entrega total (Requisicion.RegistrarEntrega), que
        // se cumple R1 (Cerrada = todo entregado al solicitante). Se mantiene la
        // firma `RequisicionCerradaEvent?` (devuelve siempre null) para no rizar
        // el handler ni el contrato; el retorno queda muerto hasta una limpieza
        // posterior. `ocurridoEn` se conserva por la firma.
        return null;
    }

    /// <summary>
    /// Registra una entrega incremental al solicitante sobre la línea
    /// indicada (ADR-0043, canal de entrega Almacén→Compras). Espejo de
    /// <see cref="RegistrarRecepcion"/>, pero con dos diferencias clave:
    ///
    /// <list type="number">
    ///   <item><b>Tolera cualquier estado</b> (no exige <c>EnSurtido</c>):
    ///     acumula <see cref="LineaRequisicion.CantidadEntregada"/> aunque la
    ///     RQ esté en un estado terminal. Es el camino caliente del PR #1 —
    ///     las salidas pure-stock llegan sobre RQs ya
    ///     <see cref="EstadoRequisicion.Cerrada"/> por el cierre viejo, y el
    ///     evento no debe romperse ni dead-lettear por eso.</item>
    ///   <item><b>Cierre por entrega total dormido</b>: solo transiciona a
    ///     <see cref="EstadoRequisicion.Cerrada"/> si la RQ está en
    ///     <c>EnSurtido</c> y todas las líneas cumplen
    ///     <c>CantidadEntregada &gt;= Cantidad</c>. Mientras el cierre viejo
    ///     (cubrimiento/recepción) siga activo, esta condición no se alcanza
    ///     (la recepción cierra antes); se enciende al retirarlo en el PR #3.</item>
    /// </list>
    ///
    /// <para>La entrega es robusta: si excede el techo físico disponible NO
    /// lanza, lo señala en <see cref="EntregaRegistradaResultado.ExcedioTecho"/>
    /// para que el handler emita una advertencia estructurada.</para>
    /// </summary>
    public EntregaRegistradaResultado RegistrarEntrega(
        Guid lineaId,
        decimal cantidadEntregada,
        DateTimeOffset ocurridoEn)
    {
        var linea = BuscarLineaOLanzar(lineaId);
        var (excedio, total, techo) = linea.RegistrarEntrega(cantidadEntregada);

        Events.RequisicionCerradaEvent? cierre = null;
        if (Estado == EstadoRequisicion.EnSurtido
            && _lineas.All(l => l.CantidadEntregada >= l.Cantidad))
        {
            Estado = EstadoRequisicion.Cerrada;
            cierre = new Events.RequisicionCerradaEvent(
                RequisicionId: Id,
                EmpresaId: EmpresaId,
                OcurridoEn: ocurridoEn);
        }

        return new EntregaRegistradaResultado(
            CierreEvento: cierre,
            ExcedioTecho: excedio,
            LineaId: lineaId,
            TotalEntregado: total,
            TechoDisponible: techo);
    }

    private void AplicarTerminacion(
        EstadoRequisicion nuevoEstado,
        Guid motivoId,
        Guid actorId,
        DateTimeOffset fechaHora,
        string? motivoTexto)
    {
        if (motivoId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "MOTIVO_REQUERIDO",
                "El motivo de terminación es requerido.");
        }

        if (actorId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ACTOR_REQUERIDO",
                "El usuario que termina la requisición es requerido.");
        }

        if (motivoTexto is { Length: > 500 })
        {
            throw new BusinessRuleException(
                "MOTIVO_TEXTO_DEMASIADO_LARGO",
                "El texto del motivo no puede exceder 500 caracteres.");
        }

        Estado = nuevoEstado;
        MotivoTerminacionId = motivoId;
        MotivoTerminacionTexto = motivoTexto;
        ActorTerminacionId = actorId;
        FechaTerminacion = fechaHora;
    }

    // --- F4-PR1: cubrimiento stock-aware ---

    /// <summary>
    /// Aplica el cubrimiento calculado por el handler tras consultar
    /// <c>IConsultarStockPort</c> y transiciona la requisición al estado
    /// final correspondiente:
    /// <list type="bullet">
    ///   <item>Todas las líneas con <c>CantidadPendiente == 0</c>
    ///         (stock total cubre todo) → <see cref="EstadoRequisicion.Cerrada"/>.</item>
    ///   <item>Cualquier línea con <c>CantidadDeCompra &gt; 0</c>
    ///         (hay saldo a OC) → <see cref="EstadoRequisicion.EnSurtido"/>.</item>
    ///   <item>Otro caso (defensivo, no debería ocurrir si la bifurcación
    ///         consume toda la cantidad original) → mantiene
    ///         <see cref="EstadoRequisicion.Autorizada"/>.</item>
    /// </list>
    ///
    /// <para>
    /// Solo invocable desde <see cref="EstadoRequisicion.Autorizada"/>:
    /// el handler llama a <see cref="RegistrarAutorizacion"/> primero
    /// (que cumple la matriz y transiciona a <c>Autorizada</c>) y
    /// luego a este método.
    /// </para>
    /// <para>
    /// Devuelve <see cref="Events.CubrimientoRegistradoEvent"/> que el
    /// handler publica vía MediatR después de <c>SaveChanges</c>. Los
    /// handlers in-proc del evento (logging, telemetría) se wirean en
    /// F4-PR4.
    /// </para>
    /// </summary>
    public RegistrarCubrimientoResultado RegistrarCubrimiento(
        IReadOnlyList<CubrimientoLinea> cubrimientos,
        DateTimeOffset ocurridoEn)
    {
        if (Estado != EstadoRequisicion.Autorizada)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_SOLO_DESDE_AUTORIZADA",
                $"RegistrarCubrimiento solo es válido desde Autorizada (actual: {Estado}).");
        }

        if (cubrimientos is null || cubrimientos.Count == 0)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_LINEAS_VACIO",
                "Se requiere al menos un cubrimiento de línea.");
        }

        if (cubrimientos.Count != _lineas.Count)
        {
            throw new BusinessRuleException(
                "CUBRIMIENTO_LINEAS_DESPAREJAS",
                $"Se esperaban {_lineas.Count} cubrimientos (uno por línea); se recibieron {cubrimientos.Count}.");
        }

        // Aplicar cubrimiento por línea. Si alguna falla la validación,
        // se aborta antes de mutar la siguiente (las que ya se aplicaron
        // no se persisten porque el handler todavía no llamó SaveChanges).
        foreach (var c in cubrimientos)
        {
            var linea = BuscarLineaOLanzar(c.LineaId);
            linea.RegistrarCubrimiento(c.CantidadDeAlmacen, c.CantidadDeCompra);
        }

        // ADR-0043 #3 (conmutación): el cubrimiento YA NO cierra la RQ. SIEMPRE
        // queda en EnSurtido — incluido el caso 100% stock
        // (todoCubierto && !hayCompra), que antes iba directo Autorizada →
        // Cerrada. Reencauzar ese path a EnSurtido (no dejarlo en Autorizada)
        // es clave: el cierre nuevo por entrega (Requisicion.RegistrarEntrega)
        // SOLO dispara desde EnSurtido, así que una RQ 100% stock debe pasar
        // por EnSurtido para poder cerrarse al entregarse. Cumple R1 (Cerrada =
        // todo entregado) y deja el cierre por entrega como único auto-cierre.
        Estado = EstadoRequisicion.EnSurtido;

        // El cubrimiento ya no produce cierre; CerradaEvento queda en null
        // (firma de RegistrarCubrimientoResultado intacta — limpieza posterior).
        Events.RequisicionCerradaEvent? cerradaEvento = null;

        var snapshot = _lineas
            .Select(l => new Events.CubrimientoLineaSnapshot(
                LineaId: l.Id,
                CantidadOriginal: l.Cantidad,
                CantidadDeAlmacen: l.CantidadDeAlmacen,
                CantidadDeCompra: l.CantidadDeCompra))
            .ToList();

        var cubrimientoEvento = new Events.CubrimientoRegistradoEvent(
            RequisicionId: Id,
            EmpresaId: EmpresaId,
            EstadoFinal: Estado,
            OcurridoEn: ocurridoEn,
            Lineas: snapshot);

        return new RegistrarCubrimientoResultado(cubrimientoEvento, cerradaEvento);
    }

    // --- F4-PR1 (OC): métodos de compromiso ---

    /// <summary>
    /// Marca esta RQ como comprometida en una OC activa. Permitido si
    /// está en <see cref="EstadoRequisicion.Autorizada"/> o
    /// <see cref="EstadoRequisicion.EnSurtido"/> y aún no está
    /// comprometida con otra OC.
    ///
    /// <para>
    /// Lo invoca el listener <c>RqComprometidaEnOcListener</c> cuando una
    /// OC agrega una línea con FK a esta RQ, y los handlers de
    /// <c>CrearOrdenCompraDesdeRequisicion</c> /
    /// <c>AgregarLineaDesdeRequisicion</c> al crear OC desde una RQ.
    /// </para>
    ///
    /// <para>
    /// <b>ADR-0033 (2026-05-13):</b> antes solo aceptaba
    /// <c>Autorizada</c>, alineado con el A3 original (OC se generaba
    /// automática al autorizar). Con el setting
    /// <c>auto_generar_oc_al_autorizar</c> en modo manual (default), la
    /// RQ pasa a <c>EnSurtido</c> sin OC y el comprador la convierte
    /// manualmente; el compromiso ocurre en ese estado. <c>Autorizada</c>
    /// se preserva como caso degenerado defensivo del cubrimiento.
    /// </para>
    /// </summary>
    public void ComprometerEnOc(Guid ordenCompraId)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "RQ_COMPROMISO_OC_ID_VACIO",
                "El id de la OC no puede ser Guid.Empty.");
        }

        if (Estado != EstadoRequisicion.Autorizada
            && Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "RQ_COMPROMISO_ESTADO_INVALIDO",
                $"Solo se pueden comprometer RQs en estado Autorizada o EnSurtido (actual: {Estado}).");
        }

        if (ComprometidaEnOcId is Guid existente && existente != ordenCompraId)
        {
            throw new BusinessRuleException(
                "RQ_YA_COMPROMETIDA_EN_OTRA_OC",
                $"La RQ ya está comprometida en la OC '{existente}'. No puede asignarse a otra.");
        }

        ComprometidaEnOcId = ordenCompraId;
    }

    /// <summary>
    /// Libera esta RQ del compromiso con una OC. Lo invoca el listener
    /// <c>LineaRqLiberadaListener</c> en F4-PR3 cuando la OC elimina
    /// la última línea con FK a esta RQ, o cuando la OC se cancela sin
    /// recepciones. Idempotente: si ya estaba libre, no-op.
    /// </summary>
    public void LiberarDeOc()
    {
        ComprometidaEnOcId = null;
    }

    private static void EnsureNotEmpty(Guid value, string nombre)
    {
        if (value == Guid.Empty)
        {
            throw new BusinessRuleException(
                "GUID_VACIO",
                $"El campo '{nombre}' no puede ser Guid.Empty.");
        }
    }
}
