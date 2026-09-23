using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.Compartido.Infrastructure.Persistence;

// F1-PR2: el alias `AlmacenEntity` y el DbSet `Almacenes` fueron retirados.
// El catálogo de almacenes vive ahora en el módulo Almacén
// (Millet.Almacen.Domain.Catalogo.Almacen / almacen.almacenes).

namespace Millet.Compartido.Infrastructure.Persistence;

/// <summary>
/// DbContext del esquema <c>compartido</c> en PostgreSQL: catálogos
/// cross-empresa (empresas, monedas, catálogo SAT, tipos de cambio DOF,
/// proveedores y artículos no-producción). Ver ADR-0011 y ADR-0014.
///
/// <para>
/// Proveedores y artículos viven aquí (F7-PR1) porque son catálogos
/// cross-module: Compras los consume para captura de requisiciones;
/// CxP, Activos Fijos, Almacén no-prod los consumirán cuando lleguen.
/// SAP tiene 1 catálogo de no-producción, el ERP también.
/// </para>
///
/// <para>
/// Tras el refactor F-Admin-PR0.1 (ADR-0035), las entidades viven en
/// sus módulos dueños (Administracion, Catalogos, DatosMaestros,
/// Almacen). Este DbContext sigue siendo el dueño del schema físico
/// <c>compartido</c> y de las migraciones existentes — el split por
/// módulo ocurre orgánicamente cuando cada módulo agregue su primera
/// migración propia.
/// </para>
/// </summary>
public sealed class CompartidoDbContext : BaseDbContext
{
    public CompartidoDbContext(
        DbContextOptions<CompartidoDbContext> options,
        SharedKernel.Application.ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Moneda> Monedas => Set<Moneda>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Articulo> Articulos => Set<Articulo>();
    // ADR-0048 D6: master de clientes (primer consumidor: Facturación vía
    // IClientesReadPort; nacen desde A+W por auto-provisión).
    public DbSet<Cliente> Clientes => Set<Cliente>();
    // ADR-0048 D5: master de productos de venta manufacturados (A+W),
    // SEPARADO de Articulos (compras/almacén) a propósito.
    public DbSet<ProductoAw> ProductosAw => Set<ProductoAw>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    // FAC-ING-PR2: catálogo administrable de canales de venta (reemplaza el
    // enum de Facturación). PK short asignada por la app; seed 1..10 espejo
    // del enum original. Facturación lo lee vía ICanalesVentaReadPort; la
    // ingesta A+W resuelve por clave_aw (GRUPPE) vía ICanalVentaPorClaveAwResolver.
    public DbSet<CanalVenta> CanalesVenta => Set<CanalVenta>();
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    // ADM-PR1 (doc 10-catalogo-puestos-empleados): master de puestos y
    // empleados para reglas de negocio por persona (políticas de viáticos
    // por puesto, jefe directo como autorizador N1). Consumidos por CxP y
    // Almacén vía IPuestoReadPort/IEmpleadoReadPort (adapters en ADM-PR2).
    public DbSet<Puesto> Puestos => Set<Puesto>();
    public DbSet<Empleado> Empleados => Set<Empleado>();
    // PR-A1: asignación N:M entre Sucursal y Departamento. UNIQUE
    // (SucursalId, DepartamentoId). Consumido por Compras via puerto
    // ISucursalDepartamentoReadPort en PR-A2.
    public DbSet<SucursalDepartamento> SucursalDepartamentos => Set<SucursalDepartamento>();
    // F1-ADM-01 Fase 1: asignación N:M entre Sucursal y Puesto, análoga a
    // SucursalDepartamento. UNIQUE (SucursalId, PuestoId).
    public DbSet<SucursalPuesto> SucursalPuestos => Set<SucursalPuesto>();

    // F9-PR1: catálogos para Órdenes de Compra.
    public DbSet<Incoterm> Incoterms => Set<Incoterm>();
    public DbSet<Transportista> Transportistas => Set<Transportista>();
    public DbSet<RegimenFiscal> RegimenesFiscales => Set<RegimenFiscal>();
    public DbSet<CondicionesPago> CondicionesPago => Set<CondicionesPago>();

    // ADR-0046 Etapa 1a: catálogo de unidades de medida (reemplaza el string
    // libre de Articulo.UnidadMedidaDefault; el FK desde Articulo es Etapa 1b).
    public DbSet<UnidadMedida> UnidadesMedida => Set<UnidadMedida>();
    // UF2-PR1 (frontend): seed del catálogo de usos principales.
    // Brecha detectada al wirear el Sheet "Nueva OC" — el comando
    // CrearOrdenCompraVacia requiere UsoPrincipalId pero F9-PR1 no
    // sembró este catálogo. F-Admin-PR5.2 agrega el CRUD completo.
    public DbSet<UsoPrincipal> UsosPrincipales => Set<UsoPrincipal>();
    // Patrón catálogo (ADR-0046): categoría de artículo administrable, reemplaza
    // el string libre Articulo.Categoria. El FK articulos.categoria_id es PR2.
    public DbSet<CategoriaArticulo> CategoriasArticulo => Set<CategoriaArticulo>();

    // F-Admin-PR5.1: histórico de tipos de cambio por moneda × fecha.
    public DbSet<TipoCambio> TiposCambio => Set<TipoCambio>();

    // F-Admin-PR5.3: catálogos SAT read-mostly (sin CRUD UI).
    public DbSet<FormaPago> FormasPago => Set<FormaPago>();
    public DbSet<UsoCfdi> UsosCfdi => Set<UsoCfdi>();

    // F-Admin-PR6.1: series y secuencias de folios cross-módulo.
    // PLATFORM-TODO(<SeriesSchemaMigrate>): Fase A vive en schema
    // <c>compartido</c>; Fase B (cuando Administración tenga su propio
    // DbContext) hace migración aditiva renombrando a <c>admin.*</c>.
    public DbSet<Serie> Series => Set<Serie>();
    public DbSet<SecuenciaFolio> SecuenciasFolio => Set<SecuenciaFolio>();

    // F-Admin-PR7.1: parámetros globales del sistema (TimezoneDefault,
    // FormatoFecha, RedondeoMonetario, etc.) o por módulo (cuando Modulo
    // es no-null). Vive en compartido (PLATFORM-TODO Fase B → admin).
    public DbSet<ParametroGlobal> ParametrosGlobales => Set<ParametroGlobal>();
    public DbSet<IntegrationEventOutboxEntry> IntegrationEventsOutbox => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("compartido");
        base.OnModelCreating(modelBuilder);

        // Primer HasDbFunction del repo (ADR-0045): mapea el built-in
        // pg_catalog.translate(text, from, to) para búsqueda textual con
        // folding de acentos sin la extensión `unaccent` (que exigiría
        // allow-list en `azure.extensions` — infra, evitada por PR). El
        // schema pg_catalog se declara explícito porque el default del
        // modelo es `compartido` y generaría `compartido.translate(...)`.
        // Lo consume CatalogosEndpoints (búsqueda de artículos por nombre)
        // vía PostgresFunctions.Translate.
        modelBuilder
            .HasDbFunction(typeof(PostgresFunctions).GetMethod(
                nameof(PostgresFunctions.Translate),
                new[] { typeof(string), typeof(string), typeof(string) })!)
            .HasName("translate")
            .HasSchema("pg_catalog");

        ConfigureEmpresa(modelBuilder);
        ConfigureMoneda(modelBuilder);
        ConfigureProveedor(modelBuilder);
        ConfigureArticulo(modelBuilder);
        ConfigureCliente(modelBuilder);
        ConfigureProductoAw(modelBuilder);
        ConfigureSucursal(modelBuilder);
        ConfigureCanalVenta(modelBuilder);
        ConfigureDepartamento(modelBuilder);
        ConfigureSucursalDepartamento(modelBuilder);
        ConfigureSucursalPuesto(modelBuilder);
        ConfigurePuesto(modelBuilder);
        ConfigureEmpleado(modelBuilder);
        // F1-PR2: ConfigureAlmacen retirado — el catálogo vive en AlmacenDbContext.
        ConfigureIncoterm(modelBuilder);
        ConfigureTransportista(modelBuilder);
        ConfigureRegimenFiscal(modelBuilder);
        ConfigureCondicionesPago(modelBuilder);
        ConfigureUnidadMedida(modelBuilder);
        ConfigureUsoPrincipal(modelBuilder);
        ConfigureCategoriaArticulo(modelBuilder);
        ConfigureTipoCambio(modelBuilder);
        ConfigureFormaPago(modelBuilder);
        ConfigureUsoCfdi(modelBuilder);
        ConfigureSerie(modelBuilder);
        ConfigureSecuenciaFolio(modelBuilder);
        ConfigureParametroGlobal(modelBuilder);
        ConfigureIntegrationEventOutbox(modelBuilder);
    }

    private static void ConfigureIntegrationEventOutbox(ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<IntegrationEventOutboxEntry>();
        outbox.ToTable("integration_events_outbox");
        outbox.HasKey(entry => entry.Id);
        outbox.Property(entry => entry.EventType).HasMaxLength(150).IsRequired();
        outbox.Property(entry => entry.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(entry => entry.OccurredAt).IsRequired();
        outbox.Property(entry => entry.IntegrationEmpresaId).IsRequired();
        outbox.Property(entry => entry.PublishedAt);
        outbox.Property(entry => entry.Attempts).IsRequired();
        outbox.Property(entry => entry.LastError);
        outbox.HasIndex(entry => entry.IntegrationEmpresaId);
        outbox.HasIndex(entry => entry.PublishedAt)
            .HasDatabaseName("ix_integration_events_outbox_pending")
            .HasFilter("published_at IS NULL");
    }

    /// <summary>
    /// F-Admin-PR7.1: parámetros globales con UNIQUE en Clave. Seeds en
    /// la migración (TimezoneDefault, FormatoFecha, RedondeoMonetario,
    /// IdiomaDefault) con GUIDs deterministas <c>00000006-0001-*</c>.
    /// </summary>
    private static void ConfigureParametroGlobal(ModelBuilder modelBuilder)
    {
        var parametro = modelBuilder.Entity<ParametroGlobal>();
        parametro.ToTable("parametros_globales", t =>
        {
            t.HasCheckConstraint("ck_parametros_globales_tipo",
                "tipo BETWEEN 0 AND 3");
        });
        parametro.HasKey(x => x.Id);
        parametro.HasIndex(x => x.Clave).IsUnique();
        parametro.HasIndex(x => x.Modulo);
        parametro.Property(x => x.Clave).HasMaxLength(100).IsRequired();
        parametro.Property(x => x.Valor).HasMaxLength(2000).IsRequired();
        parametro.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        parametro.Property(x => x.Modulo).HasMaxLength(50);
        parametro.Property(x => x.Descripcion).HasMaxLength(500).IsRequired();

        // Seeds default (F-Admin-PR7.1): 4 parámetros del sistema.
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        parametro.HasData(
            SeedParametro("00000006-0001-0000-0000-000000000001", "system.timezone-default",
                "America/Mexico_City", TipoParametro.Texto,
                "Zona horaria por defecto del sistema (IANA TZ database).", seedTime),
            SeedParametro("00000006-0001-0000-0000-000000000002", "system.formato-fecha",
                "dd/MM/yyyy", TipoParametro.Texto,
                "Formato de fecha por defecto para visualización en UI.", seedTime),
            SeedParametro("00000006-0001-0000-0000-000000000003", "system.redondeo-monetario",
                "2", TipoParametro.Numero,
                "Número de decimales para montos monetarios MXN.", seedTime),
            SeedParametro("00000006-0001-0000-0000-000000000004", "system.idioma-default",
                "es-MX", TipoParametro.Texto,
                "Idioma por defecto del sistema (BCP 47).", seedTime)
        );
    }

    private static object SeedParametro(
        string id, string clave, string valor, TipoParametro tipo,
        string descripcion, DateTimeOffset seedTime) => new
        {
            Id = Guid.Parse(id),
            Clave = clave,
            Valor = valor,
            Tipo = tipo,
            Modulo = (string?)null,
            Descripcion = descripcion,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        };

    /// <summary>
    /// F-Admin-PR6.1: serie de folios cross-módulo. Tabla
    /// <c>compartido.series</c>. UNIQUE efectivo por
    /// <c>(EmpresaId, COALESCE(SucursalId, '00...'), TipoDocumento,
    /// Prefijo, COALESCE(Sufijo, ''))</c> via índice computado en
    /// PostgreSQL (NULLs no chocan en UNIQUE PostgreSQL por default —
    /// los normalizamos a sentinel).
    ///
    /// <para>Seed: la migración F-Admin-PR6.2 (<c>SeedSerieOcInicial</c>)
    /// agrega una fila por Empresa existente con
    /// <see cref="TipoDocumentoSerie.OrdenCompra"/> + Prefijo "OC" +
    /// <see cref="Domain.ReinicioPeriodo.Anual"/>.</para>
    /// </summary>
    private static void ConfigureSerie(ModelBuilder modelBuilder)
    {
        var serie = modelBuilder.Entity<Serie>();
        serie.ToTable("series", t =>
        {
            t.HasCheckConstraint("ck_series_reinicio_periodo",
                "reinicio_periodo BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_series_tipo_documento",
                "tipo_documento BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_series_prefijo_no_vacio",
                "char_length(prefijo) BETWEEN 1 AND 10");
            t.HasCheckConstraint("ck_series_sufijo_max",
                "sufijo IS NULL OR char_length(sufijo) BETWEEN 0 AND 10");
        });
        serie.HasKey(x => x.Id);
        serie.Property(x => x.EmpresaId).IsRequired();
        serie.Property(x => x.SucursalId);
        serie.Property(x => x.TipoDocumento).HasConversion<short>().IsRequired();
        serie.Property(x => x.Prefijo).HasMaxLength(10).IsRequired();
        serie.Property(x => x.Sufijo).HasMaxLength(10);
        serie.Property(x => x.ReinicioPeriodo).HasConversion<short>().IsRequired();
        serie.Property(x => x.Activa).IsRequired();

        // Índice único compuesto. Para que los NULLs de SucursalId/Sufijo
        // no salten el unique de PostgreSQL, usamos COALESCE a sentinels.
        // EF Core no soporta índices computados nativamente; usamos SQL
        // raw con HasFilter+columnas reales y agregamos un índice extra
        // via Migration custom si se requiere. Por ahora, índice no único
        // sobre lookup primario; la unicidad la fuerza el handler que
        // hace AnyAsync antes de Add.
        serie.HasIndex(x => new { x.EmpresaId, x.TipoDocumento, x.Activa });
        serie.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.TipoDocumento, x.Prefijo });
    }

    /// <summary>
    /// F-Admin-PR6.1: contador atómico de folios por (Serie, PeriodoClave).
    /// Tabla <c>compartido.secuencias_folio</c>. UNIQUE (SerieId,
    /// PeriodoClave). FK física a Series con cascade.
    /// </summary>
    private static void ConfigureSecuenciaFolio(ModelBuilder modelBuilder)
    {
        var sec = modelBuilder.Entity<SecuenciaFolio>();
        sec.ToTable("secuencias_folio", t =>
        {
            t.HasCheckConstraint("ck_secuencias_folio_ultimo_numero_no_negativo",
                "ultimo_numero >= 0");
            t.HasCheckConstraint("ck_secuencias_folio_periodo_max",
                "char_length(periodo_clave) <= 10");
        });
        sec.HasKey(x => x.Id);
        sec.Property(x => x.SerieId).IsRequired();
        sec.Property(x => x.PeriodoClave).HasMaxLength(10).IsRequired();
        sec.Property(x => x.UltimoNumero).IsRequired();

        sec.HasIndex(x => new { x.SerieId, x.PeriodoClave }).IsUnique();

        // FK física: ambas viven en compartido; OnDelete = Cascade
        // (al borrar la serie se eliminan sus secuencias). Soft-delete
        // a nivel agregado lo maneja el desactivar(); no eliminamos
        // filas en producción.
        sec.HasOne<Serie>()
            .WithMany()
            .HasForeignKey(x => x.SerieId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// F-Admin-PR5.1: tipos de cambio histórico. UNIQUE (MonedaId, Fecha)
    /// para un solo valor por par. Sin seed — se popula via endpoints o
    /// importer DOF/Banxico (deferred).
    /// </summary>
    private static void ConfigureTipoCambio(ModelBuilder modelBuilder)
    {
        var tc = modelBuilder.Entity<TipoCambio>();
        tc.ToTable("tipos_cambio", t =>
        {
            t.HasCheckConstraint("ck_tipos_cambio_valor_positivo", "valor_en_mxn > 0");
            t.HasCheckConstraint("ck_tipos_cambio_origen", "origen BETWEEN 0 AND 2");
        });
        tc.HasKey(x => x.Id);
        tc.Property(x => x.MonedaId).IsRequired();
        tc.Property(x => x.Fecha).IsRequired();
        tc.Property(x => x.ValorEnMxn).HasPrecision(15, 6).IsRequired();
        tc.Property(x => x.Origen).HasConversion<short>().IsRequired();

        tc.HasIndex(x => new { x.MonedaId, x.Fecha }).IsUnique();

        // FK física a compartido.monedas (mismo schema, decisión B.1.1 (b)).
        tc.HasOne<Moneda>()
            .WithMany()
            .HasForeignKey(x => x.MonedaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// F-Admin-PR5.3: catálogo SAT c_FormaPago. Seed completo (21 entries
    /// vigentes al 2026-01-01) via HasData; actualizaciones via migration
    /// aditiva si SAT publica versiones nuevas.
    /// </summary>
    private static void ConfigureFormaPago(ModelBuilder modelBuilder)
    {
        var fp = modelBuilder.Entity<FormaPago>();
        fp.ToTable("formas_pago", t =>
        {
            t.HasCheckConstraint("ck_formas_pago_clave_2", "char_length(clave_sat) = 2");
        });
        fp.HasKey(x => x.Id);
        fp.Property(x => x.ClaveSat).HasMaxLength(2).IsRequired();
        fp.Property(x => x.Descripcion).HasMaxLength(254).IsRequired();
        fp.Property(x => x.Activa).IsRequired();

        fp.HasIndex(x => x.ClaveSat).IsUnique();

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        fp.HasData(
            SeedFormaPago("00000002-0005-0000-0000-000000000001", "01", "Efectivo", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000002", "02", "Cheque nominativo", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000003", "03", "Transferencia electrónica de fondos", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000004", "04", "Tarjeta de crédito", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000005", "05", "Monedero electrónico", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000006", "06", "Dinero electrónico", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000008", "08", "Vales de despensa", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000012", "12", "Dación en pago", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000013", "13", "Pago por subrogación", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000014", "14", "Pago por consignación", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000015", "15", "Condonación", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000017", "17", "Compensación", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000023", "23", "Novación", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000024", "24", "Confusión", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000025", "25", "Remisión de deuda", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000026", "26", "Prescripción o caducidad", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000027", "27", "A satisfacción del acreedor", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000028", "28", "Tarjeta de débito", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000029", "29", "Tarjeta de servicios", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000030", "30", "Aplicación de anticipos", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000031", "31", "Intermediario pagos", seedTime),
            SeedFormaPago("00000002-0005-0000-0000-000000000099", "99", "Por definir", seedTime)
        );
    }

    private static object SeedFormaPago(string id, string clave, string descripcion, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        ClaveSat = clave,
        Descripcion = descripcion,
        Activa = true,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>
    /// F-Admin-PR5.3: catálogo SAT c_UsoCFDI. Seed con los 23 valores
    /// más comunes del catálogo oficial (G01–G03, I01–I08, D01–D10,
    /// S01, CP01, CN01); cubre todos los casos de uso del MVP.
    /// </summary>
    private static void ConfigureUsoCfdi(ModelBuilder modelBuilder)
    {
        var uc = modelBuilder.Entity<UsoCfdi>();
        uc.ToTable("usos_cfdi", t =>
        {
            t.HasCheckConstraint("ck_usos_cfdi_aplica", "aplica_tipo_persona BETWEEN 0 AND 2");
        });
        uc.HasKey(x => x.Id);
        uc.Property(x => x.ClaveSat).HasMaxLength(4).IsRequired();
        uc.Property(x => x.Descripcion).HasMaxLength(254).IsRequired();
        uc.Property(x => x.AplicaTipoPersona).HasConversion<short>().IsRequired();
        uc.Property(x => x.Activa).IsRequired();

        uc.HasIndex(x => x.ClaveSat).IsUnique();

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        uc.HasData(
            // Gastos: G01-G03
            SeedUsoCfdi("00000002-0006-0000-0000-000000000001", "G01", "Adquisición de mercancías", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000002", "G02", "Devoluciones, descuentos o bonificaciones", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000003", "G03", "Gastos en general", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            // Inversiones: I01-I08
            SeedUsoCfdi("00000002-0006-0000-0000-000000000004", "I01", "Construcciones", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000005", "I02", "Mobiliario y equipo de oficina por inversiones", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000006", "I03", "Equipo de transporte", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000007", "I04", "Equipo de cómputo y accesorios", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000008", "I05", "Dados, troqueles, moldes, matrices y herramental", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000009", "I06", "Comunicaciones telefónicas", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000a", "I07", "Comunicaciones satelitales", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000b", "I08", "Otra maquinaria y equipo", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            // Deducciones personales (sólo física): D01-D10
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000c", "D01", "Honorarios médicos, dentales y gastos hospitalarios", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000d", "D02", "Gastos médicos por incapacidad o discapacidad", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000e", "D03", "Gastos funerales", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-00000000000f", "D04", "Donativos", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000010", "D05", "Intereses reales efectivamente pagados por créditos hipotecarios", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000011", "D06", "Aportaciones voluntarias al SAR", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000012", "D07", "Primas por seguros de gastos médicos", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000013", "D08", "Gastos de transportación escolar obligatoria", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000014", "D09", "Depósitos en cuentas para el ahorro, primas que tengan como base planes de pensiones", AplicaTipoPersona.SoloFisica, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000015", "D10", "Pagos por servicios educativos (colegiaturas)", AplicaTipoPersona.SoloFisica, seedTime),
            // Sin efectos / Pagos / Nómina
            SeedUsoCfdi("00000002-0006-0000-0000-000000000016", "S01", "Sin efectos fiscales", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000017", "CP01", "Pagos", AplicaTipoPersona.AmbosFisicaMoral, seedTime),
            SeedUsoCfdi("00000002-0006-0000-0000-000000000018", "CN01", "Nómina", AplicaTipoPersona.SoloFisica, seedTime)
        );
    }

    private static object SeedUsoCfdi(string id, string clave, string descripcion, AplicaTipoPersona aplica, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        ClaveSat = clave,
        Descripcion = descripcion,
        AplicaTipoPersona = aplica,
        Activa = true,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>
    /// UF2-PR1: usos principales con seed básico (8 valores que cubren
    /// los buckets más comunes en non-producción industrial). Mismo
    /// shape de configuración que CondicionesPago.
    /// </summary>
    private static void ConfigureUsoPrincipal(ModelBuilder modelBuilder)
    {
        var uso = modelBuilder.Entity<UsoPrincipal>();
        uso.ToTable("usos_principales", t =>
        {
            t.HasCheckConstraint("ck_usos_principales_estatus", "estatus BETWEEN 0 AND 2");
        });
        uso.HasKey(x => x.Id);
        uso.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        uso.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        uso.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        uso.HasIndex(x => x.Clave).IsUnique();
        uso.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        uso.HasData(
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000001", "MTTO_GEN", "Mantenimiento general", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000002", "PRODUCCION", "Insumos de producción", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000003", "SVC_PROF", "Servicios profesionales", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000004", "INSUMO_OF", "Insumos de oficina", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000005", "ACTIVO_FIJO", "Activo fijo", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000006", "IMPORTACION", "Importación", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000007", "REPARACION", "Reparaciones", seedTime),
            SeedUsoPrincipal("00000002-0004-0000-0000-000000000008", "OTRO", "Otro", seedTime)
        );
    }

    private static object SeedUsoPrincipal(string id, string clave, string nombre, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        Clave = clave,
        Nombre = nombre,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    // Patrón catálogo (ADR-0046) aplicado a categoría de artículo: molde
    // UsoPrincipal (Nombre + Estatus), SIN código. El business-key es el nombre
    // normalizado; la UNIQUE sobre lower+colapso-de-espacios se agrega como
    // índice de expresión en la migración (EF fluent no expresa índices
    // funcionales). Seed = las 14 categorías reales del ERP (doble espacio
    // limpiado); GUIDs deterministas 00000002-0008-*.
    private static void ConfigureCategoriaArticulo(ModelBuilder modelBuilder)
    {
        var cat = modelBuilder.Entity<CategoriaArticulo>();
        cat.ToTable("categorias_articulo", t =>
        {
            t.HasCheckConstraint("ck_categorias_articulo_estatus", "estatus BETWEEN 0 AND 2");
        });
        cat.HasKey(x => x.Id);
        cat.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        cat.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        cat.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        cat.HasData(
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000001", "INSUMOS PRODUCCION", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000002", "PRENDAS SEGURIDAD", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000003", "MATER. DE OFICINA", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000004", "MEDICINAS Y FARMACIA", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000005", "MAT DE LIMPIEZA", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000006", "SUMI. LABORATORIO", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000007", "IMPRESIÓN Y DIGITAL", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000008", "MAT DE TI Y COMUNIC", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-000000000009", "Equipo eléctrico", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-00000000000a", "Materiales peligrosos", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-00000000000b", "Químicos", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-00000000000c", "Refacciones", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-00000000000d", "Servicios profesionales", seedTime),
            SeedCategoriaArticulo("00000002-0008-0000-0000-00000000000e", "Servicios técnicos", seedTime)
        );
    }

    private static object SeedCategoriaArticulo(string id, string nombre, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        Nombre = nombre,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>F9-PR1: Incoterms 2020 oficiales (11 reglas).</summary>
    private static void ConfigureIncoterm(ModelBuilder modelBuilder)
    {
        var incoterm = modelBuilder.Entity<Incoterm>();
        incoterm.ToTable("incoterms", t =>
        {
            t.HasCheckConstraint("ck_incoterms_estatus", "estatus BETWEEN 0 AND 2");
        });
        incoterm.HasKey(x => x.Id);
        incoterm.Property(x => x.Codigo).HasMaxLength(4).IsRequired();
        incoterm.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        incoterm.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        incoterm.HasIndex(x => x.Codigo).IsUnique();
        incoterm.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        incoterm.HasData(
            SeedIncoterm("00000002-0001-0000-0000-000000000001", "EXW", "Ex Works", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000002", "FCA", "Free Carrier", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000003", "CPT", "Carriage Paid To", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000004", "CIP", "Carriage and Insurance Paid To", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000005", "DAP", "Delivered at Place", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000006", "DPU", "Delivered at Place Unloaded", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000007", "DDP", "Delivered Duty Paid", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000008", "FAS", "Free Alongside Ship", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000009", "FOB", "Free on Board", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000010", "CFR", "Cost and Freight", seedTime),
            SeedIncoterm("00000002-0001-0000-0000-000000000011", "CIF", "Cost, Insurance and Freight", seedTime)
        );
    }

    private static object SeedIncoterm(string id, string codigo, string nombre, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        Codigo = codigo,
        Nombre = nombre,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>F9-PR1: transportistas — tabla vacía en migración, seed test data en hosted service.</summary>
    private static void ConfigureTransportista(ModelBuilder modelBuilder)
    {
        var transportista = modelBuilder.Entity<Transportista>();
        transportista.ToTable("transportistas", t =>
        {
            t.HasCheckConstraint("ck_transportistas_estatus", "estatus BETWEEN 0 AND 2");
        });
        transportista.HasKey(x => x.Id);
        transportista.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        transportista.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        transportista.Property(x => x.Email).HasMaxLength(254);
        transportista.Property(x => x.Telefono).HasMaxLength(50);
        transportista.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        transportista.HasIndex(x => x.Clave).IsUnique();
        transportista.HasIndex(x => x.Estatus);
    }

    /// <summary>
    /// F9-PR1: regímenes fiscales SAT. Seed inicial con subset (8 códigos);
    /// completado al c_RegimenFiscal vigente de CFDI 4.0 (19 códigos) para
    /// captura de clientes — el 616 (Sin obligaciones fiscales) es el del
    /// público en general. <c>AplicaPersonaFisica</c> usa la naturaleza
    /// dominante cuando el SAT marca ambas (610 y 626).
    /// </summary>
    private static void ConfigureRegimenFiscal(ModelBuilder modelBuilder)
    {
        var regimen = modelBuilder.Entity<RegimenFiscal>();
        regimen.ToTable("regimenes_fiscales", t =>
        {
            t.HasCheckConstraint("ck_regimenes_fiscales_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_regimenes_fiscales_codigo_3", "char_length(codigo) = 3");
        });
        regimen.HasKey(x => x.Id);
        regimen.Property(x => x.Codigo).HasMaxLength(3).IsRequired();
        regimen.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        regimen.Property(x => x.AplicaPersonaFisica).IsRequired();
        regimen.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        regimen.HasIndex(x => x.Codigo).IsUnique();
        regimen.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        regimen.HasData(
            SeedRegimen("00000002-0002-0000-0000-000000000001", "601", "General de Ley Personas Morales", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000002", "603", "Personas Morales con Fines no Lucrativos", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000003", "605", "Sueldos y Salarios e Ingresos Asimilados a Salarios", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000004", "606", "Arrendamiento", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000005", "612", "Personas Físicas con Actividades Empresariales y Profesionales", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000006", "621", "Incorporación Fiscal", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000007", "625", "Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000008", "626", "Régimen Simplificado de Confianza (RESICO)", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000009", "607", "Régimen de Enajenación o Adquisición de Bienes", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000010", "608", "Demás ingresos", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000011", "610", "Residentes en el Extranjero sin Establecimiento Permanente en México", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000012", "611", "Ingresos por Dividendos (socios y accionistas)", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000013", "614", "Ingresos por intereses", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000014", "615", "Régimen de los ingresos por obtención de premios", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000015", "616", "Sin obligaciones fiscales", true, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000016", "620", "Sociedades Cooperativas de Producción que optan por diferir sus ingresos", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000017", "622", "Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000018", "623", "Opcional para Grupos de Sociedades", false, seedTime),
            SeedRegimen("00000002-0002-0000-0000-000000000019", "624", "Coordinados", false, seedTime)
        );
    }

    private static object SeedRegimen(string id, string codigo, string nombre, bool aplicaPersonaFisica, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        Codigo = codigo,
        Nombre = nombre,
        AplicaPersonaFisica = aplicaPersonaFisica,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>F9-PR1: condiciones de pago estándar (contado + N días).</summary>
    private static void ConfigureCondicionesPago(ModelBuilder modelBuilder)
    {
        var cp = modelBuilder.Entity<CondicionesPago>();
        cp.ToTable("condiciones_pago", t =>
        {
            t.HasCheckConstraint("ck_condiciones_pago_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_condiciones_pago_dias", "dias_credito BETWEEN 0 AND 365");
        });
        cp.HasKey(x => x.Id);
        cp.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        cp.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        cp.Property(x => x.DiasCredito).IsRequired();
        cp.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        cp.HasIndex(x => x.Clave).IsUnique();
        cp.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        cp.HasData(
            SeedCondicionesPago("00000002-0003-0000-0000-000000000001", "CONTADO", "Contado", 0, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000002", "15D", "15 días", 15, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000003", "30D", "30 días", 30, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000004", "45D", "45 días", 45, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000005", "60D", "60 días", 60, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000006", "90D", "90 días", 90, seedTime),
            SeedCondicionesPago("00000002-0003-0000-0000-000000000007", "120D", "120 días", 120, seedTime)
        );
    }

    private static object SeedCondicionesPago(string id, string clave, string nombre, int dias, DateTimeOffset seedTime) => new
    {
        Id = Guid.Parse(id),
        Clave = clave,
        Nombre = nombre,
        DiasCredito = dias,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>
    /// ADR-0046 Etapa 1a: catálogo de unidades de medida. Dimensión como
    /// enum (check 0..4), <c>factor_a_base</c> &gt; 0, <c>decimales</c> 0..6,
    /// <c>codigo</c> único. Seed de 10 unidades (5 dimensiones, base marcada
    /// con <c>es_base</c>). El FK desde <c>articulos</c> es Etapa 1b.
    /// </summary>
    private static void ConfigureUnidadMedida(ModelBuilder modelBuilder)
    {
        var um = modelBuilder.Entity<UnidadMedida>();
        um.ToTable("unidades_medida", t =>
        {
            t.HasCheckConstraint("ck_unidades_medida_dimension", "dimension BETWEEN 0 AND 4");
            t.HasCheckConstraint("ck_unidades_medida_factor_positivo", "factor_a_base > 0");
            t.HasCheckConstraint("ck_unidades_medida_decimales", "decimales BETWEEN 0 AND 6");
            t.HasCheckConstraint("ck_unidades_medida_estatus", "estatus BETWEEN 0 AND 2");
        });
        um.HasKey(x => x.Id);
        um.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
        um.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        um.Property(x => x.Dimension).HasConversion<short>().IsRequired();
        um.Property(x => x.FactorABase).HasPrecision(18, 6).IsRequired();
        um.Property(x => x.Decimales).IsRequired();
        um.Property(x => x.EsBase).IsRequired();
        um.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        um.HasIndex(x => x.Codigo).IsUnique();
        um.HasIndex(x => x.Dimension);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        um.HasData(
            // Conteo (base PZA)
            SeedUnidadMedida("00000002-0007-0000-0000-000000000001", "PZA", "Pieza", DimensionUnidad.Conteo, 1m, 0, true, seedTime),
            SeedUnidadMedida("00000002-0007-0000-0000-000000000002", "PAR", "Par", DimensionUnidad.Conteo, 2m, 0, false, seedTime),
            // Peso (base KG)
            SeedUnidadMedida("00000002-0007-0000-0000-000000000003", "KG", "Kilogramo", DimensionUnidad.Peso, 1m, 3, true, seedTime),
            SeedUnidadMedida("00000002-0007-0000-0000-000000000004", "G", "Gramo", DimensionUnidad.Peso, 0.001m, 2, false, seedTime),
            // Volumen (base L)
            SeedUnidadMedida("00000002-0007-0000-0000-000000000005", "L", "Litro", DimensionUnidad.Volumen, 1m, 3, true, seedTime),
            SeedUnidadMedida("00000002-0007-0000-0000-000000000006", "ML", "Mililitro", DimensionUnidad.Volumen, 0.001m, 0, false, seedTime),
            // Longitud (base M)
            SeedUnidadMedida("00000002-0007-0000-0000-000000000007", "M", "Metro", DimensionUnidad.Longitud, 1m, 2, true, seedTime),
            SeedUnidadMedida("00000002-0007-0000-0000-000000000008", "CM", "Centímetro", DimensionUnidad.Longitud, 0.01m, 1, false, seedTime),
            SeedUnidadMedida("00000002-0007-0000-0000-000000000009", "MM", "Milímetro", DimensionUnidad.Longitud, 0.001m, 0, false, seedTime),
            // Tiempo (base HR)
            SeedUnidadMedida("00000002-0007-0000-0000-00000000000a", "HR", "Hora", DimensionUnidad.Tiempo, 1m, 2, true, seedTime)
        );
    }

    private static object SeedUnidadMedida(
        string id, string codigo, string nombre, DimensionUnidad dimension,
        decimal factorABase, int decimales, bool esBase, DateTimeOffset seedTime) => new
        {
            Id = Guid.Parse(id),
            Codigo = codigo,
            Nombre = nombre,
            Dimension = dimension,
            FactorABase = factorABase,
            Decimales = decimales,
            EsBase = esBase,
            Estatus = EstatusCatalogo.Activo,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        };

    /// <summary>
    /// Configura <see cref="Empresa"/> (F1-ADM-01: raíz del tenant + jerarquía
    /// opcional de 2 niveles vía <see cref="Empresa.EmpresaPadreId"/> self-FK
    /// Restrict, y domicilio fiscal estructurado). <see cref="Empresa"/> NO
    /// implementa <see cref="IPerteneceAEmpresa"/> — es la raíz, no pertenece
    /// a un tenant.
    /// </summary>
    private static void ConfigureEmpresa(ModelBuilder modelBuilder)
    {
        var empresa = modelBuilder.Entity<Empresa>();
        empresa.ToTable("empresas");
        empresa.HasKey(x => x.Id);
        empresa.HasIndex(x => x.Rfc).IsUnique();
        empresa.HasIndex(x => x.Clave).IsUnique();
        empresa.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        empresa.Property(x => x.Rfc).HasMaxLength(13).IsRequired();
        empresa.Property(x => x.RazonSocial).HasMaxLength(254).IsRequired();
        empresa.Property(x => x.NombreComercial).HasMaxLength(254);
        empresa.Property(x => x.RegimenFiscal).HasMaxLength(10).IsRequired();
        // Fracción 0–1 (0.16); 4 decimales cubren tasas SAT (0.1067 retención).
        empresa.Property(x => x.TasaIvaDefault).HasPrecision(5, 4);

        // Domicilio fiscal estructurado (F1-ADM-01). CodigoPostal ya existía
        // (LugarExpedicion del CFDI, F12-PR1) y se mantiene opcional.
        empresa.Property(x => x.Calle).HasMaxLength(254).IsRequired();
        empresa.Property(x => x.NumeroExterior).HasMaxLength(20).IsRequired();
        empresa.Property(x => x.NumeroInterior).HasMaxLength(20);
        empresa.Property(x => x.Colonia).HasMaxLength(254).IsRequired();
        empresa.Property(x => x.Ciudad).HasMaxLength(100).IsRequired();
        empresa.Property(x => x.Municipio).HasMaxLength(100).IsRequired();
        empresa.Property(x => x.Estado).HasMaxLength(100).IsRequired();
        empresa.Property(x => x.Pais).HasMaxLength(100).IsRequired();
        empresa.Property(x => x.CodigoPostal).HasMaxLength(5);

        // Jerarquía opcional de 2 niveles (raíz + hijas). Restrict: no se
        // permite borrar una empresa padre mientras tenga hijas.
        empresa.HasIndex(x => x.EmpresaPadreId);
        empresa.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaPadreId)
            .OnDelete(DeleteBehavior.Restrict);

        // Moneda operativa default, opcional. Restrict: no se permite borrar
        // una moneda referenciada por alguna empresa.
        empresa.HasIndex(x => x.MonedaId);
        empresa.HasOne<Moneda>()
            .WithMany()
            .HasForeignKey(x => x.MonedaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMoneda(ModelBuilder modelBuilder)
    {
        var moneda = modelBuilder.Entity<Moneda>();
        moneda.ToTable("monedas");
        moneda.HasKey(x => x.Id);
        moneda.HasIndex(x => x.Codigo).IsUnique();
        moneda.Property(x => x.Codigo).HasMaxLength(3).IsRequired();
        moneda.Property(x => x.Nombre).HasMaxLength(100).IsRequired();

        // Seed catálogo SAT (subset). Ids deterministas para reproducibilidad
        // de migraciones. Timestamps fijos en 2026-01-01 UTC; la convención
        // CreatedBy="seed" identifica filas pobladas por migración.
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        moneda.HasData(
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000001"), Codigo = "MXN", Nombre = "Peso Mexicano",      Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000002"), Codigo = "USD", Nombre = "Dólar EUA",           Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000003"), Codigo = "EUR", Nombre = "Euro",                Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000004"), Codigo = "CAD", Nombre = "Dólar Canadiense",    Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000005"), Codigo = "GBP", Nombre = "Libra Esterlina",     Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000006"), Codigo = "JPY", Nombre = "Yen Japonés",          Decimales = 0, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000007"), Codigo = "CHF", Nombre = "Franco Suizo",        Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000008"), Codigo = "CNY", Nombre = "Yuan Chino",          Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000009"), Codigo = "BRL", Nombre = "Real Brasileño",      Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000010"), Codigo = "ARS", Nombre = "Peso Argentino",      Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000011"), Codigo = "COP", Nombre = "Peso Colombiano",     Decimales = 2, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null },
            new { Id = Guid.Parse("00000001-0000-0000-0000-000000000012"), Codigo = "CLP", Nombre = "Peso Chileno",        Decimales = 0, Activa = true, Version = 1, CreatedAt = seedTime, UpdatedAt = seedTime, CreatedBy = (string?)"seed", UpdatedBy = (string?)"seed", DeletedAt = (DateTimeOffset?)null }
        );
    }

    /// <summary>
    /// Configura <see cref="Proveedor"/> (F7-PR1). Sin seed en la
    /// migración: el seed test data lo aplica un IHostedService al
    /// arranque cuando el environment NO es Production. En Production
    /// la tabla arranca vacía y se popula via importer SAP (F7-PR2).
    /// </summary>
    private static void ConfigureProveedor(ModelBuilder modelBuilder)
    {
        var proveedor = modelBuilder.Entity<Proveedor>();
        proveedor.ToTable("proveedores", t =>
        {
            t.HasCheckConstraint("ck_proveedores_rfc_longitud", "char_length(rfc) BETWEEN 12 AND 13");
            t.HasCheckConstraint("ck_proveedores_condiciones_pago", "condiciones_pago_dias IS NULL OR condiciones_pago_dias BETWEEN 0 AND 365");
            t.HasCheckConstraint("ck_proveedores_tipo_persona", "tipo_persona BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_proveedores_estatus", "estatus BETWEEN 0 AND 2");
            // TES-PR3 [T-G1]: datos bancarios para pago.
            t.HasCheckConstraint("ck_proveedores_clabe_formato", "clabe IS NULL OR clabe ~ '^[0-9]{18}$'");
        });
        proveedor.HasKey(x => x.Id);
        proveedor.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        proveedor.Property(x => x.ClaveLegacy).HasMaxLength(20);
        proveedor.Property(x => x.RazonSocial).HasMaxLength(254).IsRequired();
        proveedor.Property(x => x.NombreComercial).HasMaxLength(254);
        proveedor.Property(x => x.Rfc).HasMaxLength(13).IsRequired();
        proveedor.Property(x => x.TipoPersona).HasConversion<short>().IsRequired();
        proveedor.Property(x => x.CondicionesPagoDias);
        proveedor.Property(x => x.MonedaPreferidaId);
        proveedor.Property(x => x.Email).HasMaxLength(254);
        proveedor.Property(x => x.Telefono).HasMaxLength(50);
        proveedor.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        // TES-PR3 [T-G1]: datos bancarios para pago (Tesorería los lee vía
        // IProveedorBancoReadPort; CLABE enmascarada en logs y DTOs).
        proveedor.Property(x => x.Banco).HasMaxLength(120);
        proveedor.Property(x => x.Clabe).HasMaxLength(18);
        proveedor.Property(x => x.Beneficiario).HasMaxLength(254);

        proveedor.HasIndex(x => x.Clave).IsUnique();
        proveedor.HasIndex(x => x.Rfc);
        proveedor.HasIndex(x => x.Estatus);
    }

    /// <summary>
    /// Configura <see cref="Articulo"/> (F7-PR1). Mismo patrón:
    /// migración crea tabla vacía, hosted service seed test data en
    /// non-Production.
    /// </summary>
    private static void ConfigureArticulo(ModelBuilder modelBuilder)
    {
        var articulo = modelBuilder.Entity<Articulo>();
        articulo.ToTable("articulos", t =>
        {
            t.HasCheckConstraint("ck_articulos_naturaleza", "naturaleza BETWEEN 0 AND 3");
            t.HasCheckConstraint("ck_articulos_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_articulos_precio_no_negativo", "precio_referencia_monto IS NULL OR precio_referencia_monto >= 0");
            t.HasCheckConstraint("ck_articulos_precio_moneda_iso", "precio_referencia_moneda IS NULL OR precio_referencia_moneda ~ '^[A-Z]{3}$'");
        });
        articulo.HasKey(x => x.Id);
        articulo.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        articulo.Property(x => x.ClaveLegacy).HasMaxLength(20);
        articulo.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        articulo.Property(x => x.DescripcionLarga).HasColumnType("text");
        articulo.Property(x => x.UnidadMedidaDefault).HasMaxLength(20).IsRequired();
        articulo.Property(x => x.Naturaleza).HasConversion<short>().IsRequired();
        articulo.Property(x => x.Categoria).HasMaxLength(100);
        articulo.Property(x => x.PrecioReferenciaMonto).HasPrecision(15, 4);
        articulo.Property(x => x.PrecioReferenciaMoneda).HasMaxLength(3);
        articulo.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        articulo.HasIndex(x => x.Clave).IsUnique();
        articulo.HasIndex(x => x.Naturaleza);
        articulo.HasIndex(x => x.Estatus);

        // ADR-0046 Etapa 1b: FK nullable al catálogo de unidades de medida.
        // ON DELETE RESTRICT (no se puede borrar una unidad referenciada por
        // un artículo — gemelo a nivel DB del guardrail UnidadMedidaEnUso).
        // Los artículos legacy (empaque variable / sin mapeo) quedan NULL y
        // operan con el string unidad_medida_default.
        articulo.HasIndex(x => x.UnidadMedidaId);
        articulo.HasOne<UnidadMedida>()
            .WithMany()
            .HasForeignKey(x => x.UnidadMedidaId)
            .OnDelete(DeleteBehavior.Restrict);

        // ADR-0046 (patrón, PR2): FK nullable al catálogo de categorías de
        // artículo. ON DELETE RESTRICT (gemelo a nivel DB del guardrail
        // CategoriaArticuloEnUso). Los artículos no reconciliados (PR3) quedan
        // NULL y operan con el string legacy categoria.
        articulo.HasIndex(x => x.CategoriaId);
        articulo.HasOne<CategoriaArticulo>()
            .WithMany()
            .HasForeignKey(x => x.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Configura <see cref="Cliente"/> (ADR-0048 D6). Sin seed: la tabla
    /// arranca vacía y se popula por auto-provisión A+W o captura manual.
    /// UNIQUE en clave y en referencia_externa (parcial — NULLs no chocan
    /// en PostgreSQL).
    /// </summary>
    private static void ConfigureCliente(ModelBuilder modelBuilder)
    {
        var cliente = modelBuilder.Entity<Cliente>();
        cliente.ToTable("clientes", t =>
        {
            t.HasCheckConstraint("ck_clientes_rfc_longitud",
                "rfc IS NULL OR char_length(rfc) BETWEEN 12 AND 13");
            t.HasCheckConstraint("ck_clientes_regimen_3",
                "regimen_fiscal IS NULL OR char_length(regimen_fiscal) = 3");
            t.HasCheckConstraint("ck_clientes_cp_5",
                "codigo_postal_fiscal IS NULL OR char_length(codigo_postal_fiscal) = 5");
            t.HasCheckConstraint("ck_clientes_metodo_pago",
                "metodo_pago_default IS NULL OR metodo_pago_default IN ('PUE','PPD')");
            t.HasCheckConstraint("ck_clientes_moneda_iso",
                "moneda_default ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_clientes_origen", "origen BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_clientes_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_clientes_pais_residencia",
                "pais_residencia IS NULL OR pais_residencia ~ '^[A-Za-z]{3}$'");
        });
        cliente.HasKey(x => x.Id);
        cliente.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        cliente.Property(x => x.ReferenciaExterna).HasMaxLength(50);
        cliente.Property(x => x.RazonSocial).HasMaxLength(254).IsRequired();
        cliente.Property(x => x.Rfc).HasMaxLength(13);
        cliente.Property(x => x.RegimenFiscal).HasMaxLength(3);
        cliente.Property(x => x.CodigoPostalFiscal).HasMaxLength(5);
        cliente.Property(x => x.UsoCfdiDefault).HasMaxLength(4);
        cliente.Property(x => x.FormaPagoDefault).HasMaxLength(2);
        cliente.Property(x => x.MetodoPagoDefault).HasMaxLength(3);
        cliente.Property(x => x.MonedaDefault).HasMaxLength(3).IsRequired();
        cliente.Property(x => x.EsGenerico).IsRequired();
        cliente.Property(x => x.Origen).HasConversion<short>().IsRequired();
        cliente.Property(x => x.Email).HasMaxLength(254);
        cliente.Property(x => x.Telefono).HasMaxLength(50);
        cliente.Property(x => x.NumRegIdTrib).HasMaxLength(40);
        cliente.Property(x => x.PaisResidencia).HasMaxLength(3);
        cliente.Property(x => x.DomicilioExtranjeroCalle).HasMaxLength(200);
        cliente.Property(x => x.DomicilioExtranjeroEstado).HasMaxLength(100);
        cliente.Property(x => x.DomicilioExtranjeroCodigoPostal).HasMaxLength(12);
        cliente.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        cliente.HasIndex(x => x.Clave).IsUnique();
        cliente.HasIndex(x => x.ReferenciaExterna).IsUnique();
        cliente.HasIndex(x => x.Rfc);
        cliente.HasIndex(x => x.Estatus);
    }

    /// <summary>
    /// Configura <see cref="ProductoAw"/> (ADR-0048 D5). Master de venta
    /// separado de <see cref="Articulo"/>. Sin seed — nace por
    /// auto-provisión. FKs opcionales a los catálogos de ADR-0046
    /// (UnidadMedida, CategoriaArticulo) con RESTRICT, igual que Articulo.
    /// </summary>
    private static void ConfigureProductoAw(ModelBuilder modelBuilder)
    {
        var producto = modelBuilder.Entity<ProductoAw>();
        producto.ToTable("producto_aw", t =>
        {
            t.HasCheckConstraint("ck_producto_aw_clave_prodserv_8",
                "clave_prod_serv_sat IS NULL OR char_length(clave_prod_serv_sat) = 8");
            t.HasCheckConstraint("ck_producto_aw_objeto_imp",
                "objeto_imp IS NULL OR objeto_imp IN ('01','02','03')");
            t.HasCheckConstraint("ck_producto_aw_tasas",
                "(tasa_iva_traslado IS NULL OR tasa_iva_traslado BETWEEN 0 AND 1) AND " +
                "(tasa_retencion_iva IS NULL OR tasa_retencion_iva BETWEEN 0 AND 1) AND " +
                "(tasa_retencion_isr IS NULL OR tasa_retencion_isr BETWEEN 0 AND 1)");
            t.HasCheckConstraint("ck_producto_aw_origen", "origen BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_producto_aw_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_producto_aw_fraccion",
                "fraccion_arancelaria IS NULL OR fraccion_arancelaria ~ '^[0-9]{8,10}$'");
            t.HasCheckConstraint("ck_producto_aw_peso",
                "peso_unitario_kg IS NULL OR peso_unitario_kg >= 0");
        });
        producto.HasKey(x => x.Id);
        producto.Property(x => x.ReferenciaExterna).HasMaxLength(50).IsRequired();
        producto.Property(x => x.Descripcion).HasMaxLength(254).IsRequired();
        producto.Property(x => x.UnidadMedida).HasMaxLength(20).IsRequired();
        producto.Property(x => x.ClaveProdServSat).HasMaxLength(8);
        producto.Property(x => x.ClaveUnidadSat).HasMaxLength(5);
        producto.Property(x => x.ObjetoImp).HasMaxLength(2);
        producto.Property(x => x.TasaIvaTraslado).HasPrecision(5, 4);
        producto.Property(x => x.TasaRetencionIva).HasPrecision(5, 4);
        producto.Property(x => x.TasaRetencionIsr).HasPrecision(5, 4);
        producto.Property(x => x.FraccionArancelaria).HasMaxLength(10);
        producto.Property(x => x.UnidadAduana).HasMaxLength(3);
        producto.Property(x => x.PesoUnitarioKg).HasPrecision(18, 6);
        producto.Property(x => x.Origen).HasConversion<short>().IsRequired();
        producto.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        producto.HasIndex(x => x.ReferenciaExterna).IsUnique();
        producto.HasIndex(x => x.Estatus);

        producto.HasIndex(x => x.UnidadMedidaId);
        producto.HasOne<UnidadMedida>()
            .WithMany()
            .HasForeignKey(x => x.UnidadMedidaId)
            .OnDelete(DeleteBehavior.Restrict);

        producto.HasIndex(x => x.CategoriaId);
        producto.HasOne<CategoriaArticulo>()
            .WithMany()
            .HasForeignKey(x => x.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Configura <see cref="Sucursal"/> (B.1). F1-ADM-01: catálogo por
    /// empresa (<see cref="Sucursal.EmpresaId"/> + <see cref="IPerteneceAEmpresa"/>,
    /// FK Restrict). <see cref="Sucursal.Clave"/> pasa de única global a
    /// única por empresa; <see cref="Sucursal.ClaveAw"/> se mantiene única
    /// global porque identifica la sucursal en A+W (sistema externo
    /// compartido entre empresas).
    /// </summary>
    private static void ConfigureSucursal(ModelBuilder modelBuilder)
    {
        var sucursal = modelBuilder.Entity<Sucursal>();
        sucursal.ToTable("sucursales", t =>
        {
            t.HasCheckConstraint("ck_sucursales_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_sucursales_tipo", "tipo BETWEEN 0 AND 2");
        });
        sucursal.HasKey(x => x.Id);
        sucursal.Property(x => x.EmpresaId).IsRequired();
        sucursal.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        sucursal.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        sucursal.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        sucursal.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        // Relación con A+W (ADR-0048 flujo 2): numero_sucursal del pedido
        // machea contra esta clave. Nullable; única cuando existe.
        sucursal.Property(x => x.ClaveAw).HasMaxLength(40);

        // Zona horaria IANA ([Decisión 12-8], CAJAS-PR3): define el día de
        // operación de las sesiones de caja. Default = Yucatán.
        sucursal.Property(x => x.ZonaHoraria)
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(Administracion.Domain.Sucursal.ZonaHorariaDefault);

        // Domicilio operativo estructurado (F1-ADM-01).
        sucursal.Property(x => x.Calle).HasMaxLength(254).IsRequired();
        sucursal.Property(x => x.NumeroExterior).HasMaxLength(20).IsRequired();
        sucursal.Property(x => x.NumeroInterior).HasMaxLength(20);
        sucursal.Property(x => x.Colonia).HasMaxLength(254).IsRequired();
        sucursal.Property(x => x.Ciudad).HasMaxLength(100).IsRequired();
        sucursal.Property(x => x.Municipio).HasMaxLength(100).IsRequired();
        sucursal.Property(x => x.Estado).HasMaxLength(100).IsRequired();
        sucursal.Property(x => x.CodigoPostal).HasMaxLength(5).IsRequired();
        sucursal.Property(x => x.Pais).HasMaxLength(100).IsRequired();
        sucursal.Property(x => x.Responsable).HasMaxLength(254);
        sucursal.Property(x => x.InformacionUbicacion).HasMaxLength(1000);

        sucursal.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique();
        sucursal.HasIndex(x => x.ClaveAw).IsUnique();
        sucursal.HasIndex(x => x.Estatus);

        sucursal.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// FAC-ING-PR2: canales de venta administrables. PK <c>smallint</c>
    /// asignada por la aplicación (<c>ValueGeneratedNever</c> — NO identity:
    /// el seed usa 1..10 y Postgres identity + HasData provoca colisión de
    /// secuencia). <c>Version</c> es concurrency token manual porque la
    /// entidad no deriva de BaseEntity (ITieneMetadata). Seed: las 10 filas
    /// espejo del enum original; <c>clave_aw</c> = GRUPPE reales de A+W
    /// confirmados en SER-DATA el 2026-07-08 (Q2 de
    /// docs/operacion/aw-integracion-scripts/06_consultas_seed_inicial.sql).
    /// Los GRUPPE sin rama en el ERP quedan NULL y se asignan en el admin.
    /// </summary>
    private static void ConfigureCanalVenta(ModelBuilder modelBuilder)
    {
        var canal = modelBuilder.Entity<CanalVenta>();
        canal.ToTable("canales_venta", t =>
        {
            t.HasCheckConstraint("ck_canales_venta_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_canales_venta_id_positivo", "id > 0");
        });
        canal.HasKey(x => x.Id);
        canal.Property(x => x.Id).ValueGeneratedNever();
        canal.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        canal.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        // Relación con A+W (ADR-0048 flujo 2): canal_ventas (GRUPPE crudo)
        // del pedido machea contra esta clave. Nullable; única cuando existe.
        canal.Property(x => x.ClaveAw).HasMaxLength(40);
        // Concurrency token manual (no-BaseEntity; ADR-0012).
        canal.Property(x => x.Version).IsConcurrencyToken();

        canal.HasIndex(x => x.Nombre).IsUnique();
        canal.HasIndex(x => x.ClaveAw).IsUnique();
        canal.HasIndex(x => x.Estatus);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        canal.HasData(
            SeedCanalVenta(1, "Tienda Cancún", "Ventas Cancun", seedTime),
            SeedCanalVenta(2, "Tienda Circuito", "Ventas Circuito", seedTime),
            SeedCanalVenta(3, "Tienda Chichí Suárez", null, seedTime),
            SeedCanalVenta(4, "CC Mérida", "CC Mérida", seedTime),
            SeedCanalVenta(5, "CC Q.Roo", null, seedTime),
            SeedCanalVenta(6, "CC Municipios", null, seedTime),
            SeedCanalVenta(7, "Proyectos y Obras", "Ventas Proyectos", seedTime),
            SeedCanalVenta(8, "Exportación", "Ventas Internacionales", seedTime),
            SeedCanalVenta(9, "Planta Pintura", "Ventas Pintura", seedTime),
            SeedCanalVenta(10, "Administración", null, seedTime)
        );
    }

    private static object SeedCanalVenta(short id, string nombre, string? claveAw, DateTimeOffset seedTime) => new
    {
        Id = id,
        Nombre = nombre,
        ClaveAw = claveAw,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
    };

    /// <summary>
    /// Guid fijo de la empresa raíz de bootstrap (mismo valor que
    /// <c>BootstrapSuperAdminHostedService.EmpresaInicialId</c> en el módulo
    /// Identidad — no se referencia directamente para no crear una
    /// dependencia cross-módulo; Compartido solo necesita el valor literal
    /// para el backfill/seed de filas que hoy no tienen <c>EmpresaId</c>).
    /// Ver F1-ADM-01.
    /// </summary>
    internal static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    /// <summary>
    /// Configura <see cref="Departamento"/> (B.1). F1-ADM-01: catálogo por
    /// empresa (<see cref="Departamento.EmpresaId"/> + <see cref="IPerteneceAEmpresa"/>,
    /// FK Restrict). <see cref="Departamento.Clave"/> pasa de única global
    /// a única por empresa.
    /// </summary>
    private static void ConfigureDepartamento(ModelBuilder modelBuilder)
    {
        var depto = modelBuilder.Entity<Departamento>();
        depto.ToTable("departamentos", t =>
        {
            t.HasCheckConstraint("ck_departamentos_estatus", "estatus BETWEEN 0 AND 2");
        });
        depto.HasKey(x => x.Id);
        depto.Property(x => x.EmpresaId).IsRequired();
        depto.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        depto.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        depto.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        depto.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique();
        depto.HasIndex(x => x.Estatus);

        depto.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        // ADR-0047 PR5.A: departamento de sistema "Reabastecimiento Automático",
        // usado como DepartamentoId de las RQ del motor de reorden. Guid fijo
        // (DepartamentosSistema.ReabastecimientoAutomatico) para todos los ambientes.
        // F1-ADM-01: EmpresaId = empresa raíz de bootstrap (única empresa hoy).
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        depto.HasData(new
        {
            Id = DepartamentosSistema.ReabastecimientoAutomatico,
            EmpresaId = EmpresaBootstrapId,
            Clave = DepartamentosSistema.ReabastecimientoAutomaticoClave,
            Nombre = "Reabastecimiento Automático",
            Estatus = EstatusCatalogo.Activo,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        });
    }

    /// <summary>
    /// ADM-PR1 (doc 10-catalogo-puestos-empleados): catálogo de puestos.
    /// Seed D6: EJEC/GER/OPER con los GUIDs del antiguo
    /// <c>NoOpPuestoReadPort</c> de CxP para no romper las
    /// <c>politicas_viaticos</c> capturadas en dev contra ese seed.
    /// </summary>
    /// <summary>
    /// F1-ADM-01: catálogo por empresa (<see cref="Administracion.Domain.Puesto.EmpresaId"/>
    /// + <see cref="IPerteneceAEmpresa"/>, FK Restrict). Clave pasa de única
    /// global a única por empresa. Seed D6 (EJEC/GER/OPER) conserva sus GUIDs
    /// fijos; EmpresaId = empresa raíz de bootstrap (única empresa hoy).
    /// </summary>
    private static void ConfigurePuesto(ModelBuilder modelBuilder)
    {
        var puesto = modelBuilder.Entity<Administracion.Domain.Puesto>();
        puesto.ToTable("puestos", t =>
        {
            t.HasCheckConstraint("ck_puestos_estatus", "estatus BETWEEN 0 AND 2");
        });
        puesto.HasKey(x => x.Id);
        puesto.Property(x => x.EmpresaId).IsRequired();
        puesto.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        puesto.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        puesto.Property(x => x.Descripcion).HasMaxLength(500);
        puesto.Property(x => x.RolSugeridoId);
        puesto.Property(x => x.DepartamentoId);
        puesto.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        puesto.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique();
        puesto.HasIndex(x => x.RolSugeridoId);
        puesto.HasIndex(x => x.DepartamentoId);
        puesto.HasIndex(x => x.Estatus);

        puesto.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        puesto.HasOne<Departamento>()
            .WithMany()
            .HasForeignKey(x => x.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        puesto.HasData(
            SeedPuesto(Guid.Parse("00000000-0000-0000-0000-000000000001"), "EJEC", "Ejecutivo", seedTime),
            SeedPuesto(Guid.Parse("00000000-0000-0000-0000-000000000002"), "GER", "Gerente", seedTime),
            SeedPuesto(Guid.Parse("00000000-0000-0000-0000-000000000003"), "OPER", "Operativo", seedTime)
        );
    }

    private static object SeedPuesto(Guid id, string clave, string nombre, DateTimeOffset seedTime) => new
    {
        Id = id,
        EmpresaId = EmpresaBootstrapId,
        Clave = clave,
        Nombre = nombre,
        RolSugeridoId = (Guid?)null,
        DepartamentoId = (Guid?)null,
        Estatus = EstatusCatalogo.Activo,
        Version = 1,
        CreatedAt = seedTime,
        UpdatedAt = seedTime,
        CreatedBy = (string?)"seed",
        UpdatedBy = (string?)"seed",
        DeletedAt = (DateTimeOffset?)null,
    };

    /// <summary>
    /// ADM-PR1: master de empleados. FKs físicas mismo-schema a Empresa,
    /// Puesto, Sucursal, Departamento y self-FK JefeDirecto, todas
    /// <see cref="DeleteBehavior.Restrict"/>. <c>UsuarioId</c> sin FK
    /// (cross-módulo Identidad, decisión D5). UNIQUE (empresa_id, clave).
    /// </summary>
    private static void ConfigureEmpleado(ModelBuilder modelBuilder)
    {
        var empleado = modelBuilder.Entity<Administracion.Domain.Empleado>();
        empleado.ToTable("empleados", t =>
        {
            t.HasCheckConstraint("ck_empleados_estatus", "estatus BETWEEN 0 AND 2");
        });
        empleado.HasKey(x => x.Id);
        empleado.Property(x => x.EmpresaId).IsRequired();
        empleado.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        empleado.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        empleado.Property(x => x.Email).HasMaxLength(254);
        empleado.Property(x => x.EmailContacto).HasMaxLength(254);
        empleado.Property(x => x.CodigoNomina).HasMaxLength(20);
        empleado.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        empleado.HasOne<Empresa>().WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
        empleado.HasOne<Administracion.Domain.Puesto>().WithMany()
            .HasForeignKey(x => x.PuestoId)
            .OnDelete(DeleteBehavior.Restrict);
        empleado.HasOne<Administracion.Domain.Empleado>().WithMany()
            .HasForeignKey(x => x.JefeDirectoId)
            .OnDelete(DeleteBehavior.Restrict);
        empleado.HasOne<Sucursal>().WithMany()
            .HasForeignKey(x => x.SucursalId)
            .OnDelete(DeleteBehavior.Restrict);
        empleado.HasOne<Departamento>().WithMany()
            .HasForeignKey(x => x.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        empleado.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique();
        empleado.HasIndex(x => x.PuestoId);
        // Empleado → Usuario es 1:0..1 (alta unificada, plan 15): un usuario
        // no puede estar vinculado a dos empleados.
        empleado.HasIndex(x => x.UsuarioId)
            .IsUnique()
            .HasFilter("usuario_id IS NOT NULL");
        empleado.HasIndex(x => x.Estatus);
    }

    /// <summary>
    /// PR-A1: asignación N:M Sucursal ↔ Departamento. Surrogate
    /// <see cref="BaseEntity.Id"/> + UNIQUE (sucursal_id, departamento_id).
    /// FK física a ambas tablas del mismo schema con
    /// <see cref="DeleteBehavior.Restrict"/> — no se permite borrar una
    /// Sucursal o Departamento referenciado por una asignación viva.
    /// F1-ADM-01: <see cref="SucursalDepartamento.EmpresaId"/> +
    /// <see cref="IPerteneceAEmpresa"/>, FK Restrict a Empresa. La
    /// coherencia (mismo EmpresaId que la Sucursal y el Departamento
    /// vinculados) es invariante de negocio validado en el handler
    /// (Fase 2), no aquí — ver comentario en el dominio.
    /// </summary>
    private static void ConfigureSucursalDepartamento(ModelBuilder modelBuilder)
    {
        var asignacion = modelBuilder.Entity<SucursalDepartamento>();
        asignacion.ToTable("sucursal_departamentos", t =>
        {
            t.HasCheckConstraint("ck_sucursal_departamentos_estatus",
                "estatus BETWEEN 0 AND 2");
        });
        asignacion.HasKey(x => x.Id);
        asignacion.Property(x => x.EmpresaId).IsRequired();
        asignacion.Property(x => x.SucursalId).IsRequired();
        asignacion.Property(x => x.DepartamentoId).IsRequired();
        asignacion.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        asignacion.HasIndex(x => new { x.SucursalId, x.DepartamentoId }).IsUnique();
        asignacion.HasIndex(x => x.SucursalId);
        asignacion.HasIndex(x => x.DepartamentoId);

        asignacion.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        asignacion.HasOne<Sucursal>()
            .WithMany()
            .HasForeignKey(x => x.SucursalId)
            .OnDelete(DeleteBehavior.Restrict);

        asignacion.HasOne<Departamento>()
            .WithMany()
            .HasForeignKey(x => x.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Configura <see cref="SucursalPuesto"/> (F1-ADM-01 Fase 1). Análogo
    /// exacto de <see cref="ConfigureSucursalDepartamento"/> pero para
    /// <see cref="Puesto"/>. UNIQUE en <c>(SucursalId, PuestoId)</c>.
    ///
    /// <para>
    /// F1-ADM-01: <see cref="SucursalPuesto.EmpresaId"/> +
    /// <see cref="IPerteneceAEmpresa"/>, FK Restrict a Empresa. La
    /// coherencia (mismo EmpresaId que la Sucursal y el Puesto
    /// vinculados) es invariante de negocio validado en el handler
    /// (Fase 2), no aquí — ver comentario en el dominio.
    /// </para>
    /// </summary>
    private static void ConfigureSucursalPuesto(ModelBuilder modelBuilder)
    {
        var asignacion = modelBuilder.Entity<SucursalPuesto>();
        asignacion.ToTable("sucursal_puestos", t =>
        {
            t.HasCheckConstraint("ck_sucursal_puestos_estatus",
                "estatus BETWEEN 0 AND 2");
        });
        asignacion.HasKey(x => x.Id);
        asignacion.Property(x => x.EmpresaId).IsRequired();
        asignacion.Property(x => x.SucursalId).IsRequired();
        asignacion.Property(x => x.PuestoId).IsRequired();
        asignacion.Property(x => x.DepartamentoId).IsRequired();
        asignacion.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        asignacion.HasIndex(x => new { x.SucursalId, x.PuestoId }).IsUnique();
        asignacion.HasIndex(x => x.SucursalId);
        asignacion.HasIndex(x => x.PuestoId);
        asignacion.HasIndex(x => x.DepartamentoId);

        asignacion.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        asignacion.HasOne<Sucursal>()
            .WithMany()
            .HasForeignKey(x => x.SucursalId)
            .OnDelete(DeleteBehavior.Restrict);

        asignacion.HasOne<Puesto>()
            .WithMany()
            .HasForeignKey(x => x.PuestoId)
            .OnDelete(DeleteBehavior.Restrict);

        asignacion.HasOne<Departamento>()
            .WithMany()
            .HasForeignKey(x => x.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    // F1-PR2: ConfigureAlmacen retirado. El catálogo de almacenes
    // ahora vive en Millet.Almacen.Infrastructure.Persistence.AlmacenDbContext
    // (schema `almacen.*`). La migración `DropAlmacenPlaceholder` elimina
    // la tabla `compartido.almacenes`.
}
