using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Models;

namespace RequisitaPlus.Web.Data;

public class RequisitosContext(DbContextOptions<RequisitosContext> options) : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<Requisito> Requisitos => Set<Requisito>();
    public DbSet<AnaliseRequisito> AnalisesRequisitos => Set<AnaliseRequisito>();
    public DbSet<DecisaoSugestao> DecisoesSugestoes => Set<DecisaoSugestao>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AnaliseRequisito>()
            .HasOne(a => a.Requisito).WithMany().HasForeignKey(a => a.RequisitoId);
        builder.Entity<AnaliseRequisito>()
            .HasIndex(a => new { a.RequisitoId, a.DataAnalise });
        builder.Entity<DecisaoSugestao>()
            .HasOne(d => d.AnaliseRequisito).WithMany(a => a.Decisoes)
            .HasForeignKey(d => d.AnaliseRequisitoId);
        builder.Entity<DecisaoSugestao>()
            .HasIndex(d => new { d.AnaliseRequisitoId, d.IndiceSugestao }).IsUnique();
    }
}
