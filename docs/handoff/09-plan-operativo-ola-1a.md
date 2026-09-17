# Plan operativo · Ola 1A Fundaciones

## Objetivo

Dejar operables las fundaciones que consumen las olas posteriores: empresas y segregación, identidad/permisos, catálogos compartidos, maestros, configuración fiscal, documentos, catálogo contable, dimensiones y periodos. La salida exige evidencia técnica y QA; no basta con consumir las horas.

## Capacidad y tamaño

- Alcance funcional: 15 funcionalidades, 76 h base.
- Trabajo transversal previo/asociado: 18 h base.
- QA específico de funciones existentes: 8 h internas para cuatro recorridos, evidencia y regresión.
- Total operativo de referencia: 102 h. Las 5 h adicionales de QA se toman de la reserva interna; no aumentan las 840 h del alcance total.
- Inicio operativo: 17 de septiembre de 2026.
- Salida objetivo: 7 de octubre de 2026, condicionada a las entradas externas identificadas.

La preparación de repositorio, ambiente local y seguridad se administra en la lista separada **Ola 0 · Preparación y onboarding**. Geovany y Uzziel deben completar sus tareas y cargar evidencia; Eliam valida esa evidencia mediante `O0-07 · Gate de salida · habilitación de Ola 1A`. Sólo después se completa `O1A-00 · Control de entrada · Ola 0 aprobada` y comienza el desarrollo funcional de Ola 1A. Las 18 h de preparación siguen formando parte del total operativo de referencia y no amplían el alcance.

## Asignación nominal vigente

Para eliminar `Dev 1/Dev 2` del plan operativo se usa este mapeo:

- **Uzziel = Dev 1:** catálogos, proveedores, centros/dimensiones, configuración fiscal y frente contable.
- **Geovany = Dev 2:** identidad/permisos, sincronizaciones A+W, contratos de integración y datos maestros externos.
- **Revisión cruzada:** el desarrollador que no sea responsable nominal revisa el PR y participa en la regresión; esto no cambia al responsable de la funcionalidad.

Esta distribución equilibra especialización y revisión cruzada; no cambia el alcance ni convierte una dependencia del cliente en desarrollo interno.

## Secuencia y entregables

| Orden | ID | Responsable | Resultado requerido | Entrada crítica | Aceptación Millet |
|---:|---|---|---|---|---|
| 1 | Ola 0 + `O0-07` + `O1A-00` | Ambos; validación de Eliam | Repo privado, arranque reproducible, migraciones, pruebas, evidencia y reglas de PR operables; habilitación explícita de Ola 1A | Acceso efectivo al repositorio y herramientas locales | Eliam / responsable técnico VILO |
| 2 | F1-ADM-01, 03, 10, 11 | Geovany | Levantar lo existente, ejecutar caso, documentar evidencia y regresión | Datos ficticios; matriz de permisos | Guillermo Pantoja / Jorge Toache según caso |
| 3 | F1-ADM-02, 12 | Geovany | Roles, permisos y segregación por empresa verificables | Usuarios/roles vigentes; Entra para cierre real | Guillermo Pantoja + Jorge Toache |
| 4 | F1-ADM-04, 05, 08 | Uzziel | Catálogos compartidos, proveedor y jerarquías sin duplicados | Catálogos actuales de Millet | Guillermo Pantoja; Fernando Alejos; Laura Cerón según dato |
| 5 | F1-ADM-06, 07 | Geovany | Contratos y sincronización de clientes/productos A+W conforme a evidencia actual | Contratos, tablas, estados y muestras A+W | Jorge Toache + área consumidora |
| 6 | F1-ADM-09 | Uzziel | Configuración por empresa sin exponer certificados | Parámetros, series, PAC y certificados por canal seguro | Sorandi Martínez + Jorge Toache |
| 7 | F1-CON-01, 02, 03 | Uzziel | Catálogo de cuentas, dimensiones y periodos con autorización | Catálogo contable y reglas vigentes | Laura Cerón |
| 8 | QA-1A | Revisión cruzada | Cuatro funciones existentes levantadas, ejecutadas, evidenciadas y sometidas a regresión | Ambiente y datos de prueba | Responsables anteriores |

## Calendario por puertas

| Periodo objetivo | Trabajo | Puerta de salida |
|---|---|---|
| 17–21 sep | Ola 0: publicación segura, arranque local, pruebas, onboarding y evidencia | Ambos devs pueden clonar, levantar, iniciar sesión y ejecutar pruebas; Eliam completa `O0-07` y se habilita `O1A-00` |
| 21–24 sep | Inventario real de funciones existentes y F1-ADM-02/12 | Permisos/segregación reproducibles; diferencias registradas |
| 23–29 sep | F1-ADM-04/05/08 y contratos F1-ADM-06/07 | Catálogos trazados a la fuente de Millet; contratos A+W versionados o bloqueo nominal |
| 28 sep–2 oct | F1-ADM-09 y F1-CON-01/02/03 | Parámetros seguros; base contable y periodos operables |
| 5–7 oct | QA, regresión, corrección y evidencia | 15 fichas con resultado o bloqueo externo explícito; salida de Ola 1A decidida por evidencia |

Las fechas no autorizan diseñar datos o reglas que Millet ya opera. Si un insumo no llega, se completa todo lo local posible y se bloquea únicamente la variante dependiente.

## Pruebas mínimas

- Separación entre las dos empresas y negativa de acceso cruzado.
- Rol permitido y rol denegado por pantalla/acción.
- Alta/consulta de estructuras, catálogos, proveedores y documentos.
- Auditoría con usuario, fecha, entidad y cambio.
- Idempotencia y error controlado en sincronización A+W.
- Certificado/configuración fiscal aislados por empresa, sin secretos en evidencia.
- Cuenta, dimensión y periodo: alta, consulta, cierre, intento no autorizado y reapertura autorizada.
- Regresión de navegación, permisos y módulos consumidores.

## Evidencia por funcionalidad

Cada ID debe vincular: commit/PR, archivos modificados, migración o configuración, pruebas ejecutadas, dato de prueba, captura/log, resultado de regresión, dependencia usada y aceptación o bloqueo nominal. Las funciones con 0 h de construcción no están exentas.

## Correcciones al borrador anterior

- `F1-CON-02` aparecía del 12 al 13 de noviembre aunque pertenece a Fundaciones; se adelanta al bloque del 28 de septiembre al 2 de octubre.
- `Dev 1/Dev 2` se sustituye por Geovany/Uzziel según la asignación nominal vigente en Notion.
- La preparación técnica se administra como **Ola 0 independiente**. Su salida requiere evidencia de ambos desarrolladores, validación de Eliam en `O0-07` y activación de `O1A-00` antes de cualquier desarrollo funcional de Ola 1A.
- A+W, PAC, Entra y catálogos reales no se consideran validados hasta recibir y probar los insumos de Millet.
- Las 3 h originales de QA-1A no alcanzan para levantar, ejecutar, documentar y regresar cuatro funciones existentes; se asignan 8 h usando 5 h de reserva interna.

## Pendientes que impiden cerrar, no comenzar

- Usuarios GitHub exactos de Geovany y Uzziel.
- Ambientes, VPN, URLs y accesos reactivados por Millet.
- Inventario técnico A+W y documentación de portales satélite.
- Catálogos, usuarios/roles, catálogo contable, dimensiones y parámetros fiscales vigentes.
- Casos y usuarios de UAT.
