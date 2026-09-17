# Manifiesto de ejecución ClickUp · Ola 1A

Este manifiesto contiene el cambio exacto preparado para ClickUp. No acredita que las tareas ya existan.

## Destino verificado

- Proyecto: `VIDRIOS MILLET`.
- Carpeta: `Desarrollo e Implementación`.
- Lista: `List` (`901717058214`).
- Tarea coordinadora: `86e390tk3` · Cargar tareas en ClickUp por ola con fechas estimadas.

## Estructura a crear

### Tarea principal

**Nombre:** `Ola 1A · Fundaciones · 102 h operativas`  
**Inicio:** 17 septiembre 2026  
**Fin objetivo:** 7 octubre 2026  
**Estado inicial:** `en progreso`  
**Descripción:** 15 funcionalidades y sus puertas técnicas. La fecha está condicionada a entradas externas; el cierre ocurre por evidencia, no por consumo de horas.

### Tareas de control

| Tarea | Estimación | Responsable | Estado inicial | Cierre |
|---|---:|---|---|---|
| Puerta técnica, repositorio y onboarding de Ola 1A | 18 h | Eliam; Geovany y Uzziel para reproducción | En progreso | Ambos desarrolladores clonan, migran 12 contextos, levantan backend/frontend, inician sesión y ejecutan pruebas desde sus equipos |
| QA-1A · Verificar ADM-01, ADM-03, ADM-10 y ADM-11 | 8 h | Geovany; revisión Uzziel; coordinación Eliam | En espera | Cuatro recorridos ejecutados, evidencia vinculada, regresión terminada y defectos bloqueantes resueltos o escalados |
| Salida Ola 1A · decisión por evidencia | Sin estimación adicional | Eliam | En espera | 15 fichas con resultado técnico; dependencias externas nominales; decisión de avanzar, corregir o mantener bloqueo |

Las 18 h transversales se controlan como una sola bolsa operativa porque la fuente conciliada no asigna un desglose autorizado entre actividades. No se inventará una distribución retrospectiva. El equipo registra tiempo y evidencia contra sus componentes: seguridad/configuración, clonación limpia, migraciones, arranque, pruebas, documentación y onboarding.

## Tareas funcionales

| Orden | ID y tarea | Responsable / revisor | Inicio–fin | Horas funcionales | Arranque | Bloqueo de cierre | Prueba mínima | Notion |
|---:|---|---|---|---:|---|---|---|---|
| 1 | F1-ADM-01 · Administrar empresas, sucursales, departamentos y puestos | Geovany / Uzziel | 21–22 sep | 0 h construcción; QA compartido | Datos ficticios | Estructura vigente de Millet | Crear y consultar dos empresas; comprobar que no se mezclan datos | [Ficha](https://app.notion.com/3dd36ce8974981afbe50cec61dba3d83) |
| 2 | F1-ADM-02 · Administrar usuarios, roles y permisos por empresa y acción | Geovany / Uzziel | 21–24 sep | 12 h | Datos ficticios | Usuarios, roles y Entra ID | Probar acción permitida y denegada por empresa, pantalla y acción | [Ficha](https://app.notion.com/3dd36ce8974981e6a9a0d44d8d4e3ba5) |
| 3 | F1-ADM-12 · Segregar datos y configuración por empresa | Geovany / Uzziel | 21–24 sep | 6 h | Datos ficticios | Configuración definitiva de ambas empresas | Intentar acceso cruzado y comprobar rechazo sin filtración | [Ficha](https://app.notion.com/3dd36ce8974981d99609d1814095c974) |
| 4 | F1-ADM-03 · Consultar bitácora de auditoría | Geovany / Uzziel | 21–22 sep | 0 h construcción; QA compartido | Datos ficticios | Casos críticos y responsables de auditoría | Alta, cambio y autorización dejan antes/después, usuario y fecha | [Ficha](https://app.notion.com/3dd36ce89749818c9905cbf41f263dbc) |
| 5 | F1-ADM-10 · Mostrar navegación según permisos | Geovany / Uzziel | 22–23 sep | 0 h construcción; QA compartido | Datos ficticios | Matriz final de permisos | Menú oculta opción denegada y abre la permitida; API también rechaza | [Ficha](https://app.notion.com/3dd36ce89749815ab8b5c88fea26c2a8) |
| 6 | F1-ADM-04 · Mantener catálogos compartidos | Uzziel / Geovany | 23–29 sep | 4 h | Datos ficticios | Catálogos vigentes de Millet y SAT | Alta, consulta, vigencia y rechazo de duplicado | [Ficha](https://app.notion.com/3dd36ce89749810e8582dec887bc6d42) |
| 7 | F1-ADM-05 · Mantener maestro único de proveedores | Uzziel / Geovany | 23–29 sep | 4 h | Datos ficticios | Maestro vigente y reglas bancarias | Rechazar duplicado y auditar cambio fiscal o bancario | [Ficha](https://app.notion.com/3dd36ce897498123b83bfe5e0651da6d) |
| 8 | F1-ADM-08 · Administrar centros de costo y jerarquía | Uzziel / Geovany | 23–29 sep | 6 h | Datos ficticios | Jerarquía y centros vigentes | Aceptar centro vigente de la empresa y rechazar inactivo/ajeno | [Ficha](https://app.notion.com/3dd36ce8974981a283c6cfe04c5c36c5) |
| 9 | F1-CON-01 · Administrar catálogo de cuentas por empresa | Uzziel / Geovany | 28 sep–2 oct | 8 h | Datos ficticios | Catálogo contable canónico | Validar empresa, naturaleza, nivel, vigencia y cuenta afectable | [Ficha](https://app.notion.com/3dd36ce897498176ade9fcd3cddf50b8) |
| 10 | F1-CON-02 · Administrar dimensiones contables | Uzziel / Geovany | 28 sep–2 oct | 6 h | Datos ficticios | Dimensiones definitivas | Operación acepta sólo dimensión vigente y reportable | [Ficha](https://app.notion.com/3dd36ce897498110a497dc18b4a86ae7) |
| 11 | F1-CON-03 · Abrir, cerrar y reabrir periodos | Uzziel / Geovany | 28 sep–2 oct | 8 h | Datos ficticios | Calendario y autorizadores | Cerrar, impedir movimiento y reabrir sólo con autorización y auditoría | [Ficha](https://app.notion.com/3dd36ce897498183a435e7d318c34f47) |
| 12 | F1-ADM-06 · Sincronizar clientes y condiciones desde A+W | Geovany / Uzziel | 23–29 sep | 8 h | Datos ficticios/doble de prueba | Contrato, tablas, muestras y ambiente A+W | Repetir mensaje sin duplicar cliente; hacer visible un conflicto | [Ficha](https://app.notion.com/3dd36ce89749814ab721fed789a40168) |
| 13 | F1-ADM-07 · Sincronizar productos, claves y unidades con A+W | Geovany / Uzziel | 23–29 sep | 8 h | Datos ficticios/doble de prueba | Productos, unidades, claves fiscales y contrato A+W | Correspondencia única A+W–ERP–SAT; baja controlada | [Ficha](https://app.notion.com/3dd36ce897498140bd5affe594463f67) |
| 14 | F1-ADM-09 · Configurar PAC, certificados, series y parámetros | Uzziel / Geovany | 28 sep–2 oct | 6 h | Sandbox | PAC, series, certificados y parámetros por canal seguro | Empresa usa sólo su configuración; ningún secreto aparece en log o evidencia | [Ficha](https://app.notion.com/3dd36ce89749810f9840f57dbdbe7713) |
| 15 | F1-ADM-11 · Adjuntar documentos con control de acceso | Geovany / Uzziel | 23–24 sep | 0 h construcción; QA compartido | Requiere decisión interna | Definir módulos y registros cubiertos | Adjuntar/consultar en cobertura acordada y denegar a usuario sin permiso | [Ficha](https://app.notion.com/3dd36ce897498196a984db3da5748f35) |

## Descripción común para cada tarea

Cada tarea debe contener:

1. **Objetivo:** entregar exactamente la función indicada, sin ampliar el alcance por falta de insumo.
2. **Estado de arranque:** usar el indicado en la tabla. Un insumo de Millet bloquea la variante real o el cierre, no todo el trabajo local.
3. **Criterio de aceptación:** copiar el criterio de la ficha de Notion.
4. **Pruebas:** ejecutar el caso nominal, una negativa de permisos/validación y la regresión de consumidores afectados.
5. **Evidencia:** PR/commit, archivos modificados, migración o configuración, dato anonimizado, pasos, resultado esperado/obtenido, captura o log, pruebas y revisor.
6. **Cierre de desarrollo:** código revisado y pruebas aprobadas. No equivale a UAT ni producción.
7. **Cierre funcional:** responsable principal de Millet o delegado confirmado acepta el caso y queda registrado en Evidencias.

## Dependencias en ClickUp

- Puerta técnica bloquea el inicio formal de las 15 tareas, aunque el análisis pueda ocurrir en paralelo.
- ADM-01, ADM-02 y ADM-12 preceden a maestros, configuración y contabilidad.
- ADM-04, ADM-05 y ADM-08 preceden a los módulos consumidores.
- CON-01 y CON-02 preceden a CON-03 y a la generación de pólizas de olas posteriores.
- ADM-06 y ADM-07 pueden desarrollarse con dobles; su validación real espera A+W.
- ADM-09 puede desarrollarse en sandbox; su cierre real espera parámetros y certificados entregados por canal seguro.
- Las quince tareas y QA-1A bloquean el hito de salida de Ola 1A.

## Control de integridad

- Horas funcionales: 76 h.
- Trabajo transversal: 18 h.
- QA/regresión: 8 h.
- Total operativo: 102 h.
- Responsables: Geovany 8; Uzziel 7.
- Estados de arranque: 13 datos ficticios; 1 sandbox; 1 decisión interna.
- Las 5 h adicionales de QA se toman de la reserva interna y no aumentan las 840 h del alcance total.

## Datos que aún faltan para ejecutar la publicación

- IDs de miembro de ClickUp de Geovany y Uzziel, o confirmación de que deben crearse sin asignado hasta incorporarlos al espacio.
- Autorización expresa sobre este manifiesto y el borrador de Notion.
