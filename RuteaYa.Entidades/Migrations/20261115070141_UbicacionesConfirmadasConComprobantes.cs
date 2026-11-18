using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuteaYa.Entidades.Migrations
{
    /// <inheritdoc />
    public partial class UbicacionesConfirmadasConComprobantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinoUbicacionId",
                table: "Viajes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrigenUbicacionId",
                table: "Viajes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UbicacionConfirmadaId",
                table: "Paradas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntregaUbicacionId",
                table: "Envios",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetiroUbicacionId",
                table: "Envios",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UbicacionesConfirmadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SeleccionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    FormularioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TipoRecurso = table.Column<int>(type: "int", nullable: false),
                    RecursoId = table.Column<int>(type: "int", nullable: true),
                    Campo = table.Column<int>(type: "int", nullable: false),
                    ClaveCampo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RevisionRecurso = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    IdHere = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DireccionNormalizada = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PosicionLatitud = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    PosicionLongitud = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    NavegacionLatitud = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    NavegacionLongitud = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    AccesoElegido = table.Column<int>(type: "int", nullable: false),
                    TipoResultado = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TipoNumeroPuerta = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PosicionEstimada = table.Column<bool>(type: "bit", nullable: false),
                    NumeroPuertaCorregido = table.Column<bool>(type: "bit", nullable: false),
                    Proveedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfirmadaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VenceEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumidaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UbicacionesConfirmadas", x => x.Id);
                    table.CheckConstraint("CK_Ubicacion_Acceso", "[AccesoElegido] >= -1");
                    table.CheckConstraint("CK_Ubicacion_Ambito", "([TipoRecurso] = 1 AND [Campo] IN (1,2,3)) OR ([TipoRecurso] = 2 AND [Campo] IN (4,5))");
                    table.CheckConstraint("CK_Ubicacion_Navegacion", "[NavegacionLatitud] BETWEEN -90 AND 90 AND [NavegacionLongitud] BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Ubicacion_Posicion", "[PosicionLatitud] BETWEEN -90 AND 90 AND [PosicionLongitud] BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "FK_UbicacionesConfirmadas_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Viajes_DestinoUbicacionId",
                table: "Viajes",
                column: "DestinoUbicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Viajes_OrigenUbicacionId",
                table: "Viajes",
                column: "OrigenUbicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Paradas_UbicacionConfirmadaId",
                table: "Paradas",
                column: "UbicacionConfirmadaId");

            migrationBuilder.CreateIndex(
                name: "IX_Envios_EntregaUbicacionId",
                table: "Envios",
                column: "EntregaUbicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Envios_RetiroUbicacionId",
                table: "Envios",
                column: "RetiroUbicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_UbicacionesConfirmadas_SeleccionHash",
                table: "UbicacionesConfirmadas",
                column: "SeleccionHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UbicacionesConfirmadas_TokenHash",
                table: "UbicacionesConfirmadas",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UbicacionesConfirmadas_UsuarioId",
                table: "UbicacionesConfirmadas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_UbicacionesConfirmadas_VenceEnUtc",
                table: "UbicacionesConfirmadas",
                column: "VenceEnUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_UbicacionesConfirmadas_EntregaUbicacionId",
                table: "Envios",
                column: "EntregaUbicacionId",
                principalTable: "UbicacionesConfirmadas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_UbicacionesConfirmadas_RetiroUbicacionId",
                table: "Envios",
                column: "RetiroUbicacionId",
                principalTable: "UbicacionesConfirmadas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Paradas_UbicacionesConfirmadas_UbicacionConfirmadaId",
                table: "Paradas",
                column: "UbicacionConfirmadaId",
                principalTable: "UbicacionesConfirmadas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Viajes_UbicacionesConfirmadas_DestinoUbicacionId",
                table: "Viajes",
                column: "DestinoUbicacionId",
                principalTable: "UbicacionesConfirmadas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Viajes_UbicacionesConfirmadas_OrigenUbicacionId",
                table: "Viajes",
                column: "OrigenUbicacionId",
                principalTable: "UbicacionesConfirmadas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_UbicacionesConfirmadas_EntregaUbicacionId",
                table: "Envios");

            migrationBuilder.DropForeignKey(
                name: "FK_Envios_UbicacionesConfirmadas_RetiroUbicacionId",
                table: "Envios");

            migrationBuilder.DropForeignKey(
                name: "FK_Paradas_UbicacionesConfirmadas_UbicacionConfirmadaId",
                table: "Paradas");

            migrationBuilder.DropForeignKey(
                name: "FK_Viajes_UbicacionesConfirmadas_DestinoUbicacionId",
                table: "Viajes");

            migrationBuilder.DropForeignKey(
                name: "FK_Viajes_UbicacionesConfirmadas_OrigenUbicacionId",
                table: "Viajes");

            migrationBuilder.DropTable(
                name: "UbicacionesConfirmadas");

            migrationBuilder.DropIndex(
                name: "IX_Viajes_DestinoUbicacionId",
                table: "Viajes");

            migrationBuilder.DropIndex(
                name: "IX_Viajes_OrigenUbicacionId",
                table: "Viajes");

            migrationBuilder.DropIndex(
                name: "IX_Paradas_UbicacionConfirmadaId",
                table: "Paradas");

            migrationBuilder.DropIndex(
                name: "IX_Envios_EntregaUbicacionId",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_RetiroUbicacionId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "DestinoUbicacionId",
                table: "Viajes");

            migrationBuilder.DropColumn(
                name: "OrigenUbicacionId",
                table: "Viajes");

            migrationBuilder.DropColumn(
                name: "UbicacionConfirmadaId",
                table: "Paradas");

            migrationBuilder.DropColumn(
                name: "EntregaUbicacionId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "RetiroUbicacionId",
                table: "Envios");
        }
    }
}
