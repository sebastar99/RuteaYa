using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuteaYa.Entidades.Migrations
{
    /// <inheritdoc />
    public partial class ViajesYParadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Viajes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Destino = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HoraSalida = table.Column<TimeSpan>(type: "time", nullable: false),
                    HoraLlegada = table.Column<TimeSpan>(type: "time", nullable: false),
                    CantMaxPaquetes = table.Column<int>(type: "int", nullable: false),
                    PesoMax = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DesvioMaxKm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Viajes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiasViaje",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViajeId = table.Column<int>(type: "int", nullable: false),
                    DiaSemana = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiasViaje", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiasViaje_Viajes_ViajeId",
                        column: x => x.ViajeId,
                        principalTable: "Viajes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Paradas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViajeId = table.Column<int>(type: "int", nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paradas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Paradas_Viajes_ViajeId",
                        column: x => x.ViajeId,
                        principalTable: "Viajes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiasViaje_ViajeId_DiaSemana",
                table: "DiasViaje",
                columns: new[] { "ViajeId", "DiaSemana" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Paradas_ViajeId_Orden",
                table: "Paradas",
                columns: new[] { "ViajeId", "Orden" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiasViaje");

            migrationBuilder.DropTable(
                name: "Paradas");

            migrationBuilder.DropTable(
                name: "Viajes");
        }
    }
}
