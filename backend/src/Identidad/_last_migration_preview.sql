START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE TABLE identidad.usuario_servicio (
        id uuid NOT NULL,
        nombre character varying(120) NOT NULL,
        entra_app_id uuid NOT NULL,
        entra_object_id uuid NOT NULL,
        empresa_id uuid NOT NULL,
        activo boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamp with time zone NOT NULL,
        deactivated_at_utc timestamp with time zone,
        notes text,
        version integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        created_by text,
        updated_by text,
        deleted_at timestamp with time zone,
        CONSTRAINT pk_usuario_servicio PRIMARY KEY (id),
        CONSTRAINT fk_usuario_servicio_empresas_empresa_id FOREIGN KEY (empresa_id) REFERENCES compartido.empresas (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE TABLE identidad.usuario_servicio_permiso (
        usuario_servicio_id uuid NOT NULL,
        permiso_clave character varying(120) NOT NULL,
        CONSTRAINT pk_usuario_servicio_permiso PRIMARY KEY (usuario_servicio_id, permiso_clave),
        CONSTRAINT fk_usuario_servicio_permiso_usuario_servicio_usuario_servicio_ FOREIGN KEY (usuario_servicio_id) REFERENCES identidad.usuario_servicio (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0001-0000-0000-000000000001', 'cotizaciones', 'integraciones.aw.cotizaciones.crear', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Enviar cotizaciones EDI desde Glass Agent al ERP para correlación con A+W', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0001-0000-0000-000000000002', 'cotizaciones', 'integraciones.aw.cotizaciones.consultar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Consultar el estado de correlación de una cotización enviada', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0001-0000-0000-000000000003', 'cotizaciones', 'integraciones.aw.cotizaciones.reintentar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Forzar el reintento de drop o de correlación de una cotización', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0002-0000-0000-000000000001', 'pedidos', 'integraciones.aw.pedidos.consultar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Consultar pedidos espejo de A+W vía Hybrid Connection (read-only)', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0003-0000-0000-000000000001', 'clientes', 'integraciones.aw.clientes.consultar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Consultar el catálogo de clientes de A+W (read-only)', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0004-0000-0000-000000000001', 'articulos', 'integraciones.aw.articulos.consultar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Consultar el catálogo de artículos de A+W (read-only)', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0005-0000-0000-000000000001', 'inventario', 'integraciones.aw.inventario.consultar', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Consultar inventario de A+W (read-only) para validación de stock', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0006-0000-0000-000000000001', 'administracion', 'integraciones.aw.administracion.servicios', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Administrar el catálogo de service principals (UsuarioServicio)', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    INSERT INTO identidad.permisos (id, accion, codigo, created_at, created_by, deleted_at, descripcion, modulo, recurso, updated_at, updated_by, version)
    VALUES ('00000006-0006-0000-0000-000000000002', 'administracion', 'integraciones.aw.administracion.configuracion', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', NULL, 'Administrar la configuración del módulo Integraciones.Aw', 'integraciones', 'aw', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'seed', 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE INDEX ix_usuario_servicio_empresa_id ON identidad.usuario_servicio (empresa_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE UNIQUE INDEX ix_usuario_servicio_entra_app_id ON identidad.usuario_servicio (entra_app_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    CREATE INDEX ix_usuario_servicio_entra_object_id ON identidad.usuario_servicio (entra_object_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260515205658_AddUsuarioServicio') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260515205658_AddUsuarioServicio', '9.0.4');
    END IF;
END $EF$;
COMMIT;

