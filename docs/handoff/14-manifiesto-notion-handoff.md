# Manifiesto de actualización Notion · Handoff Fase 1

Este documento define el cambio exacto preparado para Notion. No acredita que la publicación ya se haya ejecutado.

## Destino verificado

- Workspace: `Búho Solutions & Vilo Studio`.
- Proyecto: `VIDRIOS MILLET`.
- Página raíz: [Desarrollo Fase 1 · Base universal de ejecución](https://app.notion.com/p/3dd36ce8974981c49dbaff9768d8e8a7).
- Base de olas: `collection://90956ba1-4b31-4bd6-b168-8d0998be4b66`.
- Base de funcionalidades: `collection://5f2045f4-08b3-41d8-ab21-907961a9a664`.
- Base de controles transversales: `collection://e9dc9264-a0dd-46a2-a6ed-68c7d72efc8e`.

## Regla de publicación

1. Conservar las fichas existentes y sus relaciones.
2. No borrar páginas para corregir estructura; renombrar, mover o crear vistas primero.
3. No mezclar la solicitud Word para Millet con el handoff interno.
4. No marcar `Aceptada`, `Cerrada` o `Con evidencia` sin la evidencia específica de esa ficha.
5. Releer después de cada escritura para comprobar título, propiedades, relaciones y contenido.

## Página raíz

### Propiedad

- `Status`: de `Not started` a `In progress`.

### Sustituciones de contenido

Reemplazar el límite histórico:

> Esta publicación documenta la ejecución; no acredita repositorio remoto publicado...

por:

> El repositorio privado y la línea base técnica ya están publicados. Esto no acredita acceso de Geovany y Uzziel, ambientes Millet reactivados, integraciones reales, UAT ni producción. Cada estado cambia únicamente con evidencia vinculada.

Agregar un bloque `Estado técnico del handoff`:

- Repositorio: `https://github.com/Eliam2102/millet-erp`.
- Línea base congelada: etiqueta `handoff-fase1-v1.2`, commit `41b235fc02a290627ea842be24c8a01cc8a36241`.
- Planeación ejecutable posterior: commit `dabb2d7` o el commit vigente de `main` al momento de publicar.
- Pruebas verificadas: backend 2,768; frontend 1,567; build de producción aprobado.
- Base limpia: 12 contextos migrados; health live/ready 200; login local comprobado.
- Pendiente: acceso y repetición independiente por Geovany y Uzziel.
- Limitación: GitHub no permite proteger `main` en este repositorio privado con el plan actual; revisión por PR es control de proceso hasta habilitar un plan compatible.

## Navegación profesional

Mantener en la portada sólo accesos de primer nivel:

1. `Comienza aquí`.
2. `Onboarding técnico y primer día`.
3. `Arquitectura y mapa del código`.
4. `Desarrollo por olas`.
5. `Módulos y funcionalidades`.
6. `Dependencias e insumos del cliente`.
7. `Integraciones`.
8. `Migración`.
9. `QA, regresión y UAT`.
10. `Evidencias y definición de terminado`.
11. `Bloqueos y escalamiento`.

Las bases completas permanecen como catálogos; las guías anteriores deben aparecer mediante vistas o páginas anidadas, no duplicadas como bloques sueltos.

## Olas

### Registro actual `Ola 0`

- Renombrar a `Puerta técnica de Ola 1A`.
- Mantener orden 0 para no romper relaciones.
- Objetivo: repositorio, seguridad, base local, migraciones, arranque, pruebas y onboarding reproducibles.
- Entrada: repositorio privado y herramientas locales disponibles.
- Salida: ambos devs clonan, migran, levantan, inician sesión y ejecutan pruebas sin asistencia.
- Estado: `En curso`.
- Aclaración dentro de la ficha: no es una ola funcional ni un entregable independiente al cliente.

### `Ola 1A`

- Estado: `En curso`.
- Objetivo: fundaciones multiempresa, identidad/permisos, maestros, configuración fiscal inicial, catálogo contable, dimensiones y periodos.
- Entrada: puerta técnica utilizable; datos ficticios o sandbox preparados; dependencias externas nominales.
- Salida: 15 fichas con resultado técnico, cuatro existentes con evidencia/regresión y bloqueos de cierre explícitos.
- Horas operativas: 102 h, documentadas dentro de la ficha.

### `Ola 1E`

Crear un nuevo registro:

- Nombre: `Ola 1E · Migración final, UAT y salida`.
- Orden: 6.
- Estado: `Por iniciar`.
- Objetivo: migración final, UAT integrada, conciliación SAP–ERP–A+W, go/no-go, reversión, corte y estabilización.
- Entrada: 1A–1D terminadas por desarrollo, ensayos de migración conciliados, ambiente y usuarios disponibles.
- Salida: UAT registrada; carga final conciliada; autorización de corte; reversión ejecutable; incidencias iniciales con dueño y fecha.

## Propiedades nuevas de Funcionalidades · Fase 1

Agregar únicamente si no existen:

| Propiedad | Tipo | Uso |
|---|---|---|
| Estado de arranque | Select | Lista para comenzar; Datos ficticios; Sandbox; Bloqueada por Millet; Bloqueada por repositorio; Decisión interna |
| Bloqueo de cierre | Texto | Insumo, decisión o evidencia que impide cerrar la ficha |
| Inicio objetivo | Fecha | Fecha operativa de inicio |
| Fin objetivo | Fecha | Fecha operativa de fin |
| Estado técnico auditado | Select | Verificable local; Parcial/condicionada; No localizada; Construcción pendiente |
| Línea base | Texto | Commit/tag contra el que se ejecuta la ficha |

No reemplazar `Estado` ni `Base existente`: las nuevas propiedades distinguen planeación, evidencia técnica y avance.

## Actualización de las 15 funcionalidades de Ola 1A

Usar [13-manifiesto-clickup-ola1a.md](13-manifiesto-clickup-ola1a.md) como tabla de responsables, fechas, prueba y bloqueo. Aplicar además:

- `Estado = Lista para iniciar`: ADM-01, ADM-02, ADM-03, ADM-04, ADM-05, ADM-06, ADM-07, ADM-08, ADM-09, ADM-10, ADM-12, CON-01, CON-02 y CON-03.
- `Estado = Bloqueada`: ADM-11 solamente.
- En ADM-11, cambiar `Base existente` de `Ya existe y funciona` a `Existe a medias`.
- En ADM-11, registrar: `Bloqueo interno: definir módulos y tipos de registro cubiertos; no depende de una nueva definición funcional del cliente`.
- `Línea base = handoff-fase1-v1.2 · 41b235f` en las 15 fichas.

### Estado técnico de las cuatro marcadas existentes en 1A

| ID | Estado técnico auditado |
|---|---|
| F1-ADM-01 | Verificable local; requiere caso con evidencia y UAT |
| F1-ADM-03 | Verificable local; requiere caso con evidencia y UAT |
| F1-ADM-10 | Verificable local; requiere caso con evidencia y UAT |
| F1-ADM-11 | Parcial/condicionada; corregir el estado heredado |

No cambiar ninguna a `Aceptada` hasta registrar el caso UAT.

## Onboarding técnico y primer día

Página existente: [Onboarding técnico y primer día](https://app.notion.com/p/3dd36ce8974981e48c26d9c26d41dfac).

Sustituir:

- repositorio histórico `Millet-TI/millet_erp` por `Eliam2102/millet-erp`;
- `no tiene remoto configurado` por el remoto privado comprobado;
- commit local histórico por la etiqueta `handoff-fase1-v1.2` y commit `41b235f`;
- instrucción genérica de migración por el orden documentado en `docs/handoff/01-arranque-local.md`.

Agregar:

- el gate completo sólo debe correr contra base exclusiva, con 12 migraciones y pruebas seriales;
- los despliegues Azure son manuales y requieren autorización;
- la revisión por PR es obligatoria por proceso aunque la protección de rama no esté técnicamente disponible;
- npm mantiene 6 hallazgos pendientes; no usar `npm audit fix --force` sin evaluación;
- checklist individual para Geovany y Uzziel con acceso, MFA, clonación, migraciones, backend, frontend, login, pruebas y primer PR revisado.

## Controles transversales

### Vistas

- `Frentes`: filtro `Tipo = Frente`; muestra TR-01 a TR-10.
- `Actividades ejecutables`: filtro `Tipo = Actividad`; muestra V3-*.
- `Ola 1A`: actividades relacionadas con puerta técnica, arquitectura, onboarding y QA-1A.

### Cambios de estado

Pasar a `En curso`, no a `Con evidencia`:

- V3-ARCH-01 · repositorio, ramas y CI/CD.
- V3-ARCH-02 · ambientes, configuración y secretos.
- V3-ARCH-03 · migraciones, respaldo y restauración.
- V3-ARCH-06 · seguridad y dependencias.
- V3-ARCH-07 · onboarding y runbook.
- V3-QA-1A · cuatro funciones existentes.

Razones para no cerrarlas todavía:

- ARCH-01/07: falta repetición independiente de ambos desarrolladores.
- ARCH-02: falta inventario y ambiente real de Millet.
- ARCH-03: migración limpia está comprobada; respaldo/restauración completa queda por comprobar.
- ARCH-06: permanecen 6 hallazgos npm y la limitación de protección de rama.
- QA-1A: auditoría técnica hecha, pero falta evidencia por caso, regresión y UAT.

Actualizar `V3-QA-1A` de 3 h a 8 h. Las 5 h adicionales salen de la reserva interna; no aumentan el techo de 840 h.

## Arquitectura de páginas anidadas

Crear o conservar una página por tema; dentro de cada una usar vistas enlazadas, no copias de registros:

### Comienza aquí

- Objetivo del proyecto.
- Fuente de verdad y precedencia documental.
- Qué hacer el primer día.
- Línea base y repositorio.
- Ruta a Ola 1A.

### Arquitectura y mapa del código

- Stack verificado.
- Estructura backend/frontend.
- 12 DbContexts y migraciones.
- Servicios compartidos.
- Integraciones y límites de evidencia.

### Desarrollo por olas

- Vista de olas.
- Criterios de entrada/salida.
- Calendario y dependencias.
- Vista filtrada de funcionalidades por ola.

### Dependencias e insumos del cliente

- Directorio confirmado.
- Insumos solicitados.
- Estado recibido/incompleto/validado.
- Regla: Millet entrega lo que ya opera; VILO sólo propone cuando no existe una práctica actual.

### QA, regresión y UAT

- Auditoría de las 35 existentes.
- Casos por ola.
- Regresión.
- Usuarios de UAT.
- Defecto, corrección, repetición y aceptación.

### Bloqueos y escalamiento

- Código/repositorio: Eliam o responsable técnico VILO.
- Reglas funcionales: responsable principal del área de Millet.
- Ambientes/A+W/accesos: Jorge Toache.
- Contabilidad/SAP: Laura Cerón.
- Fiscal/crédito/cobranza: Sorandi Martínez.
- Tesorería: Gerardo López.
- Alcance/decisión interna: Eliam y dirección VILO.

## Verificación posterior obligatoria

Después de publicar:

1. Releer la página raíz y comprobar que no haya bloques duplicados.
2. Consultar las olas y confirmar nombres, orden y estados.
3. Consultar las 15 fichas y comprobar responsables 8/7, estados 14/1 y fechas.
4. Confirmar ADM-11 como parcial y bloqueada por decisión interna.
5. Confirmar V3-QA-1A en 8 h y `En curso`.
6. Abrir el onboarding y comprobar que ya no apunte a `Millet-TI/millet_erp` ni diga que no existe remoto.
7. Confirmar que ninguna ficha quedó `Aceptada`, `Cerrada` o `Con evidencia` por esta publicación.

## Autorización requerida

La publicación externa se ejecuta únicamente después de aprobación expresa de este manifiesto y de [12-borrador-publicacion-notion-clickup.md](12-borrador-publicacion-notion-clickup.md).
