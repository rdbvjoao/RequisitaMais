using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RequisitaPlus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPropriedadeOpcional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProprietarioId",
                table: "Requisitos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VersaoConcorrencia",
                table: "Requisitos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Requisitos_ProprietarioId",
                table: "Requisitos",
                column: "ProprietarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requisitos_AspNetUsers_ProprietarioId",
                table: "Requisitos",
                column: "ProprietarioId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requisitos_AspNetUsers_ProprietarioId",
                table: "Requisitos");

            migrationBuilder.DropIndex(
                name: "IX_Requisitos_ProprietarioId",
                table: "Requisitos");

            migrationBuilder.DropColumn(
                name: "ProprietarioId",
                table: "Requisitos");

            migrationBuilder.DropColumn(
                name: "VersaoConcorrencia",
                table: "Requisitos");
        }
    }
}
