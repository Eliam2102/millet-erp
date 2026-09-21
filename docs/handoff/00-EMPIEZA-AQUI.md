# Handoff técnico de desarrollo · empieza aquí

Este paquete explica **el repositorio y lo construido**, no sustituye la planeación funcional de Notion ni la solicitud de insumos a Millet.

## Objetivo

Geovany y Uzziel deben poder clonar el repositorio, levantar el stack local, ejecutar pruebas, localizar módulos y comenzar una tarea de Ola 1A sin depender de una explicación verbal.

## Orden de lectura

1. [18-diagnostico-y-plan-reanudacion-2026-09-20.md](18-diagnostico-y-plan-reanudacion-2026-09-20.md)
2. [19-plan-control-construccion-al-15-diciembre.md](19-plan-control-construccion-al-15-diciembre.md)
3. [01-arranque-local.md](01-arranque-local.md)
4. [02-estado-verificado.md](02-estado-verificado.md)
5. [03-mapa-del-codigo.md](03-mapa-del-codigo.md)
6. [04-forma-de-trabajo.md](04-forma-de-trabajo.md)
7. [05-dependencias-externas.md](05-dependencias-externas.md)
8. [06-checklist-primer-dia.md](06-checklist-primer-dia.md)
9. [07-publicacion-y-seguridad.md](07-publicacion-y-seguridad.md)
10. [08-inventario-funcional-fase1.md](08-inventario-funcional-fase1.md)
11. [09-plan-operativo-ola-1a.md](09-plan-operativo-ola-1a.md)
12. [10-auditoria-35-existentes.md](10-auditoria-35-existentes.md)
13. [11-matriz-arranque-ola1a.md](11-matriz-arranque-ola1a.md)
14. [12-borrador-publicacion-notion-clickup.md](12-borrador-publicacion-notion-clickup.md)
15. [13-manifiesto-clickup-ola1a.md](13-manifiesto-clickup-ola1a.md)
16. [14-manifiesto-notion-handoff.md](14-manifiesto-notion-handoff.md)
17. [15-auditoria-104-parciales-no-existentes.md](15-auditoria-104-parciales-no-existentes.md)
18. [16-plan-ejecucion-y-cierre.md](16-plan-ejecucion-y-cierre.md)
19. [17-plan-validacion-35-existentes.md](17-plan-validacion-35-existentes.md)

Después deben revisar en Notion la ficha de la ola, módulo y funcionalidad asignada. El código indica lo que existe; Notion indica qué debe construirse y aceptarse en Fase 1.

## Responsables internos

- **Geovany:** desarrollador; asignación detallada por funcionalidad en Notion/ClickUp.
- **Uzziel:** desarrollador; asignación detallada por funcionalidad en Notion/ClickUp.
- **Eliam Cauich y Ángel Sánchez:** planeación, alcance y desbloqueo interno.
- **Jorge Toache (Millet):** coordinación funcional/técnica por parte de Vidrios Millet, especialmente TI y A+W.

El acceso a ClickUp y Notion fue confirmado operativamente por Eliam. El acceso al repositorio debe comprobarse por separado con los usuarios exactos de GitHub de Geovany y Uzziel; no se infieren a partir del correo. Todo cambio debe entrar mediante rama y pull request.

> **Corte 20/09/2026:** GitHub reportó el repositorio como público y `main` sin protección. No se localizaron ramas o PRs de ADM-01/ADM-02. Consultar primero el diagnóstico 18; no usar la descripción histórica de repositorio privado como estado actual.

## Secuencia de habilitación

1. Completar `O0-01` y confirmar la lectura de este handoff.
2. Completar `O0-02` y validar accesos, MFA, identidad y herramientas.
3. Cada desarrollador completa su propio entorno (`O0-03` u `O0-04`).
4. Ambos completan el recorrido técnico (`O0-05`) y la primera PR cruzada (`O0-06`).
5. Eliam valida la evidencia en `O0-07`.
6. Sólo con `O0-07` completada se habilita `O1A-00` y puede iniciar el desarrollo funcional.

- [Lista Ola 0 · Preparación y onboarding](https://app.clickup.com/9017291387/v/l/li/901717118871)
- [Lista Ola 1A · Fundaciones](https://app.clickup.com/9017291387/v/l/li/901717118872)

## Reglas que no se negocian

- No subir secretos, certificados, dumps, datos reales ni credenciales.
- No cambiar alcance a partir del código: ante contradicción, registrar la diferencia.
- No tratar mocks o servicios locales como integración real con A+W, PAC, Entra ID o SAP.
- No cerrar una tarea sin prueba, evidencia y criterio de aceptación.
- No utilizar datos de producción sin autorización y canal controlado.
- No reconstruir portales heredados sin decisión explícita de alcance.
