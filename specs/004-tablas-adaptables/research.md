# Research

- Decision: reutilizar PaginacionTabla y consolidar ocho hooks. Rationale: mismo contrato offset/limit existente. Alternativa descartada: reemplazar tablas por una biblioteca nueva.
- Decision: preferencia local por vista/tabla con lectura validada; URL explícita tiene prioridad. Rationale: recordar sin modificar perfil/API ni compartir datos de negocio. Alternativa: almacenar sólo en el pie no actualiza consultas y se descarta.
- Decision: auto medido y límite explícito. Rationale: aprovechar pantalla sin ampliar ilimitadamente descargas. Alternativa: forzar todas a100 filas o medir sólo viewport con offsets mágicos.
- Decision: listas/paginadores se migran; editores y reportes jerárquicos mantienen filas completas. Rationale: no perder captura ni totales. Se homologan contención y columnas sin cortes semánticos.
- Evidence: inventario estático delegado en Spec Kit: 219 archivos,101 paginadores,96 ListadoModulo,21 tablas nativas,129 sin paginador; siete hooks fijos10 y uno dinámico. La inspección no acredita comportamiento en navegador.
