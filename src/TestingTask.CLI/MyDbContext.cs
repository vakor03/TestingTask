using Microsoft.EntityFrameworkCore;

namespace TestingTask.CLI;

public class MyDbContext(DbContextOptions<MyDbContext> options) : DbContext(options) {
    public DbSet<Product> Products { get; set; }
}