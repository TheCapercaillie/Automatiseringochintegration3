using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ITSystem
{
    public class OrdersDbContext : DbContext
    {
        public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
        {

        }

        public DbSet<Order> Orders { get; set; }
        public DbSet<MachineRuntime> MachineRuntimes { get; set; } 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(o => o.Progress).HasDefaultValue(0);
            modelBuilder.Entity<Order>().Property(o => o.RuntimeSeconds).IsRequired(false);
            modelBuilder.Entity<MachineRuntime>().ToTable("MachineRuntimes");
        }
    }
}
