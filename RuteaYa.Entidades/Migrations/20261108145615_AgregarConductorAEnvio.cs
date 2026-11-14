using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuteaYa.Entidades.Migrations
{
    /// <inheritdoc />
    public partial class AgregarConductorAEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConductorId",
                table: "Envios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_ConductorId",
                table: "Envios",
                column: "ConductorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_Usuarios_ConductorId",
                table: "Envios",
                column: "ConductorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_Usuarios_ConductorId",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_ConductorId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "ConductorId",
                table: "Envios");
        }
    }
}
