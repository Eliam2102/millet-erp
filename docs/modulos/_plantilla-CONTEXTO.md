# CONTEXTO · <Módulo> (<código vault, p. ej. MOD-COM>)

Paquete de contexto para desarrollo. No reemplaza el levantamiento ni el diseño: los resume y apunta a ellos.
Última actualización: <fecha> · Tarea que lo actualizó: <ID>

**Convención de certeza.** Cada dato lleva una etiqueta:
- `[CONF · fuente]` confirmado por Millet o por la base de conocimiento (cita archivo, pregunta/página o nota SRC y fecha).
- `[MANUAL · archivo]` lo dice un manual del ERP escrito por VILO (p. ej. `manual-*.pdf`); NO es respuesta de Millet y no cuenta como CONF.
- `[DISEÑO · doc]` decidido por nosotros en diseño/ADR, sin respuesta de Millet que lo contradiga.
- `[SUPUESTO]` tomado a falta de respuesta; indica la alternativa y a quién se le pregunta.
- `[ABIERTO]` sin respuesta; no desarrollar sobre esto sin decisión del owner.
- `[CÓDIGO]` verificado en el repo (ruta).
Cita de preguntas: `archivo · P<número de pregunta>` (nunca páginas o párrafos mezclados). Nada se escribe sin etiqueta. Lo que no se pudo validar va a ABIERTO, nunca se inventa.

## 1. Qué es y qué no es
(3 a 6 líneas. Para qué sirve, quién lo usa, dónde empieza y qué entrega al siguiente proceso. Límites explícitos con módulos vecinos.)

## 2. Documentos a leer (en orden)
| Orden | Ruta | Para qué |
|---|---|---|
(repo `docs/modulos/<modulo>/…`, ADR aplicables, notas del vault `MOD-xx`, recorridos, manual operativo PDF de la base de conocimiento)

## 3. Fichas y estado
Estado verificado en: <rama> @ <commit> el <fecha> (indicar si hay working tree sin commit).

| Ficha | Resultado | Estado real (construido / parcial / pendiente) | PR o rama | Brechas abiertas |
|---|---|---|---|---|
(Cruzar con `.local-context/brechas-abiertas.md` y el código; el estado se verifica en el repo, no se copia del plan.)

## 4. Reglas de negocio confirmadas
| # | Regla | Etiqueta y fuente |
|---|---|---|
(Reglas exactas: umbrales, tolerancias, folios, estados y transiciones, quién autoriza, qué es excepción. Con el dato preciso, no la paráfrasis.)

## 5. Datos y entidades
(Entidades y agregados con campos clave, catálogos, estados y sus transiciones, folios/numeración, monedas e impuestos, valores de ejemplo reales de la base de conocimiento.)

## 6. Contratos
(Tope ~40 líneas: lo canónico y lo no obvio; el resto se enlaza a `rg`/ADR.)

- **Eventos que publica / consume** (nombre canónico, payload clave, idempotencia).
- **Puertos de lectura** (`I*ReadPort`) que expone / usa.
- **Permisos canónicos** del módulo y alcance por sucursal (ADR-0051).
- **Endpoints principales** (`/api/v1/...`).

## 7. Código: dónde vive y trampas
| Qué | Ruta |
|---|---|
(Application / Domain / Infrastructure / frontend / tests / migraciones. Patrón exemplar a imitar. Trampas conocidas: cosas que ya fallaron o que sorprenden.)

## 8. Pruebas
Roles y usuarios sembrados que sirven, datos semilla disponibles, escenarios de prueba manual típicos del módulo, y qué specs e2e / integración ya existen (rutas).

## 9. Abiertos, supuestos y contradicciones
### Contradicciones
| # | Fuente A | Fuente B | Manda (o pendiente de decisión del owner) |
|---|---|---|---|

### Abiertos y supuestos
| # | Pregunta o supuesto | A quién | Mientras tanto | Etiqueta |
|---|---|---|---|---|

## 10. Aprendizajes por tarea
(Una línea por tarea cerrada: fecha · ID · qué se aprendió que no estaba escrito arriba. Esta sección la alimenta el cierre de cada `/develop-task`.)
