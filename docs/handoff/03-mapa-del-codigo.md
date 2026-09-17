# Mapa del código y puntos de entrada

## Arquitectura

- Backend ASP.NET Core/.NET 9 en `backend/`.
- Frontend React/TypeScript/Vite en `frontend/`.
- PostgreSQL para desarrollo local.
- Infraestructura Azure declarada en `infra/`.
- Servicios on-prem para A+W en `on-prem/`.
- Decisiones técnicas en `docs/decisiones/`.
- Documentación por módulo en `docs/modulos/`.

## Backend

El host es `backend/src/Api`. Los módulos con proyecto propio incluyen:

- SharedKernel y Compartido.
- Identidad y Administración.
- Catálogos y Datos Maestros.
- Compras y Almacén.
- Cuentas por Pagar y Cuentas por Cobrar.
- Facturación y Tesorería.
- Centros de Costo.
- Integraciones A+W y Fiscal.

Cada tarea debe comenzar localizando: endpoint, caso de aplicación, dominio, persistencia/migración y pruebas. No asumir que la existencia de una carpeta equivale a funcionalidad terminada.

## Frontend

- Rutas: `frontend/src/routes/`.
- Funcionalidades: `frontend/src/features/`.
- Componentes compartidos: `frontend/src/components/`.
- Integración de API/estado: revisar el feature correspondiente y sus pruebas.

## Integraciones

- A+W: `backend/src/Integraciones.Aw/` y `on-prem/`.
- Fiscal/PAC: `backend/src/Integraciones.Fiscal/`.
- Los archivos y adaptadores locales son infraestructura de desarrollo; los contratos reales dependen de documentación, ambientes y accesos de Millet.

## Documentación existente

- `CURRENT_STATE.md`: inventario histórico del repositorio; revisar fecha antes de usarlo como verdad actual.
- `docs/arquitectura.md`: vista cross-módulo.
- `docs/decisiones/`: ADRs.
- `docs/modulos/`: levantamientos, diseños, planes y runbooks existentes.
- `docs/operacion/`: operación y UAT técnica existente.

La fuente de alcance Fase 1 vive en la base de conocimiento de Notion. El repositorio no sustituye la matriz funcional aprobada.

