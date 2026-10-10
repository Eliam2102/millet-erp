# Inicio v2 · Plantilla 5.3

Implementación local del 7-oct-2026 en `feature/panel-inicio`. Sin commit,
push, cambios de backend, infraestructura ni variables de entorno.

## Presentación y permisos

`PanelInicio` compone saludo, indicadores, bandeja de acción y columna lateral.
Escritorio: padding 28/32, columnas 1.55fr/1fr, gap 24. Móvil: padding 16,
una columna, indicadores 2×2 y módulos en dos columnas. `AppShell` cede
el padding de Inicio para evitar sumarlo al de la plantilla; otras rutas lo conservan.

`useResumenInicio` reutiliza `useConteosInicio` y `pendienteVisible` de `config.ts`.
Consulta solamente los conteos elegibles por permisos y ordena con funciones puras.
Muestra los primeros cuatro KPI (menos si el rol tiene menos de cuatro disponibles).
La bandeja incluye todos los conteos positivos permitidos. Carga y error nunca se
presentan como cero ni como «No tienes pendientes»; cada error admite reintento.

Reabasto, vencimientos sin conteo, liberaciones, pedidos, excepciones y periodo
contable de la v1 no son contadores verificables y no generan filas de pendientes.
Sus pantallas siguen accesibles por la navegación autorizada. Los módulos se
obtienen de `accesosNavegacion` y `rutaPermitida`: una tarjeta por módulo, enlace
al primer acceso permitido, ícono del módulo y descripción del acceso de destino.

La sesión expone nombre y permisos, pero no el nombre del rol: se muestra
`Rol: [ROL]`, sin confundir `puestoNombre` con un rol. Se conserva la alerta
original de cuenta sin asignaciones cuando la sesión no tiene empresas.

## Inventario de conteos y antigüedad

Todos los endpoints son GET existentes. Prefijo común `/api/v1/`.
Los listados usan `offset=0&limit=1`, excepto OC, que usa `page=1&pageSize=1`,
y el agregado de partidas, que no usa paginación. No se recorren todas las páginas.

| KPI elegible | Endpoint y filtro | Número | Antigüedad del registro más viejo |
|---|---|---|---|
| Requisiciones por autorizar | `compras/pendientes-autorizacion`; `nivelPendiente` 1/2 si corresponde | `total` | `items[0].fechaSolicitud`; orden FIFO ascendente garantizado |
| OC por autorizar | `compras/ordenes/pendientes-autorizacion`; `nivel=Nivel1/Nivel2` si corresponde | `totalCount` | `items[0].fechaDocumento`; orden FIFO ascendente garantizado |
| Partidas abiertas | `compras/ordenes/partidas-abiertas/kpis` | `countPartidasAbiertas`; subdato `countAtrasadas` | Sin fecha en el agregado |
| Recepciones en borrador | `almacen/recepciones`; `estado=0` | `total` | Vacía: orden descendente por fecha de movimiento/registro, sin sort configurable |
| Proveedores en revisión | `datos-maestros/proveedores`; `estatus=2` | `total` | Vacía: orden por clave, sin sort por fecha |
| Facturas en revisión | `cuentas-por-pagar/facturas`; `estado=2` | `total` | Vacía: fecha de documento descendente, sin sort configurable |
| Pagos a cuenta por aplicar | `tesoreria/pagos-cuenta`; `incluirParciales=true` | `total` | `items[0].fechaValor`; orden ascendente garantizado |
| Depósitos por confirmar | `tesoreria/depositos`; `estado=1` | `total` | Vacía: estado y creación descendente, sin sort configurable |
| Alertas de cartera pendientes | `cuentas-por-cobrar/alertas`; `atendida=false` | `total` | Vacía: disparo descendente, sin sort configurable |

Fuentes inspeccionadas en lectura: handlers `ListarPendientesAutorizacionHandler`,
`ListarPendientesAutorizacionOcHandler`, `RecepcionQueries`, `ListarProveedoresQuery`,
`ListarFacturasQuery`, `PagosACuentaAbiertosQuery`, `DepositosQuery` y
`AlertasCarteraCommands` en `backend/src`. Las fechas representan antigüedad desde
solicitud/documento/fecha valor; **no** tiempo transcurrido dentro del estado pendiente.
Se ignoran fechas ausentes, inválidas o futuras. DateOnly conserva su calendario;
los instantes se convierten a la zona del ERP (`TIMEZONE`).

El nombre anterior «Órdenes abiertas» se corrigió a «Partidas abiertas» porque
el agregado cuenta partidas, no órdenes distintas.

Los enlaces conservan las capacidades actuales de cada listado. En OC, un
autorizador de ambos niveles ve el conteo conjunto en Inicio y abre la bandeja
existente, cuyo selector permite revisar un nivel a la vez. Esa pantalla no
admite un filtro de nivel por URL. No se agregó un parámetro que ignoraría.

## Prioridad

`prioridad.ts` contiene `UMBRALES_INICIO`, `calcularPrioridad` y `compararPrioridad`.
Reglas operativas de la vista, no SLA ni estados aprobados del dominio:

- Normal: cero pendientes, o sin superar ningún umbral.
- Atención: antigüedad **>3 días**, conteo **>10**, o alguna partida atrasada.
- Crítico: antigüedad **>7 días**, conteo **>25**, o **>10 partidas atrasadas**.
- Se toma el nivel mayor. Orden: crítico, atención, normal; desempates por
  antigüedad, cantidad y orden estable del catálogo.
- Antigüedad urgente (**>3 días**) se muestra en `text-danger-fg font-medium`.
- Los DTO no ofrecen monto comparable ni bloqueo interárea: no se inventan
  factores de impacto/bloqueo del modelo completo de §7.1.

## Recientes

Sí existe consulta real: `useAuditoria` en
`modules/administracion/api/auditoria.ts`, endpoint `GET /api/v1/admin/auditoria`.
El backend admite `usuarioId` y `empresaId`, ordena `Timestamp DESC` y requiere
`admin.auditoria.leer`. Inicio consulta los últimos siete días, cuatro eventos,
solo si existen permiso, usuario y empresa activa. Sin ellos, omite la sección
por completo y no consulta auditoría. No simula historial para el resto de roles.

Muestra `entidadEtiqueta` (folio cuando existe; de lo contrario etiqueta/entidad),
`resumen` o `operacion` y tiempo relativo. El enlace lleva a la bitácora existente;
no se inventa una ruta de detalle para entidades sin correspondencia navegable.
No se muestran `cambios`, identificadores de sesión ni información técnica.

## Verificación y pendientes

Pruebas unitarias: niveles y fronteras, prioridad, desempates, fechas locales,
fechas inválidas/futuras y motivos. Pruebas del componente con MSW: permisos,
menos de cuatro elegibles, selección de cuatro entre cinco, vacío, carga,
error parcial/reintento, filtros de autorización, fechas FIFO y clase danger,
ring crítico, módulos y actividad real filtrada por usuario/empresa.

Resultados verificados sobre la implementación final:

- `npx tsc --noEmit -p tsconfig.json`: salida 0. Como el tsconfig raíz contiene
  referencias, se complementó con el build de producción.
- `npm run -s typecheck:test`: salida 0.
- `npm run -s lint`: salida 0, 0 errores y 10 advertencias fuera de Inicio.
- `npx vitest run`: 331 archivos, 1,933 pruebas aprobadas.
- `npm run build`: salida 0; advertencia de bundles mayores de 500 kB.
- `git diff --check`: salida 0.

Pendiente: pin y personalización, porque no existe almacenamiento de preferencias.
Pendiente: nombre del rol en el contrato de sesión.
Pendiente: cotejo directo con Figma, nodos 5:7 y 5:2259. El plugin no está
instalado y el enlace no fue accesible desde la herramienta web.
Pendiente: comprobación visual a 1440×900 y 390×844. Se generó una vista aislada
con DOM de React y datos ficticios, pero el entorno impidió iniciar Chromium
(`MachPortRendezvousServer: Permission denied`); el navegador conectado rechazó
abrir `file:` por su política. No se levantaron servidores ni se eludió esa política.
La validación automatizada no equivale a integración real, UAT o aceptación visual.

## Borrador local para la bitácora del proyecto

7-oct-2026: Inicio v1 se rehízo localmente siguiendo §5.3, conservando permisos y
consultas existentes. Añade KPI priorizados, antigüedad verificable y actividad
personal solo para auditoría autorizada. Fuente: estos archivos y pruebas de la
rama `feature/panel-inicio`. Sin publicación ni aceptación Millet.
Pendiente trasladar esta nota a Obsidian: la bóveda queda fuera del alcance de
escritura de esta sesión. Siguiente acción: comparar con Figma y revisar con
una sesión real autorizada, sin confundir fixtures con datos de operación.
