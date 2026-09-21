# Diagnóstico y plan de reanudación · 20 de septiembre de 2026

Este documento concentra el estado operativo verificable para reanudar la construcción de Fase 1. Sustituye como referencia diaria a los estados congelados en `CURRENT_STATE.md`, `09-plan-operativo-ola-1a.md`, `11-matriz-arranque-ola1a.md` y `16-plan-ejecucion-y-cierre.md`; esos archivos se conservan como evidencia histórica y de planeación.

No modifica por sí mismo el alcance contratado, las horas fuente, los responsables externos ni los estados publicados en Notion o ClickUp.

## Evidencia externa consultada

- [Notion · Desarrollo Fase 1](https://app.notion.com/p/3dd36ce8974981c49dbaff9768d8e8a7)
- [Notion · Ola 1A](https://app.notion.com/p/3dd36ce89749813bb590cb1577bbd046)
- [ClickUp · Ola 0](https://app.clickup.com/9017291387/v/l/li/901717118871)
- [ClickUp · Ola 1A](https://app.clickup.com/9017291387/v/l/li/901717118872)
- [ClickUp · ADM-01](https://app.clickup.com/t/86e3a6c9g)
- [ClickUp · ADM-02](https://app.clickup.com/t/86e3a6c9k)
- [Slack · propuesta multiempresa a multisucursal](https://bhosolutionsv-m4g8895.slack.com/archives/D0C2FTQ2EJG/p1789775679739899)

## 1. Conclusión ejecutiva

El proyecto no está detenido por falta de arquitectura ni por una base de código rota. Está detenido por una **ruptura de trazabilidad entre alcance, ejecución y código**:

- `main` compila y sus gates automatizados pasan;
- ClickUp muestra ADM-01 bloqueada y ADM-02 en curso;
- GitHub no contiene ramas ni pull requests de esos trabajos;
- Notion, ClickUp y los documentos locales no coinciden en responsables ni horas;
- una propuesta todavía no aprobada intenta cambiar ADM-01 de multiempresa a multisucursal;
- las 139 funciones continúan sin resultado individual de QA/UAT.

La prioridad no es volver a diseñar todo el ERP. Es recuperar el trabajo activo, congelar una fuente de verdad operativa y construir por cortes verticales pequeños con evidencia.

## 2. Qué arquitectura tiene realmente

La solución es un **monolito modular**. No usa MVC como arquitectura principal.

- Backend: .NET 9, ASP.NET Core Minimal APIs, módulos por bounded context, CQRS/MediatR y puertos/adaptadores de estilo hexagonal.
- Persistencia: PostgreSQL compartido, un esquema y `DbContext` por módulo, con convenciones transversales en `SharedKernel`.
- Frontend: React/Vite organizado por features y rutas.
- Integración: adaptadores para A+W, FiscalAPI/PAC, Entra ID, Service Bus y servicios on-prem; varios conservan stubs o dependen de infraestructura externa.

En términos prácticos: **sí tiene estructura Clean/Hexagonal dentro de un monolito modular**, pero no todos los módulos tienen el mismo nivel de madurez y algunos contextos de Fase 1 todavía no existen como módulo dedicado.

## 3. Fuentes y precedencia desde este corte

Cuando dos fuentes se contradigan, usar este orden:

1. Matriz funcional y decisión de alcance explícitamente aceptada.
2. Código integrado en `main`, migraciones y pruebas reproducibles.
3. Pull request o rama publicada, vinculada a la función correspondiente.
4. ClickUp para responsable, estado de ejecución y bloqueo actual.
5. Notion para contexto funcional, plan y aceptación.
6. Mensajes, archivos enviados o propuestas: evidencia de conversación, no aprobación.

Reglas:

- Una tarea `En progreso` sin rama o PR publicado no acredita construcción integrada.
- Un documento que solicita aprobación no cambia el alcance hasta que exista decisión registrada.
- La existencia de clases, endpoints o pantallas no equivale a aceptación funcional.
- `Terminado` requiere PR, pruebas, evidencia y aceptación aplicable.

## 4. Estado verificable del repositorio

### Git y GitHub

- Repositorio: `https://github.com/Eliam2102/millet-erp`.
- Rama local y remota: `main` en `82a05c4`.
- Último cambio de código base auditado: `f0f50eb`; los cambios posteriores hasta `82a05c4` son de handoff/documentación y configuración de entrega.
- Estado local antes de este documento: limpio y sincronizado con `origin/main`.
- Ramas remotas activas: sólo `main`.
- Pull requests encontrados: uno, documental, ya integrado (`#1`).
- No se localizaron ramas o PRs de ADM-01 ni ADM-02.
- Visibilidad observada el 20/09/2026: **público**.
- Protección de `main`: **no configurada**.

La visibilidad contradice los documentos que describen el repositorio como privado. Cambiar visibilidad o reglas es una decisión del propietario y no se realizó durante esta revisión.

### Gate técnico ejecutado el 20/09/2026

- Backend Release: compilación completa, 0 advertencias y 0 errores.
- Backend unitarias: 2,279 aprobadas, 0 fallidas.
- Backend integración Debug: 489 aprobadas, 0 fallidas:
  - Integraciones A+W: 7;
  - Compras: 120;
  - API: 362.
- Frontend: build aprobado.
- Frontend: lint aprobado.
- Frontend: 278 archivos y 1,567 pruebas aprobadas.

Las pruebas de integración requieren:

1. una base limpia;
2. los 12 `DbContext` migrados;
3. ejecución serial;
4. configuración `Debug`, porque `/api/dev/fake-login` está protegido por `#if DEBUG`.

El script `tools/validate-integration-isolated.sh` automatiza estas condiciones sin usar la base de otro proyecto ni la base de desarrollo habitual.

### Qué no acredita este gate

- autenticación real con Entra ID;
- conexión real con A+W;
- conexión real con PAC/FiscalAPI;
- migración real desde SAP;
- despliegue en QA o producción;
- recorrido funcional de las 139 funciones;
- aceptación de las áreas de Millet.

## 5. Conciliación de planeación

### Alcance global de Fase 1

- 139 funciones.
- 628 horas funcionales base.
- Estado de matriz: 35 existentes, 55 parciales y 49 no existentes.
- QA/UAT individual: 139 pendientes.

### Ola 1A

- 15 funciones.
- Base funcional del inventario: 76 h.
- Planeación local: 18 h transversales + 8 h QA.
- Total operativo calculable: 102 h.

Contradicciones publicadas:

| Fuente | Horas |
|---|---:|
| Inventario funcional | 76 h base |
| Lista de ClickUp | 84 h |
| Tarea padre de ClickUp | 97 h base |
| Plan local completo | 102 h |

**Recomendación para aprobación:** conservar 76 h como base funcional trazable y 26 h como bolsa transversal/QA, total 102 h. No usar 84 h o 97 h hasta documentar su fórmula.

### Responsables

Hay tres distribuciones distintas:

- plan local y página de Ola 1A en Notion: Geovany 8 / Uzziel 7;
- ClickUp actual: Geovany 7 / Uzziel 8, porque ADM-02 está asignada a Uzziel;
- propiedades de las filas en Notion: Geovany 4 / Uzziel 11.

Hasta conciliar las fuentes, ClickUp representa la asignación de ejecución observada, pero debe alinearse con Notion y con una decisión explícita del responsable del proyecto.

## 6. Trabajo activo que debe recuperarse

### ADM-01 · empresas, sucursales, departamentos y puestos

Estado observado: bloqueada.

Existe una propuesta para cambiar de multiempresa a una empresa multisucursal. La propuesta:

- reconoce que el repositorio ya soporta varias razones sociales;
- usa como indicios la configuración de una sola empresa local y la ausencia de varias empresas sembradas;
- solicita validación del cliente;
- añade usuarios por sucursal, perfiles corporativos y propagación gradual a otros módulos.

Esos indicios no sustituyen una decisión funcional. El ADR-0011 y la arquitectura vigente parten de varias razones sociales/RFC y el aislamiento por `empresa_id` ya atraviesa dominio, identidad, persistencia y permisos.

Decisión técnica provisional:

- **no retirar multiempresa**;
- tratar multisucursal como alcance adicional dentro de cada empresa;
- continuar sólo los componentes no controvertidos de ADM-01: empresa, sucursal, departamento y puesto;
- no mover a ADM-01 lo que pertenece a ADM-02 o ADM-12 sin reestimación;
- si Millet aprueba el cambio, registrar un ADR/adenda y actualizar matriz, criterios, horas y pruebas de aislamiento.

### ADM-02 · usuarios, roles y autenticación

Estado observado: en progreso, asignada a Uzziel en ClickUp.

Hay comentarios sobre login Entra ID, grupos/roles y logout pendiente, pero no existe rama, commit o PR enlazado. Para recuperar el avance se requiere:

- rama remota o WIP PR;
- lista exacta de archivos modificados;
- instrucciones reproducibles sin secretos;
- resultado de login, redirección y logout;
- decisión sobre roles que requieren grupo de Entra;
- pruebas locales y evidencia del entorno real por separado.

No debe considerarse avance integrado hasta publicar esa evidencia.

## 7. Plan de reanudación

### Etapa 0 · Control y recuperación — antes de abrir más frentes

1. Confirmar si el repositorio debe seguir público; si no, volverlo privado.
2. Definir el control de `main`: protección técnica o, mientras tanto, regla operativa de PR obligatorio.
3. Pedir a Geovany y Uzziel publicar sus ramas actuales como WIP PR, sin esperar a que estén terminadas.
4. Vincular cada PR con la función y tarea de ClickUp correspondiente.
5. Resolver la decisión ADM-01 con el responsable funcional; hasta entonces conservar multiempresa.
6. Conciliar 76/84/97/102 h y publicar una sola fórmula.
7. Corregir responsables de Notion para que coincidan con la asignación acordada.
8. Corregir la secuencia Ola 0: el cierre observado fue O0-06; los documentos aún mencionan O0-07.

Salida de la etapa:

- ningún trabajo activo existe sólo en una laptop;
- `main` tiene un control de integración definido;
- ADM-01 tiene decisión o alcance provisional escrito;
- horas y responsables tienen una versión única.

### Etapa 1 · Cerrar el corte vertical de identidad

Orden recomendado:

1. Recuperar y revisar ADM-02.
2. Cerrar login, redirecciones y logout.
3. Separar evidencia local/fake de evidencia Entra real.
4. Integrar por PR pequeño y reversible.
5. Ejecutar gate técnico y recorrido funcional.

No iniciar en paralelo una reestructuración amplia de ADM-01 mientras ADM-02 siga sin PR, porque identidad, empresa activa y permisos son dependencias de casi toda la ola.

### Etapa 2 · Fundaciones administrativas

Construir o validar en este orden:

1. ADM-01: estructura empresa/sucursal/departamento/puesto, sin retirar `empresa_id`.
2. ADM-12: aislamiento y acceso a datos.
3. ADM-10: permisos por rol.
4. ADM-03: auditoría de operaciones.
5. ADM-11: decisión interna pendiente antes de construir.

Cada función debe entregar una ruta reproducible y pruebas de aislamiento, no sólo CRUD.

### Etapa 3 · Catálogos e integraciones de la ola

1. ADM-04, ADM-05 y ADM-08: catálogos administrativos.
2. ADM-06 y ADM-07: contratos A+W con fake/local primero y conexión real como gate externo separado.
3. ADM-09: configuración fiscal con sandbox; producción queda fuera hasta credenciales, autorización y pruebas controladas.

### Etapa 4 · Fundación contable

CON-01, CON-02 y CON-03 no deben tratarse como tres CRUD aislados. No existe un bounded context contable dedicado y los puntos actuales están repartidos en Centros de Costo/Compartido.

Antes de programar:

1. ADR del límite del módulo contable;
2. modelo mínimo de catálogo, dimensiones y periodos;
3. contratos con Compras, CxP, Facturación y Tesorería;
4. migración inicial y permisos;
5. cortes verticales pequeños para CON-01, CON-02 y CON-03.

## 8. Forma de trabajo obligatoria

### Definition of Ready

Una función puede iniciar sólo si tiene:

- ID y descripción de alcance;
- responsable único;
- criterio de aceptación;
- dependencias y decisiones resueltas o explícitamente excluidas;
- horas base identificadas;
- rama nombrada y tarea enlazada;
- datos de prueba permitidos.

### Definition of Done

Una función puede cerrar sólo con:

- PR revisado e integrado;
- build, lint y pruebas aplicables en verde;
- migraciones/configuración documentadas;
- ruta funcional reproducible;
- evidencia vinculada en ClickUp;
- regresión de lo existente;
- resultado QA;
- UAT cuando corresponda.

### Convención de integración

- Una rama por corte pequeño, no una rama por módulo completo.
- WIP PR durante el desarrollo para que el trabajo sea visible.
- No hacer commits directos a `main`.
- No mezclar cambio de alcance, refactor transversal y funcionalidad en el mismo PR.
- Todo PR debe citar la función `F1-*` y la tarea de ClickUp.
- Si una tarea permanece `En progreso` sin rama/PR visible, su avance queda `Por confirmar`.

## 9. Tablero inmediato recomendado

| Prioridad | Acción | Responsable | Estado verificable |
|---:|---|---|---|
| 1 | Publicar WIP PR de ADM-02 | Uzziel | Por confirmar |
| 2 | Publicar rama/evidencia de ADM-01 | Geovany | Por confirmar |
| 3 | Resolver multiempresa vs multisucursal | Eliam/Ángel + Millet | Pendiente de decisión |
| 4 | Alinear responsables Notion/ClickUp | Coordinación | Pendiente |
| 5 | Conciliar horas 76/84/97/102 | Coordinación | Pendiente |
| 6 | Decidir visibilidad y protección de GitHub | Propietario del repositorio | Pendiente |
| 7 | Integrar ADM-02 mediante PR | Desarrollo + revisión cruzada | Bloqueado por evidencia |
| 8 | Reanudar ADM-01 con alcance confirmado | Desarrollo | Bloqueado por decisión |

## 10. Criterio para declarar el repositorio listo para construcción continua

El repositorio estará listo cuando se cumplan simultáneamente:

- estado local y remoto sincronizados;
- ramas activas publicadas;
- PR obligatorio y revisión cruzada operando;
- gate automatizado reproducible;
- una sola asignación y una sola cifra de horas por ola;
- decisiones de alcance registradas;
- ClickUp, Notion y GitHub enlazados por función;
- primera función de Ola 1A integrada con evidencia completa.

Al corte de este documento, el gate técnico está verde, pero la continuidad de desarrollo permanece **condicionada a recuperar las ramas de ADM-01/ADM-02 y resolver las contradicciones operativas**.
