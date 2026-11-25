using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Perito.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "perito");

            migrationBuilder.CreateTable(
                name: "ajustadores",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    especialidades = table.Column<List<string>>(type: "text[]", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustadores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asegurados",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    rfc_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    correo_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    telefono_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    clabe_cifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    anonimizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asegurados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_de_salida",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    agregado = table.Column<Guid>(type: "uuid", nullable: false),
                    contenido = table.Column<string>(type: "jsonb", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    siguiente_intento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "text", nullable: true),
                    muerto = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_de_salida", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_recibidos",
                schema: "perito",
                columns: table => new
                {
                    consumidor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    mensaje_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_recibidos", x => new { x.consumidor, x.mensaje_id });
                });

            migrationBuilder.CreateTable(
                name: "propuestas_de_triaje",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    siniestro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    documento = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_propuestas_de_triaje", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registro_de_accesos",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "text", nullable: false),
                    rol = table.Column<string>(type: "text", nullable: false),
                    recurso = table.Column<string>(type: "text", nullable: false),
                    recurso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finalidad = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registro_de_accesos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saldos_de_reserva",
                schema: "perito",
                columns: table => new
                {
                    ramo = table.Column<string>(type: "text", nullable: false),
                    reserva_vigente = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
                    siniestros_con_reserva = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saldos_de_reserva", x => x.ramo);
                });

            migrationBuilder.CreateTable(
                name: "polizas",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ramo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    asegurado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    fin_vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    prima_pagada_hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_cancelacion = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_polizas", x => x.id);
                    table.ForeignKey(
                        name: "FK_polizas_asegurados_asegurado_id",
                        column: x => x.asegurado_id,
                        principalSchema: "perito",
                        principalTable: "asegurados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coberturas",
                schema: "perito",
                columns: table => new
                {
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    suma_asegurada = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    tipo_de_deducible = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_de_deducible = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    coaseguro = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coberturas", x => new { x.poliza_id, x.codigo });
                    table.ForeignKey(
                        name: "FK_coberturas_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "perito",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "siniestros",
                schema: "perito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asegurado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ramo = table.Column<string>(type: "text", nullable: false),
                    fecha_ocurrencia = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_aviso = table.Column<DateOnly>(type: "date", nullable: false),
                    descripcion_cifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    lesiones_cifradas = table.Column<byte[]>(type: "bytea", nullable: true),
                    estimacion = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    cobertura = table.Column<string>(type: "text", nullable: true),
                    motivo_no_elegible = table.Column<string>(type: "text", nullable: true),
                    indicios = table.Column<List<string>>(type: "text[]", nullable: false),
                    triado_por = table.Column<string>(type: "text", nullable: true),
                    propuesta_aceptada = table.Column<Guid>(type: "uuid", nullable: true),
                    ajustador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    datos_recibidos = table.Column<List<string>>(type: "text[]", nullable: false),
                    dictamen_procedente = table.Column<bool>(type: "boolean", nullable: true),
                    dictamen_motivo = table.Column<string>(type: "text", nullable: true),
                    dictamen_clausula = table.Column<string>(type: "text", nullable: true),
                    perdida_ajustada = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    indemnizacion = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    fecha_dictamen = table.Column<DateOnly>(type: "date", nullable: true),
                    dictaminado_por = table.Column<string>(type: "text", nullable: true),
                    exclusiones_confirmadas = table.Column<List<string>>(type: "text[]", nullable: false),
                    fecha_limite_de_pago = table.Column<DateOnly>(type: "date", nullable: true),
                    pago_referencia = table.Column<string>(type: "text", nullable: true),
                    pago_monto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: true),
                    pagado_por = table.Column<string>(type: "text", nullable: true),
                    pago_fuera_de_plazo = table.Column<bool>(type: "boolean", nullable: true),
                    fecha_rechazo = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siniestros", x => x.id);
                    table.ForeignKey(
                        name: "FK_siniestros_ajustadores_ajustador_id",
                        column: x => x.ajustador_id,
                        principalSchema: "perito",
                        principalTable: "ajustadores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siniestros_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "perito",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_de_reserva",
                schema: "perito",
                columns: table => new
                {
                    siniestro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_de_reserva", x => new { x.siniestro_id, x.secuencia });
                    table.ForeignKey(
                        name: "FK_movimientos_de_reserva_siniestros_siniestro_id",
                        column: x => x.siniestro_id,
                        principalSchema: "perito",
                        principalTable: "siniestros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_de_salida_ocurrido_en_secuencia",
                schema: "perito",
                table: "mensajes_de_salida",
                columns: new[] { "ocurrido_en", "secuencia" },
                filter: "procesado_en IS NULL AND NOT muerto");

            migrationBuilder.CreateIndex(
                name: "IX_polizas_asegurado_id",
                schema: "perito",
                table: "polizas",
                column: "asegurado_id");

            migrationBuilder.CreateIndex(
                name: "IX_polizas_folio",
                schema: "perito",
                table: "polizas",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_propuestas_de_triaje_siniestro_id",
                schema: "perito",
                table: "propuestas_de_triaje",
                column: "siniestro_id");

            migrationBuilder.CreateIndex(
                name: "IX_registro_de_accesos_titular_id_fecha",
                schema: "perito",
                table: "registro_de_accesos",
                columns: new[] { "titular_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_siniestros_ajustador_id_estado",
                schema: "perito",
                table: "siniestros",
                columns: new[] { "ajustador_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "IX_siniestros_asegurado_id",
                schema: "perito",
                table: "siniestros",
                column: "asegurado_id");

            migrationBuilder.CreateIndex(
                name: "IX_siniestros_poliza_id_fecha_ocurrencia",
                schema: "perito",
                table: "siniestros",
                columns: new[] { "poliza_id", "fecha_ocurrencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coberturas",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "mensajes_de_salida",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "mensajes_recibidos",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "movimientos_de_reserva",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "propuestas_de_triaje",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "registro_de_accesos",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "saldos_de_reserva",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "siniestros",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "ajustadores",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "polizas",
                schema: "perito");

            migrationBuilder.DropTable(
                name: "asegurados",
                schema: "perito");
        }
    }
}
