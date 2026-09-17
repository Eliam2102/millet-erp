# Matriz de arranque congelada · Ola 1A

## Regla de estado

- **Puede comenzar con datos ficticios/sandbox:** el desarrollador puede trabajar hoy; el insumo de Millet bloquea el cierre real, no el inicio.
- **Requiere decisión interna:** VILO debe acotar la implementación antes de asignarla.
- **Bloqueada por Millet:** sólo se usaría cuando no existe trabajo local útil. En este corte no aplica a ninguna ficha completa de 1A; sí existen variantes reales bloqueadas.
- **Bloqueada por repositorio:** no aplica; el repositorio privado fue publicado, clonado y probado.

## Orden y estado de las 15 funcionalidades

| Orden | ID | Responsable | Arranque | Bloqueo de cierre | Prueba mínima | Evidencia de cierre |
|---:|---|---|---|---|---|---|
| 1 | F1-ADM-01 | Geovany | Datos ficticios | Estructura vigente de Millet | Crear/consultar dos empresas y probar no mezcla | PR, captura, dato usado y resultado negativo |
| 2 | F1-ADM-02 | Geovany | Datos ficticios | Usuarios, roles y Entra ID | Rol permitido/denegado por empresa y acción | Matriz usada, pruebas API/UI y capturas |
| 3 | F1-ADM-12 | Geovany | Datos ficticios | Configuración definitiva de ambas empresas | Intento de acceso cruzado rechazado | Log/captura y prueba automatizada |
| 4 | F1-ADM-03 | Geovany | Datos ficticios | Casos críticos de auditoría | Alta/cambio/autorización dejan antes/después, usuario y fecha | Consulta de bitácora y evidencia del evento |
| 5 | F1-ADM-10 | Geovany | Datos ficticios | Matriz final de permisos | Menú oculta opción denegada y abre permitida | Capturas de ambos roles y prueba de navegación |
| 6 | F1-ADM-04 | Uzziel | Datos ficticios | Catálogos vigentes | Alta/consulta/vigencia y rechazo de duplicado | Datos, pruebas y pantalla |
| 7 | F1-ADM-05 | Uzziel | Datos ficticios | Maestro de proveedores y reglas bancarias | Identidad única y cambio bancario auditado | PR, prueba de duplicado y auditoría |
| 8 | F1-ADM-08 | Uzziel | Datos ficticios | Jerarquía/centros vigentes | Centro de costo vigente por empresa; inactivo rechazado | Pruebas y reporte de jerarquía |
| 9 | F1-CON-01 | Uzziel | Datos ficticios | Catálogo contable canónico | Cuenta por empresa, naturaleza y vigencia | Migración, API/UI y prueba de segregación |
| 10 | F1-CON-02 | Uzziel | Datos ficticios | Dimensiones definitivas | Póliza/operación acepta sólo dimensión vigente | Migración, prueba y captura |
| 11 | F1-CON-03 | Uzziel | Datos ficticios | Calendario y autorizadores | Cerrar, impedir movimiento y reabrir autorizado | Pruebas de transición y auditoría |
| 12 | F1-ADM-06 | Geovany | Datos ficticios | Contrato/muestras/ambiente A+W | Mensaje repetido no duplica cliente; conflicto visible | Contrato versionado, logs y prueba idempotente |
| 13 | F1-ADM-07 | Geovany | Datos ficticios | Productos/unidades/claves A+W | Correspondencia única A+W–ERP–SAT y baja controlada | Mapeo, pruebas y reporte de conflictos |
| 14 | F1-ADM-09 | Uzziel | Sandbox | PAC, series y certificados seguros | Empresa usa sólo su configuración; secreto no se expone | Prueba sandbox, log redactado y checklist |
| 15 | F1-ADM-11 | Geovany | Decisión interna | Cobertura de módulos/registros | Adjuntar/consultar y denegar a usuario sin permiso | Matriz de cobertura, prueba por módulo y evidencia |

## Puertas técnicas

1. Organización/identidad/segregación antes de maestros y contabilidad.
2. Catálogos/proveedores/centros antes de consumidores.
3. Catálogo contable y periodos antes de pólizas de olas posteriores.
4. Contratos A+W pueden trabajarse con dobles de prueba, pero no cerrarse sin evidencia de Millet.
5. PAC puede trabajarse en sandbox, nunca con certificados enviados por correo o guardados en Git.
6. Adjuntos no se asigna como “universal” hasta definir qué módulos y permisos cubre.

## Horas de Ola 1A

- Funcionales base: 76 h.
- Transversales asociadas: 18 h.
- QA/regresión de cuatro existentes: 8 h.
- Total operativo: 102 h.
- Diferencia contra el borrador anterior: +5 h tomadas de la reserva interna, sin alterar el techo total de 840 h.

El cierre ocurre por evidencia y criterios, no porque se hayan consumido 102 horas.
