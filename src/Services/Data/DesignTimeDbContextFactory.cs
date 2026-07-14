using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kiriha.Services.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        // Use a dummy path for design-time
        optionsBuilder.UseSqlite("Data Source=design_time.db");

        return new AppDbContext(optionsBuilder.Options);
    }
}
