# Reglas: regla → prueba → cláusula

Cada regla del dominio dice de qué cláusula de las [condiciones generales](condiciones/condiciones-generales.md) sale
y qué la prueba. La compuerta real es esta tabla, no el porcentaje de cobertura: una regla sin prueba no está hecha.

Pruebas: `D` = `tests/Perito.Domain.Tests` · `A` = `tests/Perito.Application.Tests` (almacén en memoria con copias
por sesión) · `I` = `tests/Perito.Infrastructure.Tests` (PostgreSQL real) · `W` = `tests/Perito.Api.Tests` (API
real sobre PostgreSQL) · `S` = `tests/Perito.Asistente.Tests` · `R` = `tests/Perito.Architecture.Tests`.

## Póliza y cálculo

| Id | Regla | Prueba(s) | Cláusula |
|---|---|---|---|
| R01 | Vigencia: la ocurrencia cae entre inicio y fin, ambos inclusive | D `PolizaYCalculoTests.La_vigencia_incluye_sus_dos_extremos` (4 casos) | GEN-2 |
| R02 | Cancelación: desde su fecha no ampara; el día anterior sí | D `Lo_ocurrido_desde_la_cancelacion_no_se_ampara_y_lo_anterior_si` · `DictamenPagoYRechazoTests.Una_poliza_cancelada_antes_de_la_ocurrencia_se_dictamina_con_GEN_2` | GEN-2 |
| R03 | Periodo de gracia de 30 días naturales desde el día siguiente al pagado: el día 30 ampara, el 31 no | D `El_dia_treinta_del_periodo_de_gracia_ampara_y_el_treinta_y_uno_no` | GEN-3 |
| R04 | Cobertura no contratada ⇒ no elegible (y la fecha manda sobre la cobertura en el motivo) | D `Una_cobertura_que_no_esta_en_la_caratula_no_es_elegible_y_cita_GEN_1` · `La_fecha_manda_sobre_la_cobertura_en_el_motivo` · `Una_cobertura_no_contratada_se_tria_como_no_elegible` | GEN-1 |
| R05 | Carátula válida: un solo ramo, sin coberturas repetidas, suma positiva, deducible válido, coaseguro ≤ 50 % | D `Una_caratula_invalida_no_se_emite` (6 casos) · A `Un_folio_no_se_emite_dos_veces_ni_sin_titular` · I `El_folio_es_unico_en_la_base_aunque_dos_altas_pasen_la_revision_a_la_vez` | GEN-1, GEN-5 |
| R06 | Deducible fijo, % de la suma o % de la pérdida | D `El_deducible_se_calcula_segun_su_tipo` (3 casos) | GEN-6 |
| R07 | Indemnización = (pérdida − deducible) × (1 − coaseguro), tope en la suma; al centavo, la mitad hacia arriba | D `CondicionesGeneralesTests.Los_ejemplos_de_GEN_7_se_cumplen` (los 6 ejemplos LEÍDOS de la tabla del documento: el oráculo es el texto, no una copia de la fórmula) · `El_centavo_se_redondea_la_mitad_hacia_arriba_no_al_par` · `Una_perdida_igual_al_deducible_no_se_indemniza` | GEN-5, GEN-6, GEN-7 |

## Siniestro: aviso, triaje, asignación, reserva

| Id | Regla | Prueba(s) | Cláusula |
|---|---|---|---|
| R08 | La ocurrencia no es posterior al aviso; el día del aviso es el de la Ciudad de México | D `El_aviso_no_acepta_una_ocurrencia_posterior_al_aviso` · A `El_dia_del_aviso_es_el_del_calendario_local_no_el_de_UTC` | GEN-4 |
| R09 | Minimización del aviso: descripción de 20 a 4000 caracteres; lesiones sólo si las hay | D `El_aviso_exige_una_descripcion_de_al_menos_veinte_caracteres` (2 casos) | GEN-12 |
| R10 | El triaje lo hace una persona con un tipo de SU ramo; la cobertura sale del catálogo | D `El_triaje_deriva_la_cobertura_del_catalogo_y_no_de_quien_tria` · `Un_tipo_de_otro_ramo_no_se_acepta_en_el_triaje` | AUT-2.x, HOG-2.x |
| R11 | Indicio: ocurrencia en los primeros 15 días de vigencia (día 14 sí, 15 no) | D `El_siniestro_en_los_primeros_quince_dias_de_vigencia_es_indicio` | GEN-9 |
| R12 | Indicio: aviso después de 5 días naturales (5 en plazo, 6 tardío) | D `El_aviso_despues_de_cinco_dias_naturales_es_indicio` | GEN-4, GEN-9 |
| R13 | Indicio: 2 o más siniestros de la póliza en los 365 días anteriores (el 366 no cuenta) | D `Dos_siniestros_previos_en_doce_meses_son_indicio` · A `La_frecuencia_cuenta_los_siniestros_de_los_doce_meses_anteriores` · I `La_ventana_de_frecuencia_se_traduce_a_SQL_con_sus_extremos` | GEN-9 |
| R14 | Indicio: estimación ≥ 90 % de la suma asegurada | D `Una_estimacion_del_noventa_por_ciento_de_la_suma_es_indicio` | GEN-9 |
| R15 | Dos indicios o más ⇒ investigación especial y sólo un investigador recibe el caso | D `Dos_indicios_obligan_a_investigacion_y_uno_no` · `Con_investigacion_solo_un_investigador_puede_recibir_el_caso` | GEN-9 |
| R16 | Asignación: ajustador activo, de la especialidad del ramo, con menos de 10 casos abiertos (contados en la base) | D `La_asignacion_exige_ajustador_activo_de_la_especialidad_del_ramo` · `Un_ajustador_lleva_a_lo_mas_diez_casos_abiertos` · A `La_carga_del_ajustador_cuenta_sus_casos_abiertos_en_la_base` | interna |
| R17 | Sólo se registran los datos que pide el tipo; repetir no cambia nada | D `Solo_se_registran_los_datos_que_pide_el_tipo_y_repetirlos_no_cambia_nada` | GEN-8 |
| R18 | Reserva = cálculo de GEN-7 sobre la estimación, con tope en la suma; no elegible o bajo el deducible ⇒ sin reserva | D `La_reserva_es_la_perdida_estimada_neta_de_deducible` · `La_reserva_inicial_no_excede_la_suma_asegurada` · `Un_siniestro_no_elegible_no_se_reserva` · `Una_estimacion_que_no_supera_el_deducible_no_se_reserva` | GEN-5, GEN-7 |
| R19 | Ajuste de reserva: saldo de 0.01 a la suma, con motivo, distinto del actual; queda el delta | D `Cada_ajuste_de_reserva_lleva_motivo_tope_y_deja_su_delta` | interna |

## Dictamen, pago y rechazo

| Id | Regla | Prueba(s) | Cláusula |
|---|---|---|---|
| R20 | Dictamen determinista: elegibilidad → fraude comprobado → exclusiones → cálculo | D `Un_siniestro_elegible_con_documentos_y_sin_exclusiones_es_procedente_por_el_calculo` · `Un_siniestro_no_elegible_se_dictamina_improcedente_sin_reserva_ni_documentos` (3 casos) | GEN-1/2/3/7 |
| R21 | Sin la documentación del tipo no se dictamina un siniestro elegible | D `Sin_la_documentacion_del_tipo_no_se_dictamina` | GEN-8 |
| R22 | Una exclusión confirmada que aplica a la cobertura ⇒ improcedente con su cláusula; una que no aplica es un error | D `Una_exclusion_confirmada_que_aplica_hace_improcedente_y_cita_su_clausula` · `Una_exclusion_que_no_aplica_a_la_cobertura_se_rechaza_como_dato` | AUT-4.x, HOG-4.x |
| R23 | Con indicios no se dictamina sin el resultado de la investigación; fraude comprobado ⇒ improcedente | D `Con_indicios_no_se_dictamina_sin_el_resultado_de_la_investigacion_y_el_fraude_comprobado_es_GEN_9` | GEN-9 |
| R24 | Pérdida que no supera el deducible ⇒ improcedente, aun sin reserva | D `Una_perdida_que_no_supera_el_deducible_es_improcedente_aun_sin_reserva` | GEN-6 |
| R25 | Procedente exige reserva constituida y suficiente (si no, primero se ajusta) | D `Un_dictamen_procedente_necesita_reserva_constituida` · `Si_la_indemnizacion_excede_la_reserva_primero_se_ajusta` | interna |
| R26 | Separación de funciones: quien dictamina no paga | D `Quien_dictamina_no_puede_pagar_el_mismo_siniestro` · W `El_flujo_completo_por_HTTP_respeta_roles_y_la_separacion_de_funciones` | interna |
| R27 | Plazo de pago: 30 días naturales desde el dictamen; después se paga marcado | D `Pagar_despues_de_treinta_dias_del_dictamen_queda_marcado_fuera_de_plazo` (30 y 31) | GEN-10 |
| R28 | Pagar o rechazar libera toda la reserva y el evento lo dice | D `El_pago_libera_toda_la_reserva_y_su_evento_lo_dice` · `Un_dictamen_improcedente_no_se_paga_se_rechaza_con_su_clausula_y_libera_la_reserva` | interna |
| R29 | El rechazo sólo procede de un dictamen improcedente y lleva su cláusula | D `Un_dictamen_improcedente_no_se_paga_se_rechaza_con_su_clausula_y_libera_la_reserva` · `Un_dictamen_procedente_no_se_rechaza` | GEN-11 |
| — | Transiciones inválidas: las 47 combinaciones estado × acción no permitidas fallan con `transicion_invalida` y no cambian estado ni eventos | D `TransicionesInvalidasTests.Una_accion_fuera_de_su_estado_falla_y_no_cambia_nada` (47) · `La_matriz_cubre_las_cincuenta_y_seis_combinaciones` · W `El_contrato_de_errores_es_problem_json_con_codigo_y_sin_detalles_internos` | máquina de estados |

## El asistente propone y no decide

| Id | Regla | Prueba(s) | Cláusula |
|---|---|---|---|
| R30 | Ninguna propuesta cambia el siniestro ni emite eventos (6 propuestas hostiles) | A `ProponeYNoDecideTests.Ninguna_propuesta_cambia_el_siniestro_ni_emite_eventos` · I `La_propuesta_se_guarda_como_documento_y_no_toca_el_siniestro_ni_la_bandeja` (xmin y bandeja intactos) · W `Pedir_una_propuesta_no_cambia_el_estado_del_siniestro` | GEN-12 |
| R31 | Aceptar una propuesta pasa por las reglas: el dominio calcula cobertura, elegibilidad y dictamen | A `Aceptar_una_propuesta_pasa_por_las_reglas_y_el_dictamen_es_del_dominio` · D `Aceptar_una_propuesta_registra_cual_fue_pero_no_cambia_el_resultado_del_triaje` | GEN-12 |
| R32 | Sólo una función de lectura, sin parámetros y acotada al siniestro; cualquier otra se rechaza y se anota | S `Una_funcion_fuera_de_la_lista_blanca_no_se_ejecuta_y_queda_anotada` · `El_kernel_solo_expone_la_funcion_de_lectura_de_la_poliza` · R `Las_unicas_funciones_del_kernel_son_las_de_la_lista_blanca` | GEN-12 |
| R33 | En el IL, el asistente no crea comandos, no usa la unidad de trabajo ni cambia agregados | R `El_asistente_no_envia_comandos_ni_cambia_agregados` (control: inyectar un comando la hace fallar) | GEN-12 |
| R34 | La cita sólo sobrevive si está entre las cláusulas recuperadas y ampara el tipo; tipo, datos y exclusiones sólo del catálogo del ramo | A `ValidadorDePropuestaTests` (5 pruebas, 8 casos) | — |
| R35 | Tope de rondas de funciones y de tiempo; una respuesta ilegible queda anotada y no inventa campos | S `Un_modelo_que_solo_pide_funciones_se_corta_en_las_rondas_permitidas` · `Una_respuesta_ilegible_queda_anotada_y_no_inventa_campos` · `La_respuesta_se_lee_aunque_traiga_texto_alrededor_y_campos_con_otro_tipo` | — |
| R36 | Las propuestas tienen límite por usuario y minuto | W `Las_propuestas_tienen_limite_por_usuario_y_minuto` | — |

## Privacidad

| Id | Regla | Prueba(s) | Cláusula |
|---|---|---|---|
| R37 | Datos del titular mínimos y validados (RFC, correo, teléfono de 10 dígitos, CLABE con dígito de control) | D `Cada_dato_del_titular_se_valida` (5) · `La_clabe_se_valida_con_su_digito_de_control` (5) | GEN-12 |
| R38 | Ningún dato personal llega a un prompt: identificadores conocidos del titular, patrones y nombres con señal se seudonimizan; el detalle de lesiones nunca se envía | D `PrivacidadTests` (seudonimizador, 19 casos) · A `El_texto_llega_seudonimizado_y_el_detalle_de_lesiones_no_llega` · S `Ningun_prompt_de_ningun_caso_lleva_un_dato_personal_conocido` (50 casos × 3 contextos) · R `Solo_el_seudonimizador_crea_el_texto_que_recibe_el_asistente` | GEN-12 |
| R39 | La función de la póliza no devuelve datos personales ni el folio | S `La_funcion_de_la_poliza_se_ejecuta_via_Application_y_no_trae_datos_personales` | GEN-12 |
| R40 | Cifrado de campo AES-256-GCM: sin texto claro en la base, vínculo al campo, integridad y rotación | I `Los_identificadores_y_el_texto_libre_se_guardan_cifrados_sin_texto_claro_en_la_base` · `CifradoTests` (7) | GEN-12 |
| R41 | Toda lectura de datos personales declara su finalidad y queda registrada antes de devolverlos (sin los datos) | A `Consultar_un_siniestro_exige_una_finalidad_permitida_y_sin_ella_no_registra_ni_devuelve` · `Cada_consulta_de_datos_personales_queda_registrada_con_quien_que_de_quien_y_para_que` · I `Consultar_un_siniestro_deja_su_registro_de_acceso_sin_datos_personales` · W `Leer_datos_personales_exige_finalidad_y_queda_en_el_registro_del_titular` | GEN-12 |
| R42 | Anonimización a solicitud: no procede con siniestros abiertos; borra identificadores y texto libre, conserva montos y su fundamento, y no deja rastro en ninguna tabla | D `Con_siniestros_abiertos_no_se_anonimiza` · `Anonimizar_borra_todo_identificador_es_irreversible_y_emite_su_evento` · A `Anonimizar_borra_al_titular_y_el_texto_libre_conserva_los_montos_y_queda_registrado` · I `Anonimizar_no_deja_rastro_de_los_datos_del_titular_en_ninguna_tabla` | GEN-12 |
| R43 | Los eventos no llevan datos personales | R `Los_eventos_de_dominio_no_tienen_campos_de_texto_libre` · I `Los_eventos_de_la_bandeja_no_llevan_datos_personales` | GEN-12 |

## Eventos, persistencia y arquitectura

| Regla | Prueba(s) |
|---|---|
| El cambio de estado y su evento se guardan en la misma transacción; un conflicto no deja ninguno | I `Cada_cambio_deja_su_evento_en_la_bandeja_en_la_misma_confirmacion` · `Dos_cambios_concurrentes_al_mismo_siniestro_uno_gana_y_el_otro_es_conflicto_sin_dejar_eventos` |
| Entrega al menos una vez con efecto una sola vez (bandeja de entrada) | I `Un_mensaje_entregado_dos_veces_se_aplica_una_sola_vez` |
| Reintento con espera creciente y mensaje muerto con su error | I `Un_consumidor_que_falla_se_reintenta_con_espera_creciente_y_termina_muerto_con_su_error` |
| Despachadores concurrentes sin duplicar y cuadre: proyección = Σ reservas vigentes | I `Dos_despachadores_a_la_vez_no_procesan_un_mensaje_dos_veces_y_la_proyeccion_cuadra` · W `El_trabajador_de_la_bandeja_lleva_la_reserva_al_tablero` |
| Leer y confirmar sin cambios no reescribe la fila | I `Una_lectura_confirmada_no_reescribe_la_fila` |
| Un conflicto de concurrencia es 409 con su código | W `Un_conflicto_de_concurrencia_sale_como_409_con_su_codigo` |
| El catálogo del código y las condiciones generales no se separan | D `Cada_tipo_del_catalogo_esta_en_su_clausula_con_el_mismo_ramo_cobertura_y_datos` · `Cada_exclusion_del_catalogo_esta_en_su_clausula_con_las_coberturas_a_las_que_aplica` · `Las_clausulas_generales_que_cita_el_dominio_existen` · S `El_corpus_incrustado_trae_todas_las_clausulas_con_sus_metadatos_y_sin_el_comentario` |
| Capas: dominio sólo BCL y sin E/S; aplicación sin infraestructura ni paquetes; EF y filas no salen de la infraestructura | R `ArquitecturaTests` (8 pruebas) |
| Sólo `SystemClock` lee el reloj del sistema | R `Nadie_usa_el_reloj_del_sistema_salvo_SystemClock` |
| Sin un secreto la API no arranca | W `Sin_un_secreto_la_API_no_arranca` (3 casos) |
