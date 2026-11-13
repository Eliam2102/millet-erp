# Centro de avisos del ERP

Una campana en Topbar reúne pendientes autorizados del usuario actual. La lectura y la detección se guardan en `core.aviso_lectura`; los estados, firmas, responsables y reglas siguen perteneciendo al módulo de origen.

## Fuentes del primer lote

- Compras: firmas N1/N2 de OC y RQ, cancelación de OC y corrección de documentos devueltos/rechazados. El ciclo de RQ aumenta al transmitir; no usar `Version` como episodio.
- Almacén: avisos existentes de vale vencido, ajuste mayor aprobado y acta por revisar. Se conserva la lectura del origen. Leer un acta no la revisa.
- Incidencias: propias abiertas/en atención. Reasignación usa su historial; la incidencia de vale sin regularizar comparte clave con la obligación de Almacén.

Otros módulos no están conectados en este lote. No se añade un motor de workflows, bus, worker ni servicio desplegable nuevo.

## Conectar una fuente

Implementar `IFuenteAvisosUsuario` dentro del módulo dueño y registrarla en `Api/Notificaciones/NotificacionesRegistration.cs`. `ConsultarAsync` devuelve el conjunto COMPLETO de avisos elegibles para `ContextoAvisos`; no una página limitada. Si se consulta un origen paginado, recorrerlo hasta acreditar el final antes de devolver la lista. Un error se propaga; no devolver vacío ni un subconjunto como éxito.

Reutilizar los selectores, permisos y guards de los comandos y de la página destino. Tener permiso para firmar no implica poder leer esa página. No mostrar documento, contador o título fuera de empresa/sucursal/alcance. La sesión y pertenencia se comprueban también en el host. Para cruzar módulos, usar sus puertos públicos.

Crear la clave con `ClaveAviso.Crear`: empresa, dominio de la acción, tipo, ID, paso, ciclo/revisión y episodio de asignación estable. No usar la hora de consulta, el título, el folio mutable ni `Version`. Una acción con incidencia explicativa se cuenta una sola vez; acciones distintas del mismo documento conservan claves distintas. La clave del vale es `(empresa, almacen, movimiento, valeId, regularizar-vale, 1)`.

`AvisoUsuario` contiene texto plano y un destino interno generado por el módulo. `Categoria` es `Accion` o `Informativo`. Importar `LeidoOrigenAt` si existe. Implementar `MarcarLeidoAsync` cuando el origen ya persiste lectura; la operación es idempotente y no cambia el negocio. El centro propaga lectura a las fuentes visibles de una clave fusionada.

La fuente retira un pendiente cuando deja de ser elegible: resolución, cancelación, avance de etapa, sustitución de versión o pérdida de facultad. No consultar recibos para decidir si el negocio sigue pendiente. Un caso sin regla de destinatario no habilita difusión general; necesita diagnóstico explícito para un gestor autorizado.

## Sincronización y concurrencia

`POST /api/v1/notificaciones/sincronizar` recibe sucursal, filtro de módulo/condición, página de 1 a 50 (20 por defecto), continuación y cursor de detección decimal. Devuelve contexto validado, cobertura por fuente, totales conocidos, página y novedades globales. Las novedades y el total no dependen del largo de la página. Fuentes fallidas se muestran como cobertura incompleta, no cero pendientes.

El bloqueo transaccional PostgreSQL por usuario/empresa abarca **consulta de fuentes, recibos y corte de secuencia**, y se confirma antes de devolver respuesta. Bloquear solo el INSERT no basta: una petición con snapshot viejo podría avanzar hasta una secuencia de otra petición que no incluyó. No paralelizar consultas sobre el mismo DbContext. Las fuentes tienen plazo de cinco segundos y no aportan recibos parciales si fallan.

`POST /api/v1/notificaciones/{clave}/leer` revalida visibilidad actual; no resuelve ni autoriza. GET/PUT `/preferencia-sonido` pertenece solo al usuario actual. Nuevas preferencias comienzan silenciadas. No aceptar identidad desde el cuerpo.

## Interfaz y audio

El hook consulta cada 15 segundos cuando la sesión está visible, y al volver al foco/reconectar. Su clave de caché incluye usuario, empresa, sucursal y permisos. Cambiar contexto desmonta la vista anterior y descarta respuestas viejas. Mostrar sin leer y por hacer como cantidades separadas; las fallas de consulta no equivalen a ausencia de trabajo.

Web Audio se habilita mediante gesto explícito de activar/probar. Web Locks y un cursor compartido monotónico coordinan las pestañas del mismo navegador/contexto. Comparar como BigInt, nunca Number. Primera carga, retorno de desconexión, activación y recuperación de fuente se baselinen sin alarma histórica. Un fallo de audio no bloquea la campana. No se garantiza sonido con ERP cerrado, pestaña suspendida o equipo silenciado.

## Validación y límites

Pruebas del núcleo: `SharedKernel.UnitTests/Notificaciones`. HTTP y PostgreSQL: `Api.IntegrationTests/Notificaciones`, con fixtures de autenticación del proyecto. Fuentes: `Compras.UnitTests/Notificaciones` y `Almacen.UnitTests/Avisos`. UI/audio: `frontend/src/features/notificaciones`.

Seguir `docs/qa/gate-integracion.md`. Datos ficticios identificados, bases aisladas, evidencias fuera del repositorio. La compilación y el ensayo local no acreditan despliegue, QA ni aceptación Millet. Ninguna migración debe aplicarse a un ambiente compartido sin su autorización.
