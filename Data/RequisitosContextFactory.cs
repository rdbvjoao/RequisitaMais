using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RequisitaPlus.Web.Data;

// Ferramentas de migrations nunca inicializam a aplicação nem abrem o banco real.
public sealed class RequisitosContextFactory : IDesignTimeDbContextFactory<RequisitosContext>
{
    public RequisitosContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source=:memory:").Options);
}
