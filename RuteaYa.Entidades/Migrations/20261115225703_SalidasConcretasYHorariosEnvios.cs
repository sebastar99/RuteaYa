using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuteaYa.Entidades.Migrations
{
    /// <inheritdoc />
    public partial class SalidasConcretasYHorariosEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntregaLimiteConOffset",
                table: "Envios",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiroDisponibleDesde",
                table: "Envios",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RetiroSinRestriccion",
                table: "Envios",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SalidaViajeId",
                table: "Envios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalidasViaje",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViajeId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaHora = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LlegadaMaxima = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LlegadaSinRestriccion = table.Column<bool>(type: "bit", nullable: false),
                    Disponible = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalidasViaje", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalidasViaje_Viajes_ViajeId",
                        column: x => x.ViajeId,
                        principalTable: "Viajes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Envios_SalidaViajeId",
                table: "Envios",
                column: "SalidaViajeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalidasViaje_ViajeId_Fecha",
                table: "SalidasViaje",
                columns: new[] { "ViajeId", "Fecha" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_SalidasViaje_SalidaViajeId",
                table: "Envios",
                column: "SalidaViajeId",
                principalTable: "SalidasViaje",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_SalidasViaje_SalidaViajeId",
                table: "Envios");

            migrationBuilder.DropTable(
                name: "SalidasViaje");

            migrationBuilder.DropIndex(
                name: "IX_Envios_SalidaViajeId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "EntregaLimiteConOffset",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "RetiroDisponibleDesde",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "RetiroSinRestriccion",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "SalidaViajeId",
                table: "Envios");
        }
    }
}
